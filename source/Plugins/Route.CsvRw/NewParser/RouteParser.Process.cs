//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2026, The OpenBVE Project contributors
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
//ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
//ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

namespace CsvRwRouteParser.New
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using OpenBveApi;
    using OpenBveApi.Interface;
    using OpenBveApi.Math;
    using OpenBveApi.Routes;
    using RouteManager2.Stations;

    internal partial class RouteParser
    {
        /// <summary>
        /// Single-pass route processing pipeline:
        /// Load → Sort → Process (separate + dispatch inline)
        /// Replaces the old PreprocessOptions + PreprocessLex + 2-pass Dispatch.
        /// </summary>
        private void ParseRoute(string FileName, System.Text.Encoding Encoding, ref RouteData Data, bool PreviewOnly)
        {
            // --- Step 1: Load file and build expression list ---
            List<string> Lines = System.IO.File.ReadAllLines(FileName, Encoding).ToList();
            IList<Expression> Expressions = LoadExpressions(FileName, Lines, Encoding);

            // --- Step 2: Process Options first (need UnitOfLength before sorting) ---
            double[] UnitOfLength = { 1.0 };
            Data.UnitOfSpeed = 0.277777777777778;
            ScanOptions(Expressions, ref Data, ref UnitOfLength, PreviewOnly);

            // --- Step 3: Sort by track position ---
            SortByTrackPosition(UnitOfLength, ref Expressions);

            // --- Step 4: Single pass — separate + dispatch ---
            ProcessExpressions(FileName, Encoding, Expressions, UnitOfLength, ref Data, PreviewOnly);

            CurrentRoute.UnitOfLength = UnitOfLength;
        }

        // =====================================================================
        //  LOAD — read file, split lines into expressions, handle $Sub/$Include
        // =====================================================================
        private IList<Expression> LoadExpressions(string FileName, List<string> Lines, System.Text.Encoding Encoding)
        {
            // Split each line into expressions (handles CSV commas, RW @-separators)
            SplitLines(FileName, Lines, out IList<Expression> Expressions);
            // Evaluate $If/$Else/$EndIf, $Rnd, $Chr, $Sub, $Include
            EvaluateControlCommands(FileName, Encoding, ref Expressions);
            return Expressions;
        }

        /// <summary>Splits raw lines into Expression objects.</summary>
        /// <remarks>PERF: uses IndexOf instead of Regex for .Load detection.</remarks>
        private void SplitLines(string FileName, List<string> Lines, out IList<Expression> Expressions, bool AllowRwRouteDescription = true, double trackPositionOffset = 0.0)
        {
            Expressions = new List<Expression>(20000);

            // --- RW: strip full-line comments (before '=' at bracket level 0) ---
            if (IsRW)
            {
                for (int i = 0; i < Lines.Count; i++)
                {
                    int level = 0;
                    for (int j = 0; j < Lines[i].Length; j++)
                    {
                        char c = Lines[i][j];
                        if (c == '(') level++;
                        else if (c == ')') level--;
                        else if (c == ';' && level == 0)
                        {
                            Lines[i] = Lines[i].Substring(0, j).TrimEnd();
                            break;
                        }
                        else if (c == '=' && level == 0)
                        {
                            break;
                        }
                    }
                }
            }

            for (int i = 0; i < Lines.Count; i++)
            {
                // Remove null characters (found in old DOS-era routes)
                // PERF: only allocate if null chars actually present
                if (Lines[i].IndexOf('\0') >= 0)
                {
                    Lines[i] = Lines[i].Replace("\0", string.Empty);
                }

                // RW: skip route description block (text before first [ or $)
                if (IsRW && AllowRwRouteDescription)
                {
                    if (Lines[i].StartsWith("[", StringComparison.Ordinal) && Lines[i].IndexOf(']') > 0 ||
                        Lines[i].StartsWith("$"))
                    {
                        AllowRwRouteDescription = false;
                        CurrentRoute.Comment = CurrentRoute.Comment.Trim();
                    }
                    else
                    {
                        if (CurrentRoute.Comment.Length != 0)
                        {
                            CurrentRoute.Comment += "\n";
                        }
                        CurrentRoute.Comment += Lines[i];
                        continue;
                    }
                }

                // --- SplitLineHack: if line has multiple .Load, split on commas ---
                // PERF: use IndexOf instead of Regex.Matches
                if (SplitLineHack && Lines[i].IndexOf(".Load", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Count commas to decide if split is needed
                    int loadCount = 0;
                    for (int k = 0; k < Lines[i].Length; k++)
                    {
                        if (Lines[i][k] == ',')
                        {
                            loadCount++;
                            if (loadCount > 0)
                            {
                                // Check if comma is outside brackets
                                // (simplified — we already know .Load is present)
                            }
                        }
                    }

                    // Quick check: does it have commas at all?
                    if (Lines[i].IndexOf(',') >= 0)
                    {
                        string[] parts = Lines[i].Split(',');
                        Lines.RemoveAt(i);
                        for (int j = 0; j < parts.Length; j++)
                        {
                            string trimmed = parts[j].Trim();
                            if (trimmed.Length > 0)
                            {
                                Lines.Insert(i, trimmed);
                                i++;
                            }
                        }
                        i--; // adjust for loop increment
                        continue;
                    }
                }

                // --- Parse bracket-level expressions from the line ---
                int bracketLevel = 0;
                int start = 0, col = 0;
                for (int j = 0; j < Lines[i].Length; j++)
                {
                    char c = Lines[i][j];
                    if (c == '(')
                    {
                        bracketLevel++;
                    }
                    else if (c == ')')
                    {
                        if (Plugin.CurrentOptions.EnableBveTsHacks)
                        {
                            if (bracketLevel > 0)
                            {
                                bracketLevel--;
                            }
                            else
                            {
                                Plugin.CurrentHost.AddMessage(MessageType.Warning, false,
                                    "Invalid additional closing parenthesis encountered at line " + i +
                                    " character " + j + " in file " + FileName);
                            }
                        }
                        else
                        {
                            bracketLevel--;
                        }
                    }
                    else if (c == ',' && bracketLevel == 0 && !IsRW)
                    {
                        // CSV: comma separates expressions
                        string t = Lines[i].Substring(start, j - start).Trim();
                        if (t.Length > 0 && !t.StartsWith(";"))
                        {
                            Expressions.Add(new Expression(FileName, t, i + 1, col + 1, trackPositionOffset));
                        }
                        start = j + 1;
                        col++;
                    }
                    else if (c == '@' && IsRW)
                    {
                        if (Plugin.CurrentOptions.EnableBveTsHacks && bracketLevel != 0)
                        {
                            Plugin.CurrentHost.AddMessage(MessageType.Warning, false,
                                "Expression was not closed correctly at line " + i +
                                " character " + j + " in file " + FileName);
                            bracketLevel = 0;
                        }
                        if (bracketLevel == 0)
                        {
                            string t = Lines[i].Substring(start, j - start).Trim();
                            if (t.Length > 0 && !t.StartsWith(";"))
                            {
                                Expressions.Add(new Expression(FileName, t, i + 1, col + 1, trackPositionOffset));
                            }
                            start = j + 1;
                            col++;
                        }
                    }
                }
                // Remaining text after last separator
                if (Lines[i].Length - start > 0)
                {
                    string t = Lines[i].Substring(start).Trim();
                    if (t.Length > 0 && !t.StartsWith(";"))
                    {
                        Expressions.Add(new Expression(FileName, t, i + 1, col + 1, trackPositionOffset));
                    }
                }
            }
        }

        /// <summary>Evaluates $If/$Else/$EndIf, $Rnd, $Chr, $Sub, $Include directives.</summary>
        private void EvaluateControlCommands(string FileName, System.Text.Encoding Encoding, ref IList<Expression> Expressions)
        {
            string[] Subs = new string[16];
            int openIfs = 0;
            for (int i = 0; i < Expressions.Count; i++)
            {
                if (Expressions[i].Skip) continue;

                string epilog = " at line " + Expressions[i].Line.ToString(Culture) + ", column " + Expressions[i].Column.ToString(Culture) + " in file " + Expressions[i].File;
                bool skip = false;

                for (int j = Expressions[i].Text.Length - 1; j >= 0; j--)
                {
                    if (Expressions[i].Text[j] != '$') continue;

                    // Find command name (up to '(' or '/')
                    int k;
                    for (k = j + 1; k < Expressions[i].Text.Length; k++)
                    {
                        if (Expressions[i].Text[k] == '(') break;
                        if (Expressions[i].Text[k] == '/' || Expressions[i].Text[k] == '\\')
                        {
                            k = Expressions[i].Text.Length + 1;
                            break;
                        }
                    }
                    if (k > Expressions[i].Text.Length) break;

                    string cmdText = Expressions[i].Text.Substring(j, k - j).TrimEnd();
                    if (cmdText[0] != '$' || !Enum.TryParse(cmdText.Substring(1), true, out ControlCommands cmd))
                        break;

                    // Find matching closing parenthesis
                    int depth = 1, h;
                    for (h = k + 1; h < Expressions[i].Text.Length; h++)
                    {
                        if (Expressions[i].Text[h] == '(') depth++;
                        else if (Expressions[i].Text[h] == ')')
                        {
                            depth--;
                            if (depth < 0) { skip = true; Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Invalid parenthesis structure in " + cmdText + epilog); }
                        }
                        if (depth <= 0) break;
                    }
                    if (skip) break;
                    if (depth != 0) { Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Invalid parenthesis structure in " + cmdText + epilog); break; }

                    string s = Expressions[i].Text.Substring(k + 1, h - k - 1).Trim();

                    switch (cmd)
                    {
                        case ControlCommands.If:
                        case ControlCommands.ElseIf:
                            if (j != 0)
                            {
                                Plugin.CurrentHost.AddMessage(MessageType.Error, false, "The $" + cmd + " directive must not appear within another statement" + epilog);
                            }
                            else if (double.TryParse(s, System.Globalization.NumberStyles.Float, Culture, out double num))
                            {
                                openIfs++;
                                Expressions[i].Text = string.Empty;
                                if (num == 0.0)
                                {
                                    // Skip until matching $Else/$ElseIf/$EndIf
                                    i++;
                                    int level = 1;
                                    while (i < Expressions.Count)
                                    {
                                        if (Expressions[i].Text.StartsWith("$if", StringComparison.OrdinalIgnoreCase))
                                        {
                                            Expressions[i].Skip = true; level++;
                                        }
                                        else if (Expressions[i].Text.StartsWith("$elseif", StringComparison.OrdinalIgnoreCase))
                                        {
                                            i--; level--; break;
                                        }
                                        else if (Expressions[i].Text.StartsWith("$else", StringComparison.OrdinalIgnoreCase))
                                        {
                                            Expressions[i].Skip = true;
                                            if (level == 1) { level--; break; }
                                        }
                                        else if (Expressions[i].Text.StartsWith("$endif", StringComparison.OrdinalIgnoreCase))
                                        {
                                            Expressions[i].Skip = true; level--;
                                            if (level == 0) { openIfs--; break; }
                                        }
                                        else
                                        {
                                            Expressions[i].Skip = true;
                                        }
                                        i++;
                                    }
                                    if (level != 0)
                                        Plugin.CurrentHost.AddMessage(MessageType.Error, false, "$EndIf missing at the end of the file" + epilog);
                                }
                            }
                            else
                            {
                                Plugin.CurrentHost.AddMessage(MessageType.Error, false, "The $If condition does not evaluate to a number" + epilog);
                            }
                            skip = true;
                            break;

                        case ControlCommands.Else:
                            Expressions[i].Skip = true;
                            if (openIfs != 0)
                            {
                                i++;
                                int level = 1;
                                while (i < Expressions.Count)
                                {
                                    if (Expressions[i].Text.StartsWith("$if", StringComparison.OrdinalIgnoreCase)) { Expressions[i].Skip = true; level++; }
                                    else if (Expressions[i].Text.StartsWith("$else", StringComparison.OrdinalIgnoreCase))
                                    {
                                        Expressions[i].Skip = true;
                                        if (level == 1) { Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Duplicate $Else encountered" + epilog); }
                                    }
                                    else if (Expressions[i].Text.StartsWith("$endif", StringComparison.OrdinalIgnoreCase))
                                    {
                                        Expressions[i].Skip = true; level--;
                                        if (level == 0) { openIfs--; break; }
                                    }
                                    else { Expressions[i].Skip = true; }
                                    i++;
                                }
                                if (level != 0)
                                    Plugin.CurrentHost.AddMessage(MessageType.Error, false, "$EndIf missing at the end of the file" + epilog);
                            }
                            else
                            {
                                Plugin.CurrentHost.AddMessage(MessageType.Error, false, "$Else without matching $If encountered" + epilog);
                            }
                            skip = true;
                            break;

                        case ControlCommands.EndIf:
                            Expressions[i].Skip = true;
                            if (openIfs != 0) openIfs--;
                            else Plugin.CurrentHost.AddMessage(MessageType.Error, false, "$EndIf without matching $If encountered" + epilog);
                            skip = true;
                            break;

                        case ControlCommands.Include:
                            if (j != 0) { Plugin.CurrentHost.AddMessage(MessageType.Error, false, "The $Include directive must not appear within another statement" + epilog); skip = true; break; }
                            string[] args = s.Split(';');
                            for (int ia = 0; ia < args.Length; ia++) args[ia] = args[ia].Trim();
                            int count = (args.Length + 1) / 2;
                            string[] files = new string[count];
                            double[] weights = new double[count];
                            double[] offsets = new double[count];
                            double weightsTotal = 0.0;
                            for (int ia = 0; ia < count; ia++)
                            {
                                string file; double offset;
                                int colon = args[2 * ia].IndexOf(':');
                                if (colon >= 0)
                                {
                                    file = args[2 * ia].Substring(0, colon).TrimEnd();
                                    string value = args[2 * ia].Substring(colon + 1).TrimStart();
                                    if (!double.TryParse(value, System.Globalization.NumberStyles.Float, Culture, out offset))
                                    { skip = true; Plugin.CurrentHost.AddMessage(MessageType.Error, false, "The track position offset " + value + " is invalid in " + cmdText + epilog); break; }
                                }
                                else { file = args[2 * ia]; offset = 0.0; }
                                try { files[ia] = Path.CombineFile(System.IO.Path.GetDirectoryName(FileName), file); }
                                catch { skip = true; Plugin.CurrentHost.AddMessage(MessageType.Error, false, "The filename " + file + " contains invalid characters in " + cmdText + epilog); break; }
                                offsets[ia] = offset;
                                if (!System.IO.File.Exists(files[ia])) { skip = true; Plugin.CurrentHost.AddMessage(MessageType.Error, false, "The file " + file + " could not be found in " + cmdText + epilog); break; }
                                if (2 * ia + 1 < args.Length)
                                {
                                    if (!NumberFormats.TryParseDoubleVb6(args[2 * ia + 1], out weights[ia])) { skip = true; Plugin.CurrentHost.AddMessage(MessageType.Error, false, "A weight is invalid in " + cmdText + epilog); break; }
                                    if (weights[ia] <= 0.0) { skip = true; Plugin.CurrentHost.AddMessage(MessageType.Error, false, "A weight is not positive in " + cmdText + epilog); break; }
                                    weightsTotal += weights[ia];
                                }
                                else { weights[ia] = 1.0; weightsTotal += 1.0; }
                            }
                            if (count == 0) { skip = true; Plugin.CurrentHost.AddMessage(MessageType.Error, false, "No file was specified in " + cmdText + epilog); break; }
                            if (!skip)
                            {
                                double rnd = Plugin.CurrentHost.Random.NextDouble() * weightsTotal;
                                double val = 0.0; int chosen = 0;
                                for (int ia = 0; ia < count; ia++) { val += weights[ia]; if (val > rnd) { chosen = ia; break; } }
                                System.Text.Encoding incEnc = TextEncoding.GetSystemEncodingFromFile(files[chosen]);
                                List<string> incLines = System.IO.File.ReadAllLines(files[chosen], incEnc).ToList();
                                SplitLines(files[chosen], incLines, out IList<Expression> incExprs, false, offsets[chosen] + Expressions[i].TrackPositionOffset);
                                Expressions[i].Skip = true;
                                if (incExprs.Count != 0)
                                {
                                    ((List<Expression>)Expressions).InsertRange(i, incExprs);
                                    i--;
                                }
                            }
                            skip = true;
                            break;

                        case ControlCommands.Chr:
                            if (NumberFormats.TryParseIntVb6(s, out int chrX) && chrX >= 0)
                            {
                                Expressions[i].Text = Expressions[i].Text.Substring(0, j) + char.ConvertFromUtf32(chrX) + Expressions[i].Text.Substring(h + 1);
                            }
                            else
                            {
                                Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Index is invalid in " + cmdText + epilog);
                                skip = true;
                            }
                            break;

                        case ControlCommands.ChrAscii:
                            if (NumberFormats.TryParseIntVb6(s, out int ascX) && ascX >= 0 && ascX <= 127)
                            {
                                Expressions[i].Text = Expressions[i].Text.Substring(0, j) + char.ConvertFromUtf32(ascX) + Expressions[i].Text.Substring(h + 1);
                            }
                            else
                            {
                                Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Index does not correspond to a valid ASCII character in " + cmdText + epilog);
                                skip = true;
                            }
                            break;

                        case ControlCommands.Rnd:
                            int semi = s.IndexOf(";", StringComparison.Ordinal);
                            if (semi >= 0)
                            {
                                string s1 = s.Substring(0, semi).TrimEnd();
                                string s2 = s.Substring(semi + 1).TrimStart();
                                if (NumberFormats.TryParseIntVb6(s1, out int rMin) && NumberFormats.TryParseIntVb6(s2, out int rMax))
                                {
                                    int rVal = rMin + (int)Math.Floor(Plugin.CurrentHost.Random.NextDouble() * (rMax - rMin + 1));
                                    Expressions[i].Text = Expressions[i].Text.Substring(0, j) + rVal.ToString(Culture) + Expressions[i].Text.Substring(h + 1);
                                }
                                else
                                {
                                    Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Index is invalid in " + cmdText + epilog);
                                    skip = true;
                                }
                            }
                            else
                            {
                                if (NumberFormats.TryParseIntVb6(s, out int rSingle))
                                {
                                    Expressions[i].Text = Expressions[i].Text.Substring(0, j) + rSingle.ToString(Culture) + Expressions[i].Text.Substring(h + 1);
                                }
                                else
                                {
                                    Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Two arguments are expected in " + cmdText + epilog);
                                    skip = true;
                                }
                            }
                            break;

                        case ControlCommands.Sub:
                            int subDepth = 0; bool hasEquals = false; int eqPos = 0;
                            for (eqPos = h + 1; eqPos < Expressions[i].Text.Length; eqPos++)
                            {
                                char sc = Expressions[i].Text[eqPos];
                                if (sc == '(') subDepth++;
                                else if (sc == ')') subDepth--;
                                else if (sc == '=' && subDepth == 0) { hasEquals = true; break; }
                                else if (!char.IsWhiteSpace(sc) && subDepth == 0) { subDepth = -1; break; }
                                if (hasEquals || subDepth < 0) break;
                            }
                            if (hasEquals)
                            {
                                int subEnd = 0;
                                subDepth = 0;
                                for (subEnd = eqPos + 1; subEnd < Expressions[i].Text.Length; subEnd++)
                                {
                                    if (Expressions[i].Text[subEnd] == '(') subDepth++;
                                    else if (Expressions[i].Text[subEnd] == ')') subDepth--;
                                    if (subDepth < 0) break;
                                }
                                if (NumberFormats.TryParseIntVb6(s, out int subIdx) && subIdx >= 0)
                                {
                                    while (subIdx >= Subs.Length) Array.Resize(ref Subs, Subs.Length << 1);
                                    Subs[subIdx] = Expressions[i].Text.Substring(eqPos + 1, subEnd - eqPos - 1).Trim();
                                    Expressions[i].Text = Expressions[i].Text.Substring(0, j) + Expressions[i].Text.Substring(subEnd);
                                }
                                else
                                {
                                    Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Index is expected to be non-negative in " + cmdText + epilog);
                                    skip = true;
                                }
                            }
                            else
                            {
                                if (NumberFormats.TryParseIntVb6(s, out int subRef) && subRef >= 0 && subRef < Subs.Length && Subs[subRef] != null)
                                {
                                    Expressions[i].Text = Expressions[i].Text.Substring(0, j) + Subs[subRef] + Expressions[i].Text.Substring(h + 1);
                                }
                                else
                                {
                                    Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Index is out of range in " + cmdText + epilog);
                                    skip = true;
                                    Expressions[i].Text = Expressions[i].Text.Substring(0, j) + Expressions[i].Text.Substring(h + 1);
                                }
                            }
                            break;
                    }
                    if (skip) break;
                }
            }

            // Trim + mark comment lines introduced by $Chr/$Rnd/$Sub
            for (int i = 0; i < Expressions.Count; i++)
            {
                Expressions[i].Text = Expressions[i].Text.Trim();
                if (Expressions[i].Text.Length > 0 && Expressions[i].Text[0] == ';')
                {
                    Expressions[i].Skip = true;
                }
            }
        }

        // =====================================================================
        //  OPTIONS — quick scan, only processes Options.* commands
        //  Runs BEFORE sort because UnitOfLength is needed for track position parsing.
        //  PERF: only calls Separate on lines containing "options", skips the rest.
        // =====================================================================
        private void ScanOptions(IList<Expression> Expressions, ref RouteData Data, ref double[] UnitOfLength, bool PreviewOnly)
        {
            string section = "";
            bool sectionAlwaysPrefix = false;

            for (int j = 0; j < Expressions.Count; j++)
            {
                if (Expressions[j].Skip) continue;

                // Track section state (cheap string check)
                if (Expressions[j].Text.StartsWith("[") && Expressions[j].Text.EndsWith("]"))
                {
                    section = Expressions[j].Text.Substring(1, Expressions[j].Text.Length - 2).Trim();
                    if (string.Compare(section, "object", StringComparison.OrdinalIgnoreCase) == 0) section = "Structure";
                    else if (string.Compare(section, "railway", StringComparison.OrdinalIgnoreCase) == 0) section = "Track";
                    sectionAlwaysPrefix = true;
                    continue;
                }

                // PERF: quick pre-check — skip lines that definitely don't contain options
                // This avoids calling Separate on 99%+ of expressions.
                if (Expressions[j].Text.IndexOf("options", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                // Only now do the full separation (rare path)
                if (IsRW) Expressions[j].ConvertRwToCsv(section, sectionAlwaysPrefix);
                Expressions[j].SeparateCommandsAndArguments(out string cmd, out string argSeq, Culture, true, IsRW, section);

                bool isNumber = IsRW && string.Compare(section, "track", StringComparison.OrdinalIgnoreCase) == 0;
                if (isNumber && NumberFormats.TryParseDoubleVb6(cmd, UnitOfLength, out _)) continue;

                string[] args = SplitArguments(argSeq);

                // Resolve section prefix
                if (cmd.ToLowerInvariant() == "with")
                {
                    sectionAlwaysPrefix = false;
                    section = args.Length >= 1 ? args[0] : string.Empty;
                    cmd = null;
                }
                else
                {
                    if (cmd.StartsWith(".")) cmd = section + cmd;
                    else if (sectionAlwaysPrefix) cmd = section + "." + cmd;
                    cmd = cmd.Replace(".Void", string.Empty);
                }

                // Handle indices
                if (cmd != null && cmd.EndsWith(")"))
                {
                    for (int k = cmd.Length - 2; k >= 0; k--)
                    {
                        if (cmd[k] == '(')
                        {
                            string indices = cmd.Substring(k + 1, cmd.Length - k - 2).TrimStart();
                            cmd = cmd.Substring(0, k).TrimEnd();
                            int h = indices.IndexOf(";", StringComparison.Ordinal);
                            if (h >= 0)
                            {
                                string a = indices.Substring(0, h).TrimEnd();
                                string b = indices.Substring(h + 1).TrimStart();
                                if (a.Length > 0 && !NumberFormats.TryParseIntVb6(a, out _)) { cmd = null; break; }
                                if (b.Length > 0 && !NumberFormats.TryParseIntVb6(b, out _)) { cmd = null; }
                            }
                            else if (indices.Length > 0 && !NumberFormats.TryParseIntVb6(indices, out _))
                            {
                                cmd = null;
                            }
                            break;
                        }
                    }
                }

                // Process Options commands
                if (string.IsNullOrEmpty(cmd) || cmd.Length <= 8) continue;
                if (!Enum.TryParse(cmd.Substring(8), true, out OptionsCommand optCmd)) continue;

                switch (optCmd)
                {
                    case OptionsCommand.UnitOfLength:
                        if (args.Length == 0) { Plugin.CurrentHost.AddMessage(MessageType.Error, false, "At least 1 argument is expected in " + cmd); break; }
                        UnitOfLength = new double[args.Length];
                        for (int i = 0; i < args.Length; i++)
                        {
                            UnitOfLength[i] = i == args.Length - 1 ? 1.0 : 0.0;
                            if (args[i].Length > 0 && !NumberFormats.TryParseDoubleVb6(args[i], out UnitOfLength[i]))
                            {
                                Plugin.CurrentHost.AddMessage(MessageType.Error, false, "FactorInMeters" + i + " is invalid in " + cmd);
                                UnitOfLength[i] = i == 0 ? 1.0 : 0.0;
                            }
                            else if (UnitOfLength[i] <= 0.0)
                            {
                                Plugin.CurrentHost.AddMessage(MessageType.Error, false, "FactorInMeters" + i + " is expected to be positive in " + cmd);
                                UnitOfLength[i] = i == args.Length - 1 ? 1.0 : 0.0;
                            }
                        }
                        break;
                    case OptionsCommand.UnitOfSpeed:
                        if (args.Length < 1) { Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Exactly 1 argument is expected in " + cmd); break; }
                        if (NumberFormats.TryParseDoubleVb6(args[0], out double speed) && speed > 0.0)
                        {
                            Data.UnitOfSpeed = speed * 0.277777777777778;
                        }
                        else
                        {
                            Plugin.CurrentHost.AddMessage(MessageType.Error, false, "FactorInKmph is invalid in " + cmd);
                            Data.UnitOfSpeed = 0.277777777777778;
                        }
                        break;
                    case OptionsCommand.ObjectVisibility:
                        if (args.Length >= 1 && NumberFormats.TryParseIntVb6(args[0], out int visMode) && visMode >= 0 && visMode <= 2)
                        {
                            Plugin.CurrentOptions.ObjectDisposalMode = (ObjectDisposalMode)visMode;
                        }
                        break;
                    case OptionsCommand.CompatibleTransparencyMode:
                        if (PreviewOnly) break;
                        if (args.Length >= 1 && NumberFormats.TryParseIntVb6(args[0], out int transMode) && (transMode == 0 || transMode == 1))
                        {
                            Plugin.CurrentOptions.OldTransparencyMode = transMode == 1;
                        }
                        break;
                    case OptionsCommand.EnableBveTsHacks:
                        if (PreviewOnly) break;
                        if (args.Length >= 1 && NumberFormats.TryParseIntVb6(args[0], out int hackMode) && (hackMode == 0 || hackMode == 1))
                        {
                            Plugin.CurrentOptions.EnableBveTsHacks = hackMode == 1;
                        }
                        break;
                    case OptionsCommand.StartingDirection:
                        if (args.Length != 2) { Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Exactly 2 arguments are expected in " + cmd); break; }
                        if (NumberFormats.TryParseDoubleVb6(args[0], out double dirX) && NumberFormats.TryParseDoubleVb6(args[1], out double dirY))
                        {
                            Data.StartingDirection = new OpenBveApi.Math.Vector2(dirX, dirY);
                            if (Data.StartingDirection == OpenBveApi.Math.Vector2.Null)
                            {
                                Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Direction must not be zero in " + cmd);
                                Data.StartingDirection = OpenBveApi.Math.Vector2.Down;
                            }
                        }
                        break;
                }
            }
        }

        // =====================================================================
        //  SORT — reorder expressions by track position
        //  PERF: quick pre-check before calling expensive TryParseDoubleVb6
        // =====================================================================
        private void SortByTrackPosition(double[] unitFactors, ref IList<Expression> Expressions)
        {
            var sorted = new SortedList<double, Expression>(new DuplicateLessThanKeyComparer<double>());
            double currentPos = -1.0, lastPos = -1.0;
            bool checkNumbers = !IsRW;

            for (int i = 0; i < Expressions.Count; i++)
            {
                if (Expressions[i].Skip) continue;

                if (IsRW)
                {
                    if (Expressions[i].Text.StartsWith("[") && Expressions[i].Text.EndsWith("]"))
                    {
                        string s = Expressions[i].Text.Substring(1, Expressions[i].Text.Length - 2).Trim();
                        checkNumbers = string.Compare(s, "Railway", StringComparison.OrdinalIgnoreCase) == 0;
                    }
                }

                // PERF: quick pre-check — if first char is not digit/dot/minus, skip TryParseDoubleVb6
                if (checkNumbers && Expressions[i].Text.Length > 0)
                {
                    char first = Expressions[i].Text[0];
                    if (first >= '0' && first <= '9' || first == '.' || first == '-' || first == '+')
                    {
                        if (NumberFormats.TryParseDouble(Expressions[i].Text, unitFactors, out double x))
                        {
                            x += Expressions[i].TrackPositionOffset;
                            if (x >= 0.0)
                            {
                                // BVE-TS hack: specific route file fixes
                                if (Plugin.CurrentOptions.EnableBveTsHacks)
                                {
                                    string fname = System.IO.Path.GetFileName(Expressions[i].File).ToLowerInvariant();
                                    if (fname == "balloch - dumbarton central special nighttime run.csv" ||
                                        fname == "balloch - dumbarton central summer 2004 morning run.csv")
                                    {
                                        if (x != 0 || currentPos != 4125) currentPos = x;
                                    }
                                    else { currentPos = x; }
                                }
                                else { currentPos = x; }
                            }
                            else
                            {
                                Plugin.CurrentHost.AddMessage(MessageType.Error, false,
                                    "Negative track position encountered at line " + Expressions[i].Line.ToString(Culture) +
                                    ", column " + Expressions[i].Column.ToString(Culture) + " in file " + Expressions[i].File);
                            }
                            continue; // track position lines are not commands
                        }
                    }
                }

                // Non-number line: add with current position
                if (lastPos != currentPos)
                {
                    sorted.Add(currentPos, new Expression(string.Empty, (currentPos / unitFactors[unitFactors.Length - 1]).ToString(Culture), -1, -1, -1));
                    lastPos = currentPos;
                }
                sorted.Add(currentPos, Expressions[i]);
            }
            Expressions = sorted.Values;
        }

        // =====================================================================
        //  PROCESS — single pass over all expressions: separate + dispatch inline
        //  Replaces: PreprocessLex + Dispatch loop 1 (non-track) + Dispatch loop 2 (track)
        // =====================================================================
        private void ProcessExpressions(string FileName, System.Text.Encoding Encoding, IList<Expression> Expressions, double[] UnitOfLength, ref RouteData Data, bool PreviewOnly)
        {
            CurrentStation = -1;
            CurrentStop = -1;
            CurrentSection = 0;
            int blockIndex = 0;
            CurrentRoute.Tracks[0].Direction = TrackDirection.Forwards;
            CurrentRoute.Stations = new RouteStation[] { };
            double progressFactor = Expressions.Count == 0 ? 0.3333 : 0.3333 / Expressions.Count;

            // Pre-setup (same as old dispatch)
            CheckForAvailablePatch(FileName, ref Data, ref Expressions, PreviewOnly);
            if (!PreviewOnly)
            {
                for (int i = 0; i < Plugin.CurrentHost.Plugins.Length; i++)
                {
                    if (Plugin.CurrentHost.Plugins[i].Object != null)
                    {
                        EnabledHacks.BveTsHacks = Plugin.CurrentOptions.EnableBveTsHacks;
                        EnabledHacks.BlackTransparency = true;
                        Plugin.CurrentHost.Plugins[i].Object.SetCompatibilityHacks(EnabledHacks);
                        Plugin.CurrentHost.Plugins[i].Object.SetObjectParser(Plugin.CurrentOptions.CurrentXParser);
                        Plugin.CurrentHost.Plugins[i].Object.SetObjectParser(Plugin.CurrentOptions.CurrentObjParser);
                    }
                }
            }

            string section = string.Empty;
            bool sectionAlwaysPrefix = false;

            for (int i = 0; i < Expressions.Count; i++)
            {
                // Progress + cancel check
                Plugin.CurrentProgress = i * progressFactor;
                if ((i & 255) == 0)
                {
                    Thread.Yield();
                    if (Plugin.Cancel) { Plugin.IsLoading = false; return; }
                }

                if (Expressions[i].Skip) continue;

                string text = Expressions[i].Text;
                if (text.Length == 0) continue;

                // --- Section header: [Section] ---
                if (text[0] == '[' && text[text.Length - 1] == ']')
                {
                    section = text.Substring(1, text.Length - 2).Trim();
                    if (string.Compare(section, "object", StringComparison.OrdinalIgnoreCase) == 0) section = "Structure";
                    else if (string.Compare(section, "railway", StringComparison.OrdinalIgnoreCase) == 0) section = "Track";
                    sectionAlwaysPrefix = true;
                    continue;
                }

                // --- RW: convert to CSV format ---
                if (IsRW)
                {
                    ArgumentTokenizer.ConvertRwToCsv(text, section, sectionAlwaysPrefix, out string converted);
                    text = converted;
                }

                // --- Separate command + arguments ---
                ArgumentTokenizer.Separate(text, Culture, false, IsRW, section,
                    out string cmd, out string argSeq,
                    Expressions[i].File, Expressions[i].Line,
                    Plugin.CurrentOptions.EnableBveTsHacks, EnabledHacks.AggressiveRwBrackets);

                string[] args = SplitArguments(argSeq);

                // --- Track position (number line) ---
                bool numberCheck = !IsRW || string.Compare(section, "track", StringComparison.OrdinalIgnoreCase) == 0;
                if (numberCheck && NumberFormats.IsValidDouble(cmd, UnitOfLength))
                {
                    NumberFormats.TryParseDouble(cmd, UnitOfLength, out double pos);
                    if (Plugin.CurrentOptions.EnableBveTsHacks && IsRW && pos == 4535545100) pos = 45355;
                    if (pos < 0.0)
                    {
                        Plugin.CurrentHost.AddMessage(MessageType.Error, false,
                            "Negative track position encountered at line " + Expressions[i].Line.ToString(Culture) +
                            ", column " + Expressions[i].Column.ToString(Culture) + " in file " + Expressions[i].File);
                    }
                    else
                    {
                        Data.TrackPosition = pos;
                        blockIndex = (int)Math.Floor(pos / Data.BlockInterval + 0.001);
                        if (Data.FirstUsedBlock == -1) Data.FirstUsedBlock = blockIndex;
                        Data.CreateMissingBlocks(blockIndex, PreviewOnly);
                    }
                    continue;
                }

                // --- 'with' command: update section state ---
                if (string.Equals(cmd, "with", StringComparison.OrdinalIgnoreCase))
                {
                    sectionAlwaysPrefix = false;
                    section = args.Length >= 1 ? args[0] : string.Empty;
                    continue;
                }

                // ============================================================
                //  COMMAND NORMALIZATION — prefix, suffix strip, path fix
                // ============================================================

                // Prefix with current section (e.g. ".FormL" → "Structure.FormL")
                if (cmd.StartsWith("."))
                {
                    if (Plugin.CurrentOptions.EnableBveTsHacks &&
                        (cmd.StartsWith(".run", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith(".flange", StringComparison.OrdinalIgnoreCase)) &&
                        string.Compare(section, "train", StringComparison.OrdinalIgnoreCase) != 0)
                    {
                        cmd = "train" + cmd;
                    }
                    else
                    {
                        cmd = section + cmd;
                    }
                }
                else if (sectionAlwaysPrefix)
                {
                    cmd = section + "." + cmd;
                }

                // Strip .Void
                cmd = cmd.Replace(".Void", "");

                // Fix train subfolder paths
                FixTrainPaths(ref cmd, args);

                // Fix structure/texture paths
                FixStructurePaths(ref cmd, args);

                // --- Strip command suffixes (.load, .set, .params, etc.) ---
                StripSuffixes(ref cmd);

                // Rename timetable.day/night → timetableday/timetablenight
                cmd = cmd.Replace("timetable.day", "timetableday").Replace("timetable.night", "timetablenight");

                // --- Find and extract indices from command ---
                int[] indices = FindIndices(ref cmd, Expressions[i]);

                // --- Skip empty commands ---
                if (string.IsNullOrEmpty(cmd)) continue;

                // --- Resolve namespace + command enum ---
                int period = cmd.IndexOf('.');
                string nameSpace = string.Empty;
                if (period >= 0)
                {
                    nameSpace = cmd.Substring(0, period).ToLowerInvariant();
                    cmd = cmd.Substring(period + 1);
                }
                cmd = cmd.ToLowerInvariant();

                // texture.* → structure.*
                if (nameSpace.StartsWith("texture", StringComparison.OrdinalIgnoreCase))
                    nameSpace = "structure";
                // signal → empty (signal commands use string names, not enums)
                if (nameSpace.StartsWith("signal", StringComparison.OrdinalIgnoreCase))
                    nameSpace = string.Empty;

                SectionKind sk;
                switch (nameSpace)
                {
                    case "options": sk = SectionKind.Options; break;
                    case "route": sk = SectionKind.Route; break;
                    case "track": sk = SectionKind.Track; break;
                    case "structure": sk = SectionKind.Structure; break;
                    case "train": sk = SectionKind.Train; break;
                    case "cycle": sk = SectionKind.Cycle; break;
                    case "": sk = SectionKind.Signal; break;
                    default: sk = SectionKind.None; break;
                }

                int commandValue = -1;
                if (sk != SectionKind.None && sk != SectionKind.Signal)
                {
                    CommandTables.TryResolve(sk, cmd, out commandValue);
                }

                // Hmmsim dynamic rail commands
                if (commandValue == -1 && sk == SectionKind.Track && Data.IsHmmsim)
                {
                    if (!Data.RailKeys.ContainsKey(cmd))
                    {
                        Data.RailKeys.Add(cmd, Data.RailKeys.Count);
                    }
                    cmd = Data.RailKeys[cmd].ToString(Culture);
                    if (Enum.TryParse(cmd, true, out TrackCommand tc))
                    {
                        commandValue = (int)tc;
                    }
                }

                if (commandValue == -1 && sk != SectionKind.Signal)
                {
                    Plugin.CurrentHost.AddMessage(MessageType.Warning, false,
                        "Command " + cmd + " is not supported at line " + Expressions[i].Line.ToString(Culture) +
                        ", column " + Expressions[i].Column.ToString(Culture) + " in file " + Expressions[i].File);
                }

                // ============================================================
                //  DISPATCH — call the right handler
                // ============================================================
                switch (sk)
                {
                    case SectionKind.Options:
                        if (commandValue != -1)
                            ParseOptionCommand((OptionsCommand)commandValue, args, UnitOfLength, Expressions[i], ref Data, PreviewOnly);
                        break;
                    case SectionKind.Route:
                        if (commandValue != -1)
                            ParseRouteCommand((RouteCommand)commandValue, args, indices[0], FileName, UnitOfLength, Expressions[i], ref Data, PreviewOnly);
                        break;
                    case SectionKind.Structure:
                        if (commandValue != -1)
                            ParseStructureCommand((StructureCommand)commandValue, args, indices, FileName, Encoding, Expressions[i], ref Data, PreviewOnly);
                        break;
                    case SectionKind.Signal:
                        ParseSignalCommand(cmd, args, indices[0], Encoding, Expressions[i], ref Data, PreviewOnly);
                        break;
                    case SectionKind.Train:
                        if (commandValue != -1)
                            ParseTrainCommand((TrainCommand)commandValue, args, indices[0], Expressions[i], ref Data, PreviewOnly);
                        break;
                    case SectionKind.Cycle:
                        if (commandValue != -1)
                            ParseCycleCommand((CycleCommand)commandValue, args, indices[0], Expressions[i], ref Data, PreviewOnly);
                        break;
                    case SectionKind.Track:
                        if (commandValue != -1)
                            ParseTrackCommand((TrackCommand)commandValue, args, FileName, UnitOfLength, Expressions[i], ref Data, blockIndex, PreviewOnly, IsRW);
                        break;
                }

                if (Plugin.Cancel) { Plugin.IsLoading = false; return; }
            }
        }

        // =====================================================
        //  HELPER: fix train subfolder paths
        // =====================================================
        private static void FixTrainPaths(ref string cmd, string[] args)
        {
            if (args.Length < 2 || args[1].IndexOf('.') >= 0) return;

            string[] trainCmds = {
                "train.run", "train.flange", "train.turn", "train.pressure",
                "train.handle", "train.power", "train.brake", "train.move",
                "train.brakehandle", "train.holdbrake", "train.horns",
                "train.sounds", "train.motor", "train.trainlinenumbers",
                "train.signal", "train.extras"
            };
            foreach (string t in trainCmds)
            {
                if (string.Equals(cmd, t, StringComparison.OrdinalIgnoreCase))
                {
                    args[1] = "train\\" + args[1];
                    return;
                }
            }
            if (string.Equals(cmd, "train.plugins", StringComparison.OrdinalIgnoreCase))
            {
                if (args[1].IndexOf('.') == 0)
                    args[1] = "train\\" + args[1].Substring(1);
                else if (args[1].IndexOf('.') == -1)
                    args[1] = "train\\" + args[1];
            }
        }

        // =====================================================
        //  HELPER: fix structure/texture paths
        // =====================================================
        private static void FixStructurePaths(ref string cmd, string[] args)
        {
            if (args.Length < 2) return;

            if (string.Equals(cmd, "structure.rail", StringComparison.OrdinalIgnoreCase) &&
                args[1].IndexOf('.') >= 0)
            {
                args[1] = args[1].Substring(args[1].LastIndexOf('.') + 1);
            }
            else if (string.Equals(cmd, "structure.beam", StringComparison.OrdinalIgnoreCase) &&
                     args[1].IndexOf('.') >= 0)
            {
                args[1] = args[1].Substring(args[1].LastIndexOf('.'));
            }
            else if (string.Equals(cmd, "structure.decoration", StringComparison.OrdinalIgnoreCase) &&
                     args[1].IndexOf('.') == -1)
            {
                args[1] = "structure\\" + args[1];
            }
            else if (string.Equals(cmd, "structure.pole", StringComparison.OrdinalIgnoreCase) &&
                     args.Length >= 3 && args[2].IndexOf('.') == -1)
            {
                args[2] = "structure\\" + args[2];
            }
            else if ((string.Equals(cmd, "texture.background", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(cmd, "texture.gradient", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(cmd, "texture.land", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(cmd, "texture.fog", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(cmd, "texture.structure", StringComparison.OrdinalIgnoreCase)) &&
                     args[1].IndexOf('.') == -1)
            {
                args[1] = "texture\\" + args[1];
            }
            else if ((string.Equals(cmd, "route.signal", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(cmd, "route.safety", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(cmd, "route.dynamiclight", StringComparison.OrdinalIgnoreCase)) &&
                     args[1].IndexOf('.') == -1)
            {
                args[1] = "route\\" + args[1];
            }
        }

        // =====================================================
        //  HELPER: strip command suffixes (.load, .set, .params)
        //  Ported from legacy CsvRwRouteParser.cs:253-299
        // =====================================================
        private static void StripSuffixes(ref string cmd)
        {
            if (cmd.StartsWith("structure", StringComparison.OrdinalIgnoreCase) && cmd.EndsWith(".load", StringComparison.OrdinalIgnoreCase))
                cmd = cmd.Substring(0, cmd.Length - 5).TrimEnd();
            else if (cmd.StartsWith("texture.background", StringComparison.OrdinalIgnoreCase) && cmd.EndsWith(".load", StringComparison.OrdinalIgnoreCase))
                cmd = cmd.Substring(0, cmd.Length - 5).TrimEnd();
            else if (cmd.StartsWith("texture.background", StringComparison.OrdinalIgnoreCase) && cmd.EndsWith(".x", StringComparison.OrdinalIgnoreCase))
                cmd = "texture.backgroundx" + cmd.Substring(18, cmd.Length - 20).TrimEnd();
            else if (cmd.StartsWith("texture.background", StringComparison.OrdinalIgnoreCase) && cmd.EndsWith(".aspect", StringComparison.OrdinalIgnoreCase))
                cmd = "texture.backgroundaspect" + cmd.Substring(18, cmd.Length - 25).TrimEnd();
            else if (cmd.StartsWith("structure.back", StringComparison.OrdinalIgnoreCase) && cmd.EndsWith(".x", StringComparison.OrdinalIgnoreCase))
                cmd = "texture.backgroundx" + cmd.Substring(14, cmd.Length - 16).TrimEnd();
            else if (cmd.StartsWith("structure.back", StringComparison.OrdinalIgnoreCase) && cmd.EndsWith(".aspect", StringComparison.OrdinalIgnoreCase))
                cmd = "texture.backgroundaspect" + cmd.Substring(14, cmd.Length - 21).TrimEnd();
            else if (cmd.StartsWith("cycle", StringComparison.OrdinalIgnoreCase) && cmd.EndsWith(".params", StringComparison.OrdinalIgnoreCase))
                cmd = cmd.Substring(0, cmd.Length - 7).TrimEnd();
            else if (cmd.StartsWith("signal", StringComparison.OrdinalIgnoreCase) && cmd.EndsWith(".load", StringComparison.OrdinalIgnoreCase))
                cmd = cmd.Substring(0, cmd.Length - 5).TrimEnd();
            else if (cmd.StartsWith("train.run", StringComparison.OrdinalIgnoreCase) && cmd.EndsWith(".set", StringComparison.OrdinalIgnoreCase))
                cmd = cmd.Substring(0, cmd.Length - 4).TrimEnd();
            else if (cmd.StartsWith("train.flange", StringComparison.OrdinalIgnoreCase) && cmd.EndsWith(".set", StringComparison.OrdinalIgnoreCase))
                cmd = cmd.Substring(0, cmd.Length - 4).TrimEnd();
            else if (cmd.StartsWith("train.timetable", StringComparison.OrdinalIgnoreCase) && cmd.EndsWith(".day.load", StringComparison.OrdinalIgnoreCase))
                cmd = "train.timetable.day" + cmd.Substring(15, cmd.Length - 24).Trim();
            else if (cmd.StartsWith("train.timetable", StringComparison.OrdinalIgnoreCase) && cmd.EndsWith(".night.load", StringComparison.OrdinalIgnoreCase))
                cmd = "train.timetable.night" + cmd.Substring(15, cmd.Length - 26).Trim();
            else if (cmd.StartsWith("train.timetable", StringComparison.OrdinalIgnoreCase) && cmd.EndsWith(".day", StringComparison.OrdinalIgnoreCase))
                cmd = "train.timetable.day" + cmd.Substring(15, cmd.Length - 19).Trim();
            else if (cmd.StartsWith("train.timetable", StringComparison.OrdinalIgnoreCase) && cmd.EndsWith(".night", StringComparison.OrdinalIgnoreCase))
                cmd = "train.timetable.night" + cmd.Substring(15, cmd.Length - 21).Trim();
            else if (cmd.StartsWith("route.signal", StringComparison.OrdinalIgnoreCase) && cmd.EndsWith(".set", StringComparison.OrdinalIgnoreCase))
                cmd = cmd.Substring(0, cmd.Length - 4).TrimEnd();
            else if (cmd.StartsWith("route.runinterval", StringComparison.OrdinalIgnoreCase))
                cmd = "train.interval" + cmd.Substring(17, cmd.Length - 17);
            else if (cmd.StartsWith("train.gauge", StringComparison.OrdinalIgnoreCase))
                cmd = "route.gauge" + cmd.Substring(11, cmd.Length - 11);
            else if (cmd.StartsWith("texture.", StringComparison.OrdinalIgnoreCase))
                cmd = "structure." + cmd.Substring(8, cmd.Length - 8);
        }
    }
}
