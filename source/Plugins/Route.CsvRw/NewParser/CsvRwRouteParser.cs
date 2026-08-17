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
using System.Collections.Generic;
using System.Linq;
using OpenBveApi;
using OpenBveApi.Colors;
using OpenBveApi.Math;
using OpenBveApi.Interface;
using OpenBveApi.Objects;
using OpenBveApi.Routes;
using RouteManager2;
using RouteManager2.Climate;
using RouteManager2.SignalManager;
using RouteManager2.Stations;

namespace CsvRwRouteParser.New {
	internal partial class RouteParser {
		internal string ObjectPath;
		internal string SoundPath;
		internal string TrainPath;
		internal string CompatibilityFolder;
		internal static CompatabilityHacks EnabledHacks;
		internal bool SplitLineHack = true;
		internal bool AllowTrackPositionArguments = false;
		internal readonly bool IsRW;
		internal readonly Plugin Plugin;

		internal RouteParser(Plugin plugin, bool isRW)
		{
			Plugin = plugin;
			IsRW = isRW;
		}

		private class RouteData
		{
			internal double TrackPosition;
			internal double BlockInterval;
			/// <summary>OpenBVE runs internally in meters per second
			/// This value is used to convert between the speed set by Options.UnitsOfSpeed and m/s
			/// </summary>
			internal double UnitOfSpeed;
			internal bool SignedCant;
			internal bool FogTransitionMode;
			internal readonly StructureData Structure;
			internal readonly SignalDictionary Signals;
			internal CompatibilitySignalObject[] CompatibilitySignals;
			internal OpenBveApi.Textures.Texture[] TimetableDaytime;
			internal OpenBveApi.Textures.Texture[] TimetableNighttime;
			internal BackgroundDictionary Backgrounds;
			internal double[] SignalSpeeds;
			internal readonly List<Block> Blocks;
			internal readonly List<Marker> Markers;
			internal readonly List<StopRequest> RequestStops;
			internal int FirstUsedBlock;
			internal bool IgnorePitchRoll;
			internal bool LineEndingFix;
			internal bool ValueBasedSections = false;
			internal bool TurnUsed = false;
			internal bool SwitchUsed = false;
			internal Vector2 StartingDirection = Vector2.Down;

			internal readonly Dictionary<int, NewPatternObj> PatternObjects;

			internal bool IsHmmsim = false;

			internal readonly List<string> ScriptedTrainFiles;

			internal void SetHmmsimProperties()
			{
				IsHmmsim = true;
				Plugin.CurrentOptions.ObjectDisposalMode = ObjectDisposalMode.Accurate;
				Plugin.CurrentOptions.ObjectOptimizationBasicThreshold = 2000;
				// from observation
				if (BlockInterval == 25)
				{
					// only set block interval if it's the default- I'm sure someone has probably
					// mixed the BlockInterval command and Hmmsim properties....
					// Note that Hmmsim doesn't really use the BlockInterval (only for railtypes)
					BlockInterval = 5;
				}
				
			}
			/*
			 * HMMSIM
			 */
			internal readonly Dictionary<string, int> RailKeys = new Dictionary<string, int>();

			internal RouteData(bool previewOnly)
			{
				BlockInterval = 25.0;
				FirstUsedBlock = -1;
				Blocks = new List<Block>();
				Markers = new List<Marker>();
				RequestStops = new List<StopRequest>();
				ScriptedTrainFiles = new List<string>();
				Signals = new SignalDictionary();
				Structure = new StructureData();
				IsHmmsim = false;
				Blocks.Add(new Block(previewOnly));
				Blocks[0].Rails.Add(0, new Rail(2.0, 1.0) { RailStarted = true });
				Blocks[0].Rails.Add(-1, new Rail(0, 0) { RailStarted = true });
				Blocks[0].RailType = new[] { 0 };
				Blocks[0].CurrentTrackState = new TrackElement(0.0);
				Blocks[0].RailCycles = new RailCycle[1];
				Blocks[0].RailCycles[0].RailCycleIndex = -1;
				PatternObjects = new Dictionary<int, NewPatternObj>();
			}

