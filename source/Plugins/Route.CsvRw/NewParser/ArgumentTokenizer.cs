//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2026, Christopher Lees, The OpenBVE Project
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
    using System.Globalization;
    using System.Linq;
    using OpenBveApi.Interface;
    using OpenBveApi.Math;

    internal static class ArgumentTokenizer
    {
        /// <summary>Converts a RW formatted expression to CSV format</summary>
        /// <param name="text">The text to convert</param>
        /// <param name="section">The current section</param>
        /// <param name="sectionAlwaysPrefix">Whether the section prefix should always be applied</param>
        /// <param name="result">The converted text</param>
        internal static void ConvertRwToCsv(string text, string section, bool sectionAlwaysPrefix, out string result)
        {
            string Text = text;
            int Equals = Text.IndexOf('=');
            if (Equals >= 0 && sectionAlwaysPrefix)
            {
                // handle RW cycle syntax
                string t = Text.Substring(0, Equals);
                switch (section.ToLowerInvariant())
                {
                    case "cycle":
                        if (NumberFormats.TryParseDoubleVb6(t, out double g))
                        {
                            t = ".Ground(" + g + ")";
                        }
                        break;
                    case "signal":
                        if (NumberFormats.TryParseDoubleVb6(t, out double s))
                        {
                            t = ".Void(" + s + ")";
                        }
                        break;
                }

                // convert RW style into CSV style
                Text = t + " " + Text.Substring(Equals + 1);
            }

            result = Text;
        }

        /// <summary>Separates an expression into it's constituent command and arguments</summary>
        /// <param name="text">The text to separate</param>
        /// <param name="culture">The current culture</param>
        /// <param name="raiseErrors">Whether errors should be raised at this point</param>
        /// <param name="isRw">Whether this is a RW format file</param>
        /// <param name="currentSection">The current section being processed</param>
        /// <param name="command">The command</param>
        /// <param name="argumentSequence">The sequence of arguments contained within the expression</param>
        /// <param name="file">The file</param>
        /// <param name="line">The line number</param>
        /// <param name="enableBveTsHacks">Whether BVE-TS hacks are enabled</param>
        /// <param name="aggressiveRwBrackets">Whether aggressive RW bracket handling is enabled</param>
        internal static void Separate(string text, CultureInfo culture, bool raiseErrors, bool isRw, string currentSection, out string command, out string argumentSequence, string file, int line, bool enableBveTsHacks, bool aggressiveRwBrackets)
        {
            string Text = text;
            int Column = 0;
            int Line = line;
            bool openingError = false, closingError = false;
            int i, firstClosingBracket = 0;
            if (enableBveTsHacks)
            {
                if (Text.StartsWith("Train. ", StringComparison.InvariantCultureIgnoreCase))
                {
                    //HACK: Some Chinese routes seem to have used a space between Train. and the rest of the command
                    //e.g. Taipei Metro. BVE4/ 2 accept this......
                    Text = "Train." + Text.Substring(7, Text.Length - 7);
                }
                else if (Text.StartsWith("Texture. Background", StringComparison.InvariantCultureIgnoreCase))
                {
                    //Same hack as above, found in Minobu route for BVE2
                    Text = "Texture.Background" + Text.Substring(19, Text.Length - 19);
                }
                else if (Text.StartsWith("Structure. ", StringComparison.InvariantCultureIgnoreCase))
                {
                    //Another variant, this time from JR 内房Line
                    Text = "Structure." + Text.Substring(11, Text.Length - 11);
                }
                else if (Text.EndsWith(")height(0)", StringComparison.InvariantCultureIgnoreCase))
                {
                    //Heavy Coal original RW- Fix starting station
                    Text = Text.Substring(0, Text.Length - 9);
                }
                else if (Text.StartsWith(".Sta (貨)", StringComparison.OrdinalIgnoreCase))
                {
                    // 普通播州赤穂行 - brackets in station name
                    Text = ".Sta [貨]"+ Text.Substring(8);
                }
                else if (Text.StartsWith(".freeobj (9"))
                {
                    // East Linconshire Railway bridges
                    Text = ".freeobj(9" + Text.Substring(11);
                }
                else if (Text.StartsWith("Track.Sta (", StringComparison.InvariantCultureIgnoreCase))
                {
                    Text = "Track.Sta(" + Text.Substring(11);
                }

                if (isRw && currentSection.ToLowerInvariant() == "track")
                {
                    //Removes misplaced track position indices from the end of a command in the Track section
                    int idx = Text.LastIndexOf(')');
                    if (idx != -1 && idx != Text.Length)
                    {
                        string s = Text.Substring(idx + 1, Text.Length - idx - 1).Trim();
                        if (NumberFormats.TryParseDoubleVb6(s, out double _))
                        {
                            Text = Text.Substring(0, idx).Trim();
                        }
                    }
                }

                if (isRw && Text.EndsWith("))"))
                {
                    int openingBrackets = Text.Count(x => x == '(');
                    int closingBrackets = Text.Count(x => x == ')');
                    //Remove obviously wrong double-ending brackets
                    if (closingBrackets == openingBrackets + 1 && Text.EndsWith("))"))
                    {
                        Text = Text.Substring(0, Text.Length - 1);
                    }
                }

                if (Text.StartsWith("route.comment", StringComparison.InvariantCultureIgnoreCase) && Text.IndexOf("(C)", StringComparison.InvariantCultureIgnoreCase) != -1)
                {
                    //Some BVE4 routes use this instead of the copyright symbol
                    Text = Text.Replace("(C)", "©");
                    Text = Text.Replace("(c)", "©");
                }

                if(isRw && aggressiveRwBrackets)
                {
                    //Attempts to aggressively discard *anything* encountered after a closing bracket
                    int c = Text.IndexOf(')');
                    while (c > Text.Length)
                    {
                        if (Text[c] == '=')
                        {
                            break;
                        }

                        if (!char.IsWhiteSpace(Text[c]))
                        {
                            Text = Text.Substring(c);
                            break;
                        }
                        c++;
                    }
                    
                }
            }

            for (i = 0; i < Text.Length; i++)
            {
                if (Text[i] == '(')
                {
                    bool found = false;
                    int argumentIndex = 0;
                    i++;
                    while (i < Text.Length)
                    {
                        if (Text[i] == ',' || Text[i] == ';')
                        {
                            //Only check parenthesis in the station name field- The comma and semi-colon are the argument separators
                            argumentIndex++;
                        }

                        if (Text[i] == '(')
                        {
                            if (raiseErrors & !openingError)
                            {
                                switch (argumentIndex)
                                {
                                    case 0:
                                        if (Text.StartsWith("sta", StringComparison.InvariantCultureIgnoreCase))
                                        {
                                            Text = Text.Remove(i, 1).Insert(i, "[");
                                            break;
                                        }
                                        if (Text.StartsWith(".marker", StringComparison.InvariantCultureIgnoreCase) || Text.StartsWith(".announce", StringComparison.InvariantCultureIgnoreCase) ||
                                            Text.IndexOf(".Load", StringComparison.InvariantCultureIgnoreCase) != -1)
                                        {
                                            /*
                                             * HACK: In filenames, temp replace with an invalid but known character
                                             *
                                             * Opening parenthesis are fortunately simpler than closing, see notes below.
                                             */
                                            if (Text.Substring(i - 5, 5).ToLowerInvariant() != ".load")
                                            {
                                                Text = Text.Remove(i, 1).Insert(i, "<");
                                            }
                                            break;
                                        }
                                        Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Invalid opening parenthesis encountered at line " + Line.ToString(culture) + ", column " +
                                                                                                                                Column.ToString(culture) + " in file " + file);
                                        openingError = true;
                                        break;
                                    case 5: //arrival sound
                                    case 7: // HMMSIM arrival sound
                                    case 10: //departure sound
                                        //break;
                                        if (Text.StartsWith("sta", StringComparison.InvariantCultureIgnoreCase) || Text.StartsWith("Track.Sta", StringComparison.InvariantCultureIgnoreCase))
                                        {
                                            int j = i;
                                            while (j < Text.Length -1)
                                            {
                                                switch (Text[j])
                                                {
                                                    case ';':
                                                        // argument separator
                                                        break;
                                                    case '(':
                                                        Text = Text.Remove(j, 1).Insert(j, "<");
                                                        break;
                                                    case ')':
                                                        Text = Text.Remove(j, 1).Insert(j, ">");
                                                        break;
                                                }

                                                j++;
                                            }
                                            
                                            break;
                                        }
                                        Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Invalid opening parenthesis encountered at line " + Line.ToString(culture) + ", column " +
                                                                                                                                Column.ToString(culture) + " in file " + file);
                                        openingError = true;
                                        break;
                                    default:
                                        Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Invalid opening parenthesis encountered at line " + Line.ToString(culture) + ", column " +
                                                                                                                                Column.ToString(culture) + " in file " + file);
                                        openingError = true;
                                        break;
                                }
                            }
                        }
                        else if (Text[i] == ')')
                        {
                            if (i == Text.Length - 1)
                            {
                                found = true;
                                firstClosingBracket = i;
                                break;
                            }
                            switch (argumentIndex)
                            {
                                case 0:
                                    if (Text.StartsWith("sta", StringComparison.InvariantCultureIgnoreCase) && i != Text.Length)
                                    {
                                        Text = Text.Remove(i, 1).Insert(i, "]");
                                        continue;
                                    }
                                    if (Text.StartsWith(".timetable", StringComparison.InvariantCultureIgnoreCase) || Text.StartsWith(".marker", StringComparison.InvariantCultureIgnoreCase) || Text.StartsWith(".announce", StringComparison.InvariantCultureIgnoreCase) || Text.IndexOf(".Load", StringComparison.InvariantCultureIgnoreCase) != -1)
                                    {
                                        if (Text.Substring(i + 1, 5).ToLowerInvariant() == ".load" || Text.Substring(i + 1, 9).ToLowerInvariant() == ".day.load" || Text.Substring(i + 1, 11).ToLowerInvariant() == ".night.load")
                                        {
                                            found = true;
                                            firstClosingBracket = i;
                                            goto breakout;
                                        }

                                        if (Text.IndexOf('<') == -1)
                                        {
                                            i++;
                                            continue;
                                        }
                                        
                                        /*
                                         * HACK: In filenames, temp replace with an invalid but known character
                                         *
                                         * Note that this is a PITA in object folder names when the creator has used the alternate .Load() format as this contains far more brackets
                                         * e.g.
                                         * .Rail(0).Load(kcrmosr(2009)\rail\c0.csv)
                                         * We must keep the first and last closing parenthesis intact here
                                         */
                                        Text = Text.Remove(i, 1).Insert(i, ">");
                                        continue;
                                    }
                                    break;
                                case 5: //arrival sound
                                case 10: //departure sound
                                    if (Text.StartsWith("sta", StringComparison.InvariantCultureIgnoreCase) && i != Text.Length)
                                    {
                                        Text = Text.Remove(i, 1).Insert(i, ">");
                                        continue;
                                    }
                                    break;
                            }
                            found = true;
                            firstClosingBracket = i;
                            break;
                        }
                        i++;
                    }

                    breakout:

                    if (!found)
                    {
                        if (raiseErrors & !closingError)
                        {
                            Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Missing closing parenthesis encountered at line " + Line.ToString(culture) + ", column " + Column.ToString(culture) + " in file " + file);
                            closingError = true;
                        }

                        Text += ")";
                    }
                }
                else if (Text[i] == ')')
                {
                    if (raiseErrors & !closingError)
                    {
                        Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Invalid closing parenthesis encountered at line " + Line.ToString(culture) + ", column " + Column.ToString(culture) + " in file " + file);
                        closingError = true;
                    }
                }
                else if (char.IsWhiteSpace(Text[i]))
                {
                    if (i >= Text.Length - 1 || !char.IsWhiteSpace(Text[i + 1]))
                    {
                        break;
                    }
                }

            }

            if (firstClosingBracket != 0 && firstClosingBracket < Text.Length - 1)
            {
                if (!char.IsWhiteSpace(Text[firstClosingBracket + 1]) && Text[firstClosingBracket + 1] != '.' && Text[firstClosingBracket + 1] != ';')
                {
                    Text = Text.Insert(firstClosingBracket + 1, " ");
                    i = firstClosingBracket;
                }
            }

            if (i < Text.Length)
            {
                // white space was found outside of parentheses
                string a = Text.Substring(0, i);
                if (a.IndexOf('(') >= 0 & a.IndexOf(')') >= 0)
                {
                    // indices found not separated from the command by spaces
                    command = Text.Substring(0, i).TrimEnd();
                    argumentSequence = Text.Substring(i + 1).TrimStart();
                    if (argumentSequence.StartsWith("(") & argumentSequence.EndsWith(")"))
                    {
                        // arguments are enclosed by parentheses
                        argumentSequence = argumentSequence.Substring(1, argumentSequence.Length - 2).Trim();
                    }
                    else if (argumentSequence.StartsWith("("))
                    {
                        // only opening parenthesis found
                        if (raiseErrors & !closingError)
                        {
                            Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Missing closing parenthesis encountered at line " + Line.ToString(culture) + ", column " + Column.ToString(culture) + " in file " + file);
                        }

                        argumentSequence = argumentSequence.Substring(1).TrimStart();
                    }
                }
                else
                {
                    // no indices found before the space
                    if (i < Text.Length - 1 && Text[i + 1] == '(')
                    {
                        // opening parenthesis follows the space
                        int j = Text.IndexOf(')', i + 1);
                        if (j > i + 1)
                        {
                            // closing parenthesis found
                            if (j == Text.Length - 1)
                            {
                                // only closing parenthesis found at the end of the expression
                                command = Text.Substring(0, i).TrimEnd();
                                argumentSequence = Text.Substring(i + 2, j - i - 2).Trim();
                            }
                            else
                            {
                                // detect border between indices and arguments
                                bool found = false;
                                command = null;
                                argumentSequence = null;
                                for (int k = j + 1; k < Text.Length; k++)
                                {
                                    if (char.IsWhiteSpace(Text[k]))
                                    {
                                        command = Text.Substring(0, k).TrimEnd();
                                        argumentSequence = Text.Substring(k + 1).TrimStart();
                                        found = true;
                                        break;
                                    }

                                    if (Text[k] == '(')
                                    {
                                        command = Text.Substring(0, k).TrimEnd();
                                        argumentSequence = Text.Substring(k).TrimStart();
                                        found = true;
                                        break;
                                    }
                                }

                                if (!found)
                                {
                                    if (raiseErrors & !openingError & !closingError)
                                    {
                                        Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Invalid syntax encountered at line " + Line.ToString(culture) + ", column " + Column.ToString(culture) + " in file " + file);
                                        closingError = true;
                                    }

                                    command = Text;
                                    argumentSequence = string.Empty;
                                }

                                if (argumentSequence.StartsWith("(") & argumentSequence.EndsWith(")"))
                                {
                                    // arguments are enclosed by parentheses
                                    argumentSequence = argumentSequence.Substring(1, argumentSequence.Length - 2).Trim();
                                }
                                else if (argumentSequence.StartsWith("("))
                                {
                                    // only opening parenthesis found
                                    if (raiseErrors & !closingError)
                                    {
                                        Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Missing closing parenthesis encountered at line " + Line.ToString(culture) + ", column " + Column.ToString(culture) + " in file " + file);
                                    }

                                    argumentSequence = argumentSequence.Substring(1).TrimStart();
                                }
                            }
                        }
                        else
                        {
                            // no closing parenthesis found
                            if (raiseErrors & !closingError)
                            {
                                Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Missing closing parenthesis encountered at line " + Line.ToString(culture) + ", column " + Column.ToString(culture) + " in file " + file);
                            }

                            command = Text.Substring(0, i).TrimEnd();
                            argumentSequence = Text.Substring(i + 2).TrimStart();
                        }
                    }
                    else
                    {
                        // no index possible
                        command = Text.Substring(0, i).TrimEnd();
                        argumentSequence = Text.Substring(i + 1).TrimStart();
                        if (argumentSequence.StartsWith("(") & argumentSequence.EndsWith(")"))
                        {
                            // arguments are enclosed by parentheses
                            argumentSequence = argumentSequence.Substring(1, argumentSequence.Length - 2).Trim();
                        }
                        else if (argumentSequence.StartsWith("("))
                        {
                            // only opening parenthesis found
                            if (raiseErrors & !closingError)
                            {
                                Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Missing closing parenthesis encountered at line " + Line.ToString(culture) + ", column " + Column.ToString(culture) + " in file " + file);
                            }

                            argumentSequence = argumentSequence.Substring(1).TrimStart();
                        }
                    }
                }
            }
            else
            {
                // no single space found
                if (Text.EndsWith(")"))
                {
                    i = Text.LastIndexOf('(');
                    if (i >= 0)
                    {
                        command = Text.Substring(0, i).TrimEnd();
                        argumentSequence = Text.Substring(i + 1, Text.Length - i - 2).Trim();
                        if (Text.StartsWith("sta", StringComparison.InvariantCultureIgnoreCase) || Text.StartsWith("Track.Sta", StringComparison.InvariantCultureIgnoreCase)|| Text.StartsWith(".marker", StringComparison.InvariantCultureIgnoreCase) || Text.StartsWith(".announce", StringComparison.InvariantCultureIgnoreCase) || Text.IndexOf(".Load", StringComparison.InvariantCultureIgnoreCase) != -1)
                        {
                            // put back any temp removed brackets
                            argumentSequence = argumentSequence.Replace('<', '(');
                            argumentSequence = argumentSequence.Replace('>', ')');
                            if (argumentSequence.EndsWith(")"))
                            {
                                argumentSequence = argumentSequence.TrimEnd(')');
                            }
                        }
                    }
                    else
                    {
                        command = Text;
                        argumentSequence = string.Empty;
                    }
                }
                else
                {
                    i = Text.IndexOf('(');
                    if (i >= 0)
                    {
                        if (raiseErrors & !closingError)
                        {
                            Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Missing closing parenthesis encountered at line " + Line.ToString(culture) + ", column " + Column.ToString(culture) + " in file " + file);
                        }

                        command = Text.Substring(0, i).TrimEnd();
                        argumentSequence = Text.Substring(i + 1).TrimStart();
                    }
                    else
                    {
                        if (raiseErrors)
                        {
                            i = Text.IndexOf(')');
                            if (i >= 0 & !closingError)
                            {
                                Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Invalid closing parenthesis encountered at line " + Line.ToString(culture) + ", column " + Column.ToString(culture) + " in file " + file);
                            }
                        }

                        command = Text;
                        argumentSequence = string.Empty;
                    }
                }
            }

            // invalid trailing characters
            if (command.EndsWith(";"))
            {
                if (raiseErrors)
                {
                    Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Invalid trailing semicolon encountered in " + command + " at line " + Line.ToString(culture) + ", column " + Column.ToString(culture) + " in file " + file);
                }
                command = command.TrimEnd(';');
            }
        }

        /// <summary>Splits an argument sequence on commas (RW) and semicolons</summary>
        /// <param name="argumentSequence">The argument sequence</param>
        /// <param name="isRw">Whether this is a RW format file</param>
        internal static string[] SplitArguments(string argumentSequence, bool isRw)
        {
            string[] Arguments;
            {
                int n = 0;
                for (int k = 0; k < argumentSequence.Length; k++) {
                    if ((isRw && argumentSequence[k] == ',') || argumentSequence[k] == ';') 
                    {
                        n++;
                    }
                }
                Arguments = new string[n + 1];
                int a = 0, h = 0;
                for (int k = 0; k < argumentSequence.Length; k++) {
                    if ((isRw && argumentSequence[k] == ',') || argumentSequence[k] == ';') 
                    {
                        Arguments[h] = argumentSequence.Substring(a, k - a).Trim();
                        a = k + 1; h++;
                    }
                }
                if (argumentSequence.Length - a > 0) {
                    Arguments[h] = argumentSequence.Substring(a).Trim();
                    h++;
                }
                Array.Resize(ref Arguments, h);
            }
            return Arguments;
        }
    }
}
