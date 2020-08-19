﻿using System;
using System.Linq;
using OpenBveApi.Math;
using OpenBveApi.Textures;
using RouteManager2.SignalManager;

namespace CsvRwRouteParser
{
	internal partial class Parser
	{
		private class RouteData
		{
			internal double TrackPosition;
			internal double BlockInterval;
			/// <summary>OpenBVE runs internally in meters per second
			/// This value is used to convert between the speed set by Options.UnitsOfSpeed and m/s
			/// </summary>
			internal double UnitOfSpeed;
			
			internal bool AccurateObjectDisposal;
			internal bool SignedCant;
			internal bool FogTransitionMode;
			internal StructureData Structure;
			internal SignalDictionary Signals;
			internal CompatibilitySignalObject[] CompatibilitySignals;
			internal Texture[] TimetableDaytime;
			internal Texture[] TimetableNighttime;
			internal BackgroundDictionary Backgrounds;
			internal double[] SignalSpeeds;
			internal Block[] Blocks;
			internal Marker[] Markers;
			internal StopRequest[] RequestStops;
			internal int FirstUsedBlock;
			internal bool IgnorePitchRoll;
			internal bool LineEndingFix;
			internal bool ValueBasedSections = false;
			/// <summary>Creates any missing blocks</summary>
			/// <param name="BlocksUsed">The total number of blocks currently used (Will be updated via ref)</param>
			/// <param name="ToIndex">The block index to process until</param>
			/// <param name="PreviewOnly">Whether this is a preview only</param>
			internal void CreateMissingBlocks(ref int BlocksUsed, int ToIndex, bool PreviewOnly)
			{
				if (ToIndex >= BlocksUsed)
				{
					while (Blocks.Length <= ToIndex)
					{
						Array.Resize(ref Blocks, Blocks.Length << 1);
					}
					for (int i = BlocksUsed; i <= ToIndex; i++)
					{
						Blocks[i] = new Block(PreviewOnly);
						if (!PreviewOnly)
						{
							Blocks[i].Background = -1;
							Blocks[i].Fog = Blocks[i - 1].Fog;
							Blocks[i].FogDefined = false;
							Blocks[i].Cycle = Blocks[i - 1].Cycle;
							Blocks[i].RailCycles = Blocks[i - 1].RailCycles;
							Blocks[i].Height = double.NaN;
						}
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
							Rail rail = new Rail
							{
								RailStarted = Blocks[i -1].Rails[key].RailStarted,
								RailStart = new Vector2(Blocks[i -1].Rails[key].RailStart),
								RailStartRefreshed = false,
								RailEnded = false,
								RailEnd = new Vector2(Blocks[i - 1].Rails[key].RailStart)
							};
							Blocks[i].Rails.Add(key, rail);
						}
						if (!PreviewOnly)
						{
							Blocks[i].RailWall = new WallDike[Blocks[i - 1].RailWall.Length];
							for (int j = 0; j < Blocks[i].RailWall.Length; j++)
							{
								if (Blocks[i - 1].RailWall[j] == null || !Blocks[i - 1].RailWall[j].Exists)
								{
									continue;
								}
								Blocks[i].RailWall[j] = Blocks[i - 1].RailWall[j].Clone();
							}
							Blocks[i].RailDike = new WallDike[Blocks[i - 1].RailDike.Length];
							for (int j = 0; j < Blocks[i].RailDike.Length; j++)
							{
								if (Blocks[i - 1].RailDike[j] == null || !Blocks[i - 1].RailDike[j].Exists)
								{
									continue;
								}
								Blocks[i].RailDike[j] = Blocks[i - 1].RailDike[j].Clone();
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
						Blocks[i].Accuracy = Blocks[i - 1].Accuracy;
						Blocks[i].AdhesionMultiplier = Blocks[i - 1].AdhesionMultiplier;
					}
					BlocksUsed = ToIndex + 1;
				}
			}
		}
		
	}
}