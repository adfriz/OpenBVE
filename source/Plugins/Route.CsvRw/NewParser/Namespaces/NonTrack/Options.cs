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
using OpenBveApi.Objects;
using OpenBveApi.Routes;

namespace CsvRwRouteParser.New {
	internal partial class RouteParser
	{
		private void ParseOptionCommand(OptionsCommand Command, string[] Arguments, double[] UnitOfLength, Expression Expression, ref RouteData Data, bool PreviewOnly)
		{
			switch (Command)
			{
				case OptionsCommand.BlockLength:
				{
					double length = 25.0;
					if (Arguments.Length >= 1 && Arguments[0].Length > 0 && !NumberFormats.TryParseDoubleVb6(Arguments[0], UnitOfLength, out length))
					{
						Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Length is invalid in Options.BlockLength at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
						length = 25.0;
					}

					Data.BlockInterval = length;
				}
					break;
				case OptionsCommand.XParser:
					if (!PreviewOnly)
					{
						int parser = 0;
						if (Arguments.Length >= 1 && Arguments[0].Length > 0 && !NumberFormats.TryParseIntVb6(Arguments[0], out parser) | parser < 0 || parser > 3)
						{
							Plugin.CurrentHost.AddMessage(MessageType.Error, false, "XParser is invalid in Options.XParser at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
						}
						else
						{
							for (int i = 0; i < Plugin.CurrentHost.Plugins.Length; i++)
							{
								Plugin.CurrentHost.Plugins[i].Object?.SetObjectParser((XParsers) parser); //Remember that this will be ignored if not the X plugin!
							}

						}
					}

					break;
				case OptionsCommand.ObjParser:
					if (!PreviewOnly)
					{
						int parser = 0;
						if (Arguments.Length >= 1 && Arguments[0].Length > 0 && !NumberFormats.TryParseIntVb6(Arguments[0], out parser) | parser < 0 || parser > 2)
						{
							Plugin.CurrentHost.AddMessage(MessageType.Error, false, "ObjParser is invalid in Options.ObjParser at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
						}
						else
						{
							for (int i = 0; i < Plugin.CurrentHost.Plugins.Length; i++)
							{
								Plugin.CurrentHost.Plugins[i].Object?.SetObjectParser((ObjParsers) parser); //Remember that this will be ignored if not the Obj plugin!
							}
						}
					}
					break;
				case OptionsCommand.UnitOfLength:
				case OptionsCommand.UnitOfSpeed:
					break;
				case OptionsCommand.ObjectVisibility:
					if (Arguments.Length < 1)
					{
						Plugin.CurrentHost.AddMessage(MessageType.Error, false, Command + " is expected to have one argument at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
					}
					else
					{
						if (!NumberFormats.TryParseIntVb6(Arguments[0], out int a))
						{
							Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Mode is invalid in " + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
						}
						else if (a < 0 || a > 3)
						{
							Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Mode is expected to be between 0 and 3 in " + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
						}
						else
						{
							Plugin.CurrentOptions.ObjectDisposalMode = (ObjectDisposalMode)a;
							if (Plugin.CurrentOptions.ObjectDisposalMode == ObjectDisposalMode.QuadTree)
							{
								Plugin.CurrentOptions.QuadTreeLeafSize = Math.Max(50, (int)Math.Ceiling(Plugin.CurrentOptions.ViewingDistance / 10.0d) * 10);
							}
						}
						
					}
					break;
				case OptionsCommand.SectionBehavior:
					if (Arguments.Length < 1)
					{
						Plugin.CurrentHost.AddMessage(MessageType.Error, false, Command + " is expected to have one argument at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
					}
					else
					{
						if (!NumberFormats.TryParseIntVb6(Arguments[0], out int a))
						{
							Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Mode is invalid in " + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
						}
						else if (a != 0 && a != 1)
						{
							Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Mode is expected to be either 0 or 1 in " + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
						}
						else
						{
							Data.ValueBasedSections = a == 1;
						}
					}

					break;
				case OptionsCommand.CantBehavior:
					if (Arguments.Length < 1)
					{
						Plugin.CurrentHost.AddMessage(MessageType.Error, false, Command + " is expected to have one argument at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
					}
					else
					{
						if (!NumberFormats.TryParseIntVb6(Arguments[0], out int a))
						{
							Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Mode is invalid in " + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
						}
						else if (a != 0 && a != 1)
						{
							Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Mode is expected to be either 0 or 1 in " + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
						}
						else
						{
							Data.SignedCant = a == 1;
						}
					}

					break;
				case OptionsCommand.FogBehavior:
					if (Arguments.Length < 1)
					{
						Plugin.CurrentHost.AddMessage(MessageType.Error, false, Command + " is expected to have one argument at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
					}
					else
					{
						if (!NumberFormats.TryParseIntVb6(Arguments[0], out int a))
						{
							Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Mode is invalid in " + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
						}
						else if (a != 0 && a != 1)
						{
							Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Mode is expected to be either 0 or 1 in " + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
						}
						else
						{
							Data.FogTransitionMode = a == 1;
						}
					}
					break;
				case OptionsCommand.EnableBveTsHacks:
					if (Arguments.Length < 1)
					{
						Plugin.CurrentHost.AddMessage(MessageType.Error, false, Command + " is expected to have one argument at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
					}
					else
					{
						if (!NumberFormats.TryParseIntVb6(Arguments[0], out int a))
						{
							Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Mode is invalid in " + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
						}
						else if (a != 0 && a != 1)
						{
							Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Mode is expected to be either 0 or 1 in " + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
						}
						else
						{
							EnabledHacks.BveTsHacks = a == 1;
						}
					}
					break;
				case OptionsCommand.ReverseDirection:
					if (Arguments.Length < 1)
					{
						Plugin.CurrentHost.AddMessage(MessageType.Error, false, Command + " is expected to have one argument at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
					}
					else
					{
						if (!NumberFormats.TryParseIntVb6(Arguments[0], out int a))
						{
							Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Mode is invalid in " + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
						}
						else if (a != 0 && a != 1)
						{
							Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Mode is expected to be either 0 or 1 in " + Command + " at line " + Expression.Line.ToString(Culture) + ", column " + Expression.Column.ToString(Culture) + " in file " + Expression.File);
						}
						else
						{
							CurrentRoute.Tracks[0].Direction = a == 1 ? TrackDirection.Reverse : TrackDirection.Forwards;
						}
					}
					break;
			}
		}
	}
}
