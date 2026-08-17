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
    using System.Threading;
    using OpenBveApi;
    using OpenBveApi.Interface;
    using OpenBveApi.Math;
    using OpenBveApi.Routes;
    using RouteManager2.Stations;

    internal partial class RouteParser
    {
        // parse route for data using the tokenised preprocessing pipeline
        private void ParseRouteForDataNew(string FileName, System.Text.Encoding Encoding, IList<Expression> Expressions, double[] UnitOfLength, ref RouteData Data, bool PreviewOnly)
        {
            CurrentStation = -1;
            CurrentStop = -1;
            CurrentSection = 0;

            int BlockIndex = 0;
            CurrentRoute.Tracks[0].Direction = TrackDirection.Forwards;
            CurrentRoute.Stations = new RouteStation[] { };
            double progressFactor = Expressions.Count == 0 ? 0.3333 : 0.3333 / Expressions.Count;
            // process non-track namespaces
            //Check for any special-cased fixes we might need
            CheckForAvailablePatch(FileName, ref Data, ref Expressions, PreviewOnly);
            //Apply parameters to object loaders
            if (!PreviewOnly)
            {
                for (int i = 0; i < Plugin.CurrentHost.Plugins.Length; i++)
                {
                    if (Plugin.CurrentHost.Plugins[i].Object != null)
                    {
                        EnabledHacks.BveTsHacks = Plugin.CurrentOptions.EnableBveTsHacks;
                        EnabledHacks.BlackTransparency = true;
                        Plugin.CurrentHost.Plugins[i].Object.SetCompatibilityHacks(EnabledHacks);
                        //Remember that these will be ignored if not the correct plugin
                        Plugin.CurrentHost.Plugins[i].Object.SetObjectParser(Plugin.CurrentOptions.CurrentXParser);
                        Plugin.CurrentHost.Plugins[i].Object.SetObjectParser(Plugin.CurrentOptions.CurrentObjParser);
                    }
                }
            }

            List<RouteToken> tokens = PreprocessLex(Expressions, UnitOfLength, ref Data);

            // pass 1: non-track namespaces (Options, Route, Structure, Signal, Train, Cycle)
            for (int i = 0; i < tokens.Count; i++)
            {
                Plugin.CurrentProgress = i * progressFactor;
                if ((i & 255) == 0)
                {
                    Thread.Sleep(1);
                    if (Plugin.Cancel)
                    {
                        Plugin.IsLoading = false;
                        return;
                    }
                }
                RouteToken rc = tokens[i];
                if (rc.IsSectionHeader)
                {
                    continue;
                }
                if (rc.IsTrackPosition)
                {
                    if (rc.TrackPosition < 0.0)
                    {
                        Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Negative track position encountered at line " + rc.Line.ToString(Culture) + ", column " + rc.Column.ToString(Culture) + " in file " + rc.File);
                    }
                    continue;
                }
                if (rc.Section == SectionKind.Track)
                {
                    // deferred to pass 2
                    continue;
                }
                string[] args = rc.Arguments;
                switch (rc.Section)
                {
                    case SectionKind.Options:
                        if (rc.CommandValue != -1)
                        {
                            ParseOptionCommand((OptionsCommand)rc.CommandValue, args, UnitOfLength, Expressions[i], ref Data, PreviewOnly);
                        }
                        break;
                    case SectionKind.Route:
                        if (rc.CommandValue != -1)
                        {
                            ParseRouteCommand((RouteCommand)rc.CommandValue, args, rc.CommandIndices != null ? rc.CommandIndices[0] : 0, FileName, UnitOfLength, Expressions[i], ref Data, PreviewOnly);
                        }
                        break;
                    case SectionKind.Structure:
                        if (rc.CommandValue != -1)
                        {
                            ParseStructureCommand((StructureCommand)rc.CommandValue, args, rc.CommandIndices, FileName, Encoding, Expressions[i], ref Data, PreviewOnly);
                        }
                        break;
                    case SectionKind.Signal:
                        ParseSignalCommand(rc.CommandName, args, rc.CommandIndices != null ? rc.CommandIndices[0] : 0, Encoding, Expressions[i], ref Data, PreviewOnly);
                        break;
                    case SectionKind.Train:
                        if (rc.CommandValue != -1)
                        {
                            ParseTrainCommand((TrainCommand)rc.CommandValue, args, rc.CommandIndices != null ? rc.CommandIndices[0] : 0, Expressions[i], ref Data, PreviewOnly);
                        }
                        break;
                    case SectionKind.Cycle:
                        if (rc.CommandValue != -1)
                        {
                            ParseCycleCommand((CycleCommand)rc.CommandValue, args, rc.CommandIndices != null ? rc.CommandIndices[0] : 0, Expressions[i], ref Data, PreviewOnly);
                        }
                        break;
                }
                if (Plugin.Cancel)
                {
                    Plugin.IsLoading = false;
                    return;
                }
            }

            // pass 2: track namespace (track positions + track.* commands)
            for (int i = 0; i < tokens.Count; i++)
            {
                Plugin.CurrentProgress = 0.3333 + i * progressFactor;
                if ((i & 255) == 0)
                {
                    Thread.Sleep(1);
                    if (Plugin.Cancel)
                    {
                        Plugin.IsLoading = false;
                        return;
                    }
                }
                RouteToken rc = tokens[i];
                if (rc.IsSectionHeader)
                {
                    continue;
                }
                if (rc.IsTrackPosition)
                {
                    double currentTrackPosition = rc.TrackPosition;
                    if (rc.Arguments != null && rc.Arguments.Length != 0)
                    {
                        if (AllowTrackPositionArguments)
                        {
                            Data.TrackPosition = currentTrackPosition;
                            BlockIndex = (int)Math.Floor(currentTrackPosition / Data.BlockInterval + 0.001);
                            if (Data.FirstUsedBlock == -1)
                            {
                                Data.FirstUsedBlock = BlockIndex;
                            }
                            Data.CreateMissingBlocks(BlockIndex, PreviewOnly);
                        }
                        else
                        {
                            Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Invalid track position encountered at line " + rc.Line.ToString(Culture) + ", column " + rc.Column.ToString(Culture) + " in file " + rc.File);
                        }
                    }
                    else
                    {
                        if (currentTrackPosition < 0.0)
                        {
                            Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Negative track position encountered at line " + rc.Line.ToString(Culture) + ", column " + rc.Column.ToString(Culture) + " in file " + rc.File);
                        }
                        else
                        {
                            Data.TrackPosition = currentTrackPosition;
                            BlockIndex = (int)Math.Floor(currentTrackPosition / Data.BlockInterval + 0.001);
                            if (Data.FirstUsedBlock == -1)
                            {
                                Data.FirstUsedBlock = BlockIndex;
                            }
                            Data.CreateMissingBlocks(BlockIndex, PreviewOnly);
                        }
                    }
                    continue;
                }
                if (rc.Section != SectionKind.Track)
                {
                    continue;
                }
                if (rc.CommandValue != -1)
                {
                    ParseTrackCommand((TrackCommand)rc.CommandValue, rc.Arguments, FileName, UnitOfLength, Expressions[i], ref Data, BlockIndex, PreviewOnly, IsRW);
                }
                if (Plugin.Cancel)
                {
                    Plugin.IsLoading = false;
                    return;
                }
            }
        }
    }
}