			/// <summary>Creates any missing blocks</summary>
			/// <param name="ToIndex">The block index to process until</param>
			/// <param name="PreviewOnly">Whether this is a preview only</param>
			internal void CreateMissingBlocks(int ToIndex, bool PreviewOnly)
			{
				if (ToIndex >= Blocks.Count)
				{
					for (int i = Blocks.Count; i <= ToIndex; i++)
					{
						Blocks.Add(new Block(PreviewOnly));
						if (!PreviewOnly)
						{
							Blocks[i].Background = -1;
							Blocks[i].Fog = Blocks[i - 1].Fog;
							Blocks[i].FogDefined = false;
							Blocks[i].Cycle = Blocks[i - 1].Cycle;
							Blocks[i].Height = double.NaN;
							Blocks[i].SnowIntensity = Blocks[i - 1].SnowIntensity;
							Blocks[i].RainIntensity = Blocks[i - 1].RainIntensity;
							Blocks[i].WeatherObject = Blocks[i - 1].WeatherObject;
							Blocks[i].LightDefinition = Blocks[i - 1].LightDefinition;
							Blocks[i].DynamicLightDefinition = Blocks[i -1].DynamicLightDefinition;
							Blocks[i].Switches = new Switch[] { };
						}
						Blocks[i].RailCycles = Blocks[i - 1].RailCycles;
						Blocks[i].RailType = new int[Blocks[i - 1].RailType.Length];
						if (!PreviewOnly)
						{
							for (int j = 0; j < Blocks[i].RailType.Length; j++)
							{
								int rc = -1;
								if (Blocks[i].RailCycles.Length > j)
								{
									rc = Blocks[i].RailCycles[j].RailCycleIndex;
								}
								if (rc != -1 && Structure.RailCycles.Length > rc && Structure.RailCycles[rc].Length > 1)
								{
									int cc = Blocks[i].RailCycles[j].CurrentCycle;
									if (cc == Structure.RailCycles[rc].Length - 1)
									{
										Blocks[i].RailType[j] = Structure.RailCycles[rc][0];
										Blocks[i].RailCycles[j].CurrentCycle = 0;
									}
									else
									{
										cc++;
										Blocks[i].RailType[j] = Structure.RailCycles[rc][cc];
										Blocks[i].RailCycles[j].CurrentCycle++;
									}
								}
								else
								{
									Blocks[i].RailType[j] = Blocks[i - 1].RailType[j];
								}
							}
						}
						
						for (int j = 0; j < Blocks[i - 1].Rails.Count; j++)
						{
							int key = Blocks[i - 1].Rails.ElementAt(j).Key;
							Rail rail = new Rail(Blocks[i - 1].Rails[key].Accuracy,Blocks[i - 1].Rails[key].AdhesionMultiplier)
							{
								RailStarted = Blocks[i -1].Rails[key].RailStarted,
								RailStart = new Vector2(Blocks[i -1].Rails[key].RailStart),
								RailStartRefreshed = false,
								RailEnded = false,
								RailEnd = new Vector2(Blocks[i - 1].Rails[key].RailStart),
								IsDriveable = Blocks[i - 1].Rails[key].IsDriveable,
								PowerSupplies = new Dictionary<PowerSupplyTypes, PowerSupply>(Blocks[i -1].Rails[key].PowerSupplies)
							};
							Blocks[i].Rails.Add(key, rail);
						}
						if (!PreviewOnly)
						{
							Blocks[i].RailWall = new Dictionary<int, WallDike>();
							for (int j = 0; j < Blocks[i - 1].RailWall.Count; j++)
							{
								int key = Blocks[i - 1].RailWall.ElementAt(j).Key;
								if (Blocks[i - 1].RailWall[key] == null || !Blocks[i - 1].RailWall[key].Exists)
								{
									continue;
								}
								Blocks[i].RailWall.Add(key, Blocks[i - 1].RailWall[key].Clone());
							}
							Blocks[i].RailDike = new Dictionary<int, WallDike>();
							for (int j = 0; j < Blocks[i - 1].RailDike.Count; j++)
							{
								int key = Blocks[i - 1].RailDike.ElementAt(j).Key;
								if (Blocks[i - 1].RailDike[key] == null || !Blocks[i - 1].RailDike[key].Exists)
								{
									continue;
								}
								Blocks[i].RailDike.Add(key, Blocks[i - 1].RailDike[key].Clone());
							}
							Blocks[i].RailPole = new Pole[Blocks[i - 1].RailPole.Length];
							for (int j = 0; j < Blocks[i].RailPole.Length; j++)
							{
								Blocks[i].RailPole[j] = Blocks[i - 1].RailPole[j];
							}
						}
						Blocks[i].Pitch = Blocks[i - 1].Pitch;
						Blocks[i].CurrentTrackState = Blocks[i - 1].CurrentTrackState;
						Blocks[i].Turn = 0.0;
					}
				}
			}

