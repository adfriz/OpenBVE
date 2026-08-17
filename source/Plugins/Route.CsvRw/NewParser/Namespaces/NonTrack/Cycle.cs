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
using System;
using OpenBveApi.Interface;
using OpenBveApi.Math;

namespace CsvRwRouteParser.New {
	internal partial class RouteParser
	{
		private void ParseCycleCommand(CycleCommand Command, string[] Arguments, int Index, Expression Expression, ref RouteData Data, bool previewOnly)
		{
			switch (Command)
			{
				case CycleCommand.Ground:
					if (!previewOnly)
					{
						if (Index >= Data.Structure.Cycles.Length)
						{
							Array.Resize(ref Data.Structure.Cycles, Index + 1);
						}

						Data.Structure.Cycles[Index] = new int[Arguments.Length];
						for (int k = 0; k < Arguments.Length; k++)
						{
							int ix = 0;
							if (Arguments[k].Length > 0 && !NumberFormats.TryParseIntVb6(Arguments[k], out ix))
							{
								Plugin.CurrentHost.AddMessage(MessageType.Error, false, "The index of " + (k + 1).ToString(Culture) + " is invalid in Cycle." + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
								ix = 0;
							}

							if (ix < 0 || !Data.Structure.Ground.ContainsKey(ix))
							{
								Plugin.CurrentHost.AddMessage(MessageType.Error, false, "GroundStructure with an index of " + ix + " is out of range in Cycle." + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
								ix = 0;
							}

							Data.Structure.Cycles[Index][k] = ix;
						}
					}

					break;
				// rail cycle
				case CycleCommand.Rail:
					if (!previewOnly)
					{
						if (Index >= Data.Structure.RailCycles.Length)
						{
							Array.Resize(ref Data.Structure.RailCycles, Index + 1);
						}

						Data.Structure.RailCycles[Index] = new int[Arguments.Length];
						for (int k = 0; k < Arguments.Length; k++)
						{
							int ix = 0;
							if (Arguments[k].Length > 0 && !NumberFormats.TryParseIntVb6(Arguments[k], out ix))
							{
								Plugin.CurrentHost.AddMessage(MessageType.Error, false, "The index of " + (k + 1).ToString(Culture) + " is invalid in Cycle." + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
								ix = 0;
							}

							if (ix < 0 || !Data.Structure.RailObjects.ContainsKey(ix))
							{
								Plugin.CurrentHost.AddMessage(MessageType.Error, false, "RailStructure with an index of " + ix + " is out of range in Cycle." + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
								ix = 0;
							}

							Data.Structure.RailCycles[Index][k] = ix;
						}
					}

					break;
			}
		}
	}
}
