using System;
using System.Collections.Generic;
using OpenBveApi.Objects;
using OpenBveApi.Runtime;
using OpenBveApi.Sounds;
using OpenBveApi.Trains;
using Raylib_cs;
using SoundManager;
using TrainManager.Car;

namespace OpenBve
{
	using OpenBveApi.Math;

	internal partial class Sounds : SoundsBase
	{
		protected override void UpdateInverseModel(double timeElapsed)
		{
			Vector3 listenerPosition = Program.Renderer.Camera.AbsolutePosition;
			
			// Atmosphere / Speed of Sound (Raylib doesn't use this directly, but we could use it for doppler if we implement it)
			
			for (int i = 0; i < SourceCount; i++)
			{
				SoundSource source = Sources[i];
				if (source == null) continue;

				if (source.State == SoundSourceState.Stopped)
				{
					Sources[i] = Sources[SourceCount - 1];
					SourceCount--;
					i--;
					continue;
				}

				if (GlobalMute)
				{
					source.Stop();
					if (!source.Looped)
					{
						source.State = SoundSourceState.Stopped;
						Sources[i] = Sources[SourceCount - 1];
						SourceCount--;
						i--;
					}
					continue;
				}

				// Calculate Position
				Vector3 position;
				switch (source.Type)
				{
					case SoundType.TrainCar:
						var car = (AbstractCar)source.Parent;
						car.CreateWorldCoordinates(source.Position, out position, out _);
						break;
					case SoundType.AnimatedObject:
						var worldSound = (WorldSound)source.Parent;
						position = worldSound.Follower.WorldPosition + worldSound.Position;
						break;
					default:
						position = source.Position;
						break;
				}

				Vector3 positionDifference = position - listenerPosition;
				double distance = positionDifference.Norm();
				double radius = source.Radius;

				if (Program.Renderer.Camera.CurrentMode == CameraViewMode.Interior | Program.Renderer.Camera.CurrentMode == CameraViewMode.InteriorLookAhead)
				{
					if (source.Parent != TrainManager.PlayerTrain.Cars[TrainManager.PlayerTrain.DriverCar])
					{
						radius *= 0.5;
					}
				}

				double gain;
				if (distance < 2.0 * radius)
				{
					gain = 1.0 - distance * distance * (4.0 * radius - distance) / (16.0 * radius * radius * radius);
				}
				else
				{
					gain = radius / distance;
				}
				gain *= source.Volume;

				if (gain <= 0.0)
				{
					if (source.State == SoundSourceState.Playing)
					{
						source.Stop();
						source.State = SoundSourceState.PlayPending;
					}
					if (!source.Looped)
					{
						source.State = SoundSourceState.Stopped;
						Sources[i] = Sources[SourceCount - 1];
						SourceCount--;
						i--;
					}
				}
				else
				{
					// Basic Panning
					float pan = 0.5f;
					if (distance > 0)
					{
						// Project positionDifference onto camera side vector
						Vector3 side = Program.Renderer.Camera.AbsoluteSide;
						double dot = Vector3.Dot(positionDifference, side);
						pan = (float)(0.5 + 0.5 * (dot / distance));
					}
					
					source.UpdateInstance((float)gain, pan);
				}
			}
		}
	}
}