			/// <summary>Sets the brightness value for the specified track position</summary>
			/// <param name="trackPosition">The track position to get the brightness value for</param>
			/// <returns>The brightness value</returns>
			internal double GetBrightness(double trackPosition)
			{
				double tMin = double.PositiveInfinity;
				double tMax = double.NegativeInfinity;
				double bMin = 1.0, bMax = 1.0;
				for (int i = 0; i < Blocks.Count; i++)
				{
					for (int j = 0; j < Blocks[i].BrightnessChanges.Length; j++)
					{
						if (Blocks[i].BrightnessChanges[j].TrackPosition <= trackPosition)
						{
							tMin = Blocks[i].BrightnessChanges[j].TrackPosition;
							bMin = Blocks[i].BrightnessChanges[j].Value;
						}
					}
				}
				for (int i = Blocks.Count - 1; i >= 0; i--)
				{
					for (int j = Blocks[i].BrightnessChanges.Length - 1; j >= 0; j--)
					{
						if (Blocks[i].BrightnessChanges[j].TrackPosition >= trackPosition)
						{
							tMax = Blocks[i].BrightnessChanges[j].TrackPosition;
							bMax = Blocks[i].BrightnessChanges[j].Value;
						}
					}
				}
				if (tMin == double.PositiveInfinity && tMax == double.NegativeInfinity)
				{
					return 1.0;
				}

				if (tMin == double.PositiveInfinity)
				{
					return (bMax - 1.0) * trackPosition / tMax + 1.0;
				}

				if (tMax == double.NegativeInfinity)
				{
					return bMin;
				}

				if (tMin == tMax)
				{
					return 0.5 * (bMin + bMax);
				}

				double n = (trackPosition - tMin) / (tMax - tMin);
				return (1.0 - n) * bMin + n * bMax;
			}
		}

		

		internal Dictionary<string, RoutefilePatch> availableRoutefilePatches = new Dictionary<string, RoutefilePatch>();

		internal static CsvRwRouteParser.Direction FindDirection(string Direction, string Command, bool IsWallDike, int Line, string File)
		{
			Direction = Direction.Trim();
			switch (Direction.ToUpperInvariant())
			{
				case "-1":
				case "L":
				case "LEFT":
					return CsvRwRouteParser.Direction.Left;
				case "B":
				case "BOTH":
					return CsvRwRouteParser.Direction.Both;
				case "+1":
				case "1":
				case "R":
				case "RIGHT":
					return CsvRwRouteParser.Direction.Right;
				case "0":
					// BVE is inconsistent: Walls / Dikes use 0 for *both* sides, stations use 0 for none....
					return IsWallDike ? CsvRwRouteParser.Direction.Both : CsvRwRouteParser.Direction.None;
				case "N":
				case "NONE":
				case "NEITHER":
					return CsvRwRouteParser.Direction.None;
				default:
					Plugin.CurrentHost.AddMessage(MessageType.Error, false, "Direction is invalid in " + Command + " at line " + Line + " in file " + File);
					return CsvRwRouteParser.Direction.Invalid;

			}
		}

