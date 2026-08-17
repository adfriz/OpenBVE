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
    using OpenBveApi;
    using OpenBveApi.Interface;
    using OpenBveApi.Math;

    internal partial class RouteParser
    {
        /// <summary>
        /// Lexical / preprocessing stage: converts the already-split <see cref="Expression"/> list
        /// into a flat list of <see cref="RouteToken"/>s. Each token carries its resolved section,
        /// the resolved command enum value, the post-formation arguments, parenthesis indices and
        /// (for numeric lines) the track position. This folds the per-line command scanning that
        /// the legacy dispatch repeated on every line into a single pass, so the dispatch stage can
        /// switch directly on a resolved enum.
        /// </summary>
        private List<RouteToken> PreprocessLex(IList<Expression> Expressions, double[] UnitOfLength, ref RouteData Data)
        {
            List<RouteToken> tokens = new List<RouteToken>(Expressions.Count);
            string Section = string.Empty;
            bool SectionAlwaysPrefix = false;

            for (int j = 0; j < Expressions.Count; j++)
            {
                if (Expressions[j].Skip)
                {
                    continue;
                }
                string newLine = Expressions[j].Text;
                if (newLine.Length == 0)
                {
                    continue;
                }
                // section header
                if (newLine[0] == '[' && newLine[newLine.Length - 1] == ']')
                {
                    Section = newLine.Substring(1, newLine.Length - 2).Trim();
                    if (string.Compare(Section, "object", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        Section = "Structure";
                    }
                    else if (string.Compare(Section, "railway", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        Section = "Track";
                    }
                    SectionAlwaysPrefix = true;
                    tokens.Add(new RouteToken(SectionKind.None, -1, 0.0, false, true, null, null, null, Expressions[j].File, Expressions[j].Line, Expressions[j].Column));
                    continue;
                }
                string command;
                string argumentSequence;
                if (IsRW)
                {
                    ArgumentTokenizer.ConvertRwToCsv(newLine, Section, SectionAlwaysPrefix, out string converted);
                    newLine = converted;
                }
                ArgumentTokenizer.Separate(newLine, Culture, false, IsRW, Section, out command, out argumentSequence, Expressions[j].File, Expressions[j].Line, Plugin.CurrentOptions.EnableBveTsHacks, EnabledHacks.AggressiveRwBrackets);
                string[] arguments = ArgumentTokenizer.SplitArguments(argumentSequence, IsRW);
                bool numberCheck = !IsRW || string.Compare(Section, "track", StringComparison.OrdinalIgnoreCase) == 0;
                if (numberCheck && NumberFormats.IsValidDouble(command, UnitOfLength))
                {
                    // track position
                    double currentTrackPosition;
                    NumberFormats.TryParseDouble(command, UnitOfLength, out currentTrackPosition);
                    if (Plugin.CurrentOptions.EnableBveTsHacks && IsRW && currentTrackPosition == 4535545100)
                    {
                        currentTrackPosition = 45355;
                    }
                    tokens.Add(new RouteToken(SectionKind.None, -1, currentTrackPosition, true, false, arguments, null, null, Expressions[j].File, Expressions[j].Line, Expressions[j].Column));
                    continue;
                }
                if (command.ToLowerInvariant() == "with")
                {
                    SectionAlwaysPrefix = false;
                    Section = arguments.Length >= 1 ? arguments[0] : string.Empty;
                    tokens.Add(new RouteToken(SectionKind.None, -1, 0.0, false, false, arguments, "with", null, Expressions[j].File, Expressions[j].Line, Expressions[j].Column));
                    continue;
                }
                // formation / normalisation (ported verbatim from the legacy per-line logic)
                if (command.StartsWith("."))
                {
                    if (Plugin.CurrentOptions.EnableBveTsHacks && (command.StartsWith(".run", StringComparison.OrdinalIgnoreCase) || command.StartsWith(".flange", StringComparison.OrdinalIgnoreCase)) && string.Compare(Section, "train", StringComparison.OrdinalIgnoreCase) != 0)
                    {
                        command = "train" + command;
                    }
                    else
                    {
                        command = Section + command;
                    }
                }
                else if (SectionAlwaysPrefix)
                {
                    command = Section + "." + command;
                }
                command = command.Replace(".Void", "");
                if (string.Compare(command, "train.run", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "train.flange", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "train.turn", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "train.pressure", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 4 && arguments[3].IndexOf('.') == -1)
                {
                    arguments[3] = "train\\" + arguments[3];
                }
                if (string.Compare(command, "train.handle", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "train.power", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "train.brake", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "train.move", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "train.brakehandle", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "train.holdbrake", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "train.horns", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "train.sounds", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "train.motor", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "train.trainlinenumbers", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "train.signal", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "train.extras", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "train.plugins", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == 0)
                {
                    arguments[1] = "train\\" + arguments[1].Substring(1);
                }
                if (string.Compare(command, "train.plugins", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "train\\" + arguments[1];
                }
                if (string.Compare(command, "structure.rail", StringComparison.OrdinalIgnoreCase) == 0 && string.Compare(Section, "track", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') >= 0)
                {
                    arguments[1] = arguments[1].Substring(arguments[1].LastIndexOf('.') + 1);
                }
                if (string.Compare(command, "structure.beam", StringComparison.OrdinalIgnoreCase) == 0 && string.Compare(Section, "track", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') >= 0)
                {
                    arguments[1] = arguments[1].Substring(arguments[1].LastIndexOf('.'));
                }
                if (string.Compare(command, "structure.decoration", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "structure\\" + arguments[1];
                }
                if (string.Compare(command, "structure.pole", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 3 && arguments[2].IndexOf('.') == -1)
                {
                    arguments[2] = "structure\\" + arguments[2];
                }
                if (string.Compare(command, "texture.background", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "texture\\" + arguments[1];
                }
                if (string.Compare(command, "texture.gradient", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "texture\\" + arguments[1];
                }
                if (string.Compare(command, "texture.land", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "texture\\" + arguments[1];
                }
                if (string.Compare(command, "texture.fog", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "texture\\" + arguments[1];
                }
                if (string.Compare(command, "texture.structure", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "texture\\" + arguments[1];
                }
                if (string.Compare(command, "route.signal", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "route\\" + arguments[1];
                }
                if (string.Compare(command, "route.safety", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "route\\" + arguments[1];
                }
                if (string.Compare(command, "route.dynamiclight", StringComparison.OrdinalIgnoreCase) == 0 && arguments.Length >= 2 && arguments[1].IndexOf('.') == -1)
                {
                    arguments[1] = "route\\" + arguments[1];
                }
                command = command.Replace("timetable.day", "timetableday").Replace("timetable.night", "timetablenight");

                int[] commandIndices = FindIndices(ref command, Expressions[j]);
                if (!string.IsNullOrEmpty(command))
                {
                    int period = command.IndexOf('.');
                    string nameSpace = string.Empty;
                    if (period >= 0)
                    {
                        nameSpace = command.Substring(0, period).ToLowerInvariant();
                        command = command.Substring(period + 1);
                    }
                    command = command.ToLowerInvariant();
                    if (nameSpace.StartsWith("texture", StringComparison.OrdinalIgnoreCase))
                    {
                        nameSpace = "structure";
                    }
                    if (nameSpace.StartsWith("signal", StringComparison.OrdinalIgnoreCase))
                    {
                        nameSpace = string.Empty;
                    }
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
                        CommandTables.TryResolve(sk, command, out commandValue);
                    }
                    if (commandValue == -1 && sk == SectionKind.Track && Data.IsHmmsim)
                    {
                        if (!Data.RailKeys.ContainsKey(command))
                        {
                            Data.RailKeys.Add(command, Data.RailKeys.Count);
                        }
                        command = Data.RailKeys[command].ToString(Culture);
                        if (Enum.TryParse(command, true, out TrackCommand tc))
                        {
                            commandValue = (int)tc;
                        }
                        else
                        {
                            commandValue = -1;
                        }
                    }
                    if (commandValue == -1 && sk != SectionKind.Signal)
                    {
                        Plugin.CurrentHost.AddMessage(MessageType.Warning, false, "Command " + command + " is not supported at line " + Expressions[j].Line.ToString(Culture) + ", column " + Expressions[j].Column.ToString(Culture) + " in file " + Expressions[j].File);
                    }
                    tokens.Add(new RouteToken(sk, commandValue, 0.0, false, false, arguments, command, commandIndices, Expressions[j].File, Expressions[j].Line, Expressions[j].Column));
                }
            }
            return tokens;
        }
    }
}
