using System;
using System.Collections.Generic;
using OpenBveApi.Math;
using OpenBveApi.Sounds;
using Raylib_cs;
using SoundManager;

namespace TrainEditor2.Audio
{
	internal partial class SoundApi : SoundsBase
	{
		protected override void UpdateInverseModel(double timeElapsed)
		{
			Vector3 listenerPosition = Vector3.Zero;
			
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

				Vector3 positionDifference = source.Position - listenerPosition;
				double distance = positionDifference.Norm();
				double radius = source.Radius;

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
					float pan = 0.5f; // TrainEditor is usually mono/center or simple
					source.UpdateInstance((float)gain, pan);
				}
			}
		}
		
		public SoundApi(OpenBveApi.Hosts.HostInterface currentHost) : base(currentHost)
		{
		}
	}
}