		private void CheckForAvailablePatch(string FileName, ref RouteData Data, ref IList<Expression> Expressions, bool PreviewOnly)
		{
			if (Plugin.CurrentOptions.EnableBveTsHacks == false)
			{
				return;
			}

			string fileHash = Path.GetChecksum(FileName);
			if (availableRoutefilePatches.TryGetValue(fileHash, out RoutefilePatch patch))
			{
				if (patch.Incompatible)
				{
					throw new Exception("This routefile is incompatible with OpenBVE: " + Environment.NewLine + Environment.NewLine + patch.LogMessage);
				}
				Data.LineEndingFix = patch.LineEndingFix;
				Data.IgnorePitchRoll = patch.IgnorePitchRoll;
				if (!string.IsNullOrEmpty(patch.LogMessage))
				{
					Plugin.CurrentHost.AddMessage(MessageType.Warning, false, patch.LogMessage);
				}

				EnabledHacks.CylinderHack = patch.CylinderHack;
				EnabledHacks.DisableSemiTransparentFaces = patch.DisableSemiTransparentFaces;
				EnabledHacks.InsufficientWallDikeArguments = patch.InsufficientWallDikeArguments;
				Plugin.CurrentOptions.ObjectDisposalMode = patch.AccurateObjectDisposal ? ObjectDisposalMode.Accurate : ObjectDisposalMode.Legacy;

				for (int i = 0; i < patch.ExpressionFixes.Count; i++)
				{
					Expressions[patch.ExpressionFixes.ElementAt(i).Key].Text = patch.ExpressionFixes.ElementAt(i).Value;
				}

				if (patch.XParser != null)
				{
					Plugin.CurrentOptions.CurrentXParser = (XParsers) patch.XParser;
				}

				Plugin.CurrentOptions.Derailments = patch.Derailments;
				Plugin.CurrentOptions.Toppling = patch.Toppling;
				Plugin.CurrentOptions.DelayedAnimatedUpdates = patch.DelayedAnimatedUpdates;
				Plugin.CurrentOptions.AdhesionHack = patch.AdhesionHack;
				SplitLineHack = patch.SplitLineHack;
				AllowTrackPositionArguments = patch.AllowTrackPositionArguments;
				foreach (int i in patch.DummyRailTypes)
				{
					if (Data.Structure.RailObjects == null)
					{
						Data.Structure.RailObjects = new ObjectDictionary();
					}
					Data.Structure.RailObjects.Add(i, new StaticObject(Plugin.CurrentHost));
				}
				foreach (int i in patch.DummyGroundTypes)
				{
					if (Data.Structure.Ground == null)
					{
						Data.Structure.Ground = new ObjectDictionary();
					}
					Data.Structure.Ground.Add(i, new StaticObject(Plugin.CurrentHost));
				}

				if (!string.IsNullOrEmpty(patch.CompatibilitySignalSet) && !PreviewOnly)
				{
					CompatibilitySignalObject.ReadCompatibilitySignalXML(Plugin.CurrentHost, patch.CompatibilitySignalSet, out Data.CompatibilitySignals, out CompatibilityObjects.SignalPost, out Data.SignalSpeeds);
				}

				if (patch.ReducedColorTransparency)
				{
					for (int i = 0; i < Plugin.CurrentHost.Plugins.Length; i++)
					{
						OpenBveApi.Textures.CompatabilityHacks hacks = new OpenBveApi.Textures.CompatabilityHacks { ReduceTransparencyColorDepth = true };
						Plugin.CurrentHost.Plugins[i].Texture?.SetCompatabilityHacks(hacks);
					}
				}

				if (patch.ViewingDistance != int.MaxValue)
				{
					Plugin.CurrentOptions.ViewingDistance = patch.ViewingDistance;
				}
				else if (patch.MaxViewingDistance != int.MaxValue && Plugin.CurrentOptions.ViewingDistance > patch.MaxViewingDistance)
				{
					Plugin.CurrentOptions.ViewingDistance = patch.MaxViewingDistance;
				}

				if (patch.ColonFix)
				{
					for (int i = 0; i < Expressions.Count; i++)
					{
						Expressions[i].Text = Expressions[i].Text.Replace(':', ';');
					}
				}

				EnabledHacks.AggressiveRwBrackets = patch.AggressiveRwBrackets;
			}
		}

		internal CurrentRoute CurrentRoute;
		// parse route
		internal void ParseRoute(string fileName, System.Text.Encoding Encoding, string trainPath, string objectPath, string soundPath, bool PreviewOnly)
		{
			CurrentRoute = Plugin.CurrentRoute;
			/*
			 * Store paths for later use
			 */
			ObjectPath = objectPath;
			SoundPath = soundPath;
			TrainPath = trainPath;
			if (!PreviewOnly)
			{
				for (int i = 0; i < Plugin.CurrentHost.Plugins.Length; i++)
				{
					Plugin.CurrentHost.Plugins[i].Object?.SetObjectParser(SoundPath); //HACK: Pass out the sound folder path to those plugins which consume it
				}
			}
			freeObjCount = 0;
			railtypeCount = 0;
			Plugin.CurrentOptions.UnitOfSpeed = "km/h";
			Plugin.CurrentOptions.SpeedConversionFactor = 0.0;
			CompatibilityFolder = Plugin.FileSystem.GetDataFolder("Compatibility");
			CompatibilityObjects.LoadCompatibilityObjects(Path.CombineFile(CompatibilityFolder, "CompatibilityObjects.xml"));

			RoutePatchDatabaseParser.LoadRoutePatchDatabase(ref availableRoutefilePatches);
			Plugin.CurrentOptions.ObjectDisposalMode = ObjectDisposalMode.Legacy;
			RouteData Data = new RouteData(PreviewOnly);
			
			if (!PreviewOnly)
			{
				Data.Blocks[0].Background = 0;
				Data.Blocks[0].Fog = new Fog(CurrentRoute.NoFogStart, CurrentRoute.NoFogEnd, Color24.Grey, 0);
				Data.Blocks[0].Cycle = new[] {-1};
				Data.Blocks[0].Height = IsRW ? 0.3 : 0.0;
				Data.Blocks[0].RailFreeObj = new Dictionary<int, List<FreeObj>>();
				Data.Blocks[0].GroundFreeObj = new List<FreeObj>();
				Data.Blocks[0].RailWall = new Dictionary<int, WallDike>();
				Data.Blocks[0].RailDike = new Dictionary<int, WallDike>();
				Data.Blocks[0].RailPole = new Pole[] {};
				string poleFolder = Path.CombineDirectory(CompatibilityFolder, "Poles");
				Data.Structure.Poles = new PoleDictionary
				{
					{0, new ObjectDictionary()}, 
					{1, new ObjectDictionary()},
					{2, new ObjectDictionary()}, 
					{3, new ObjectDictionary()}
				};
				Data.Structure.Poles[0].Add(0, LoadStaticObject(Path.CombineFile(poleFolder, "pole_1.csv"), System.Text.Encoding.UTF8, false));
				Data.Structure.Poles[1].Add(0, LoadStaticObject(Path.CombineFile(poleFolder, "pole_2.csv"), System.Text.Encoding.UTF8, false));
				Data.Structure.Poles[2].Add(0, LoadStaticObject(Path.CombineFile(poleFolder, "pole_3.csv"), System.Text.Encoding.UTF8, false));
				Data.Structure.Poles[3].Add(0, LoadStaticObject(Path.CombineFile(poleFolder, "pole_4.csv"), System.Text.Encoding.UTF8, false));
				
				Data.Structure.RailObjects = new ObjectDictionary();
				Data.Structure.RailObjects = new ObjectDictionary();
				Data.Structure.Ground = new ObjectDictionary();
				Data.Structure.WallL = new ObjectDictionary();
				Data.Structure.WallR = new ObjectDictionary();
				Data.Structure.DikeL = new ObjectDictionary();
				Data.Structure.DikeR = new ObjectDictionary();
				Data.Structure.FormL = new ObjectDictionary();
				Data.Structure.FormR = new ObjectDictionary();
				Data.Structure.FormCL = new ObjectDictionary();
				Data.Structure.FormCR = new ObjectDictionary();
				Data.Structure.RoofL = new ObjectDictionary();
				Data.Structure.RoofR = new ObjectDictionary();
				Data.Structure.RoofCL = new ObjectDictionary();
				Data.Structure.RoofCR = new ObjectDictionary();
				Data.Structure.CrackL = new ObjectDictionary();
				Data.Structure.CrackR = new ObjectDictionary();
				Data.Structure.FreeObjects = new ObjectDictionary();
				Data.Structure.Beacon = new ObjectDictionary();
				Data.Structure.Cycles = new int[][] {};
				Data.Structure.RailCycles = new int[][] { };
				Data.Structure.Run = new int[] {};
				Data.Structure.Flange = new int[] {};
				Data.Backgrounds = new BackgroundDictionary();
				Data.TimetableDaytime = new OpenBveApi.Textures.Texture[] {null, null, null, null};
				Data.TimetableNighttime = new OpenBveApi.Textures.Texture[] {null, null, null, null};
				Data.Structure.WeatherObjects = new ObjectDictionary();
				Data.Structure.LightDefinitions = new Dictionary<int, LightDefinition[]>();
				if (Plugin.CurrentOptions.CurrentCompatibilitySignalSet == null) //not selected via main form
				{
					Plugin.CurrentOptions.CurrentCompatibilitySignalSet = Path.CombineFile(Plugin.FileSystem.GetDataFolder("Compatibility"), "Signals\\Japanese.xml");
				}
				CompatibilitySignalObject.ReadCompatibilitySignalXML(Plugin.CurrentHost, Plugin.CurrentOptions.CurrentCompatibilitySignalSet, out Data.CompatibilitySignals, out CompatibilityObjects.SignalPost, out Data.SignalSpeeds);
				// game data
				CurrentRoute.Sections = new[]
				{
					new RouteManager2.SignalManager.Section(0, new[] { new SectionAspect(0, 0.0), new SectionAspect(4, double.PositiveInfinity) }, SectionType.IndexBased)
				};
				
				CurrentRoute.Sections[0].CurrentAspect = 0;
				CurrentRoute.Sections[0].StationIndex = -1;
			}
			ParseRouteForData(fileName, Encoding, ref Data, PreviewOnly);
			if (Plugin.Cancel)
			{
				Plugin.IsLoading = false;
				return;
			}
			ApplyRouteData(fileName, ref Data, PreviewOnly);
		}

		private void ParseRouteForData(string FileName, System.Text.Encoding Encoding, ref RouteData Data, bool PreviewOnly) {
			//Read the entire routefile into memory
			List<string> Lines = System.IO.File.ReadAllLines(FileName, Encoding).ToList();
			PreprocessSplitIntoExpressions(FileName, Lines, out IList<Expression> Expressions, true);
			PreprocessChrRndSub(FileName, Encoding, ref Expressions);
			double[] UnitOfLength = { 1.0 };
			//Set units of speed initially to km/h
			//This represents 1km/h in m/s
			Data.UnitOfSpeed = 0.277777777777778;
			PreprocessOptions(Expressions, ref Data, ref UnitOfLength, PreviewOnly);
			PreprocessSortByTrackPosition(UnitOfLength, ref Expressions);
			ParseRouteForDataNew(FileName, Encoding, Expressions, UnitOfLength, ref Data, PreviewOnly);
			CurrentRoute.UnitOfLength = UnitOfLength;
		}
		
		private int freeObjCount;
		private int missingObjectCount;
		private int railtypeCount;
		private readonly System.Globalization.CultureInfo Culture = System.Globalization.CultureInfo.InvariantCulture;

	}
}
