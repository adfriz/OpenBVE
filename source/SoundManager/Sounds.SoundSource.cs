using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Sounds;
using OpenBveApi.Trains;
using Raylib_cs;

namespace SoundManager
{
	public class SoundSource
	{
		public readonly SoundBuffer Buffer;
		public double Radius;
		public double Pitch;
		public double Volume;
		public Vector3 Position;
		public object Parent;
		public bool Looped;
		public SoundSourceState State;
		public Raylib_cs.Sound RaylibSound;
		public readonly SoundType Type;

		internal SoundSource(SoundBuffer buffer, double radius, double pitch, double volume, Vector3 position, object parent, bool looped)
		{
			Buffer = buffer;
			Radius = radius;
			Pitch = pitch;
			Volume = volume;
			Position = position;
			Parent = parent;
			Looped = looped;
			State = SoundSourceState.PlayPending;
			
			if (parent is AbstractCar) Type = SoundType.TrainCar;
			else if (parent is WorldObject) Type = SoundType.AnimatedObject;
			else Type = SoundType.Undefined;
		}

		public void UpdateInstance(float effectiveVolume, float pan)
		{
			if (State == SoundSourceState.PlayPending)
			{
				Buffer.Load();
				if (Buffer.Loaded == SoundBufferState.Loaded)
				{
					RaylibSound = Raylib.LoadSoundFromWave(Buffer.RaylibWave);
					Raylib.SetSoundPitch(RaylibSound, (float)Pitch);
					Raylib.SetSoundVolume(RaylibSound, effectiveVolume);
					Raylib.SetSoundPan(RaylibSound, pan);
					Raylib.PlaySound(RaylibSound);
					State = SoundSourceState.Playing;
				}
			}
			
			if (State == SoundSourceState.Playing)
			{
				Raylib.SetSoundPitch(RaylibSound, (float)Pitch);
				Raylib.SetSoundVolume(RaylibSound, effectiveVolume);
				Raylib.SetSoundPan(RaylibSound, pan);
				
				if (!Raylib.IsSoundPlaying(RaylibSound))
				{
					if (Looped)
					{
						Raylib.PlaySound(RaylibSound);
					}
					else
					{
						State = SoundSourceState.Stopped;
						Raylib.UnloadSound(RaylibSound);
					}
				}
			}
		}

		public void Stop()
		{
			if (State == SoundSourceState.Playing)
			{
				Raylib.StopSound(RaylibSound);
				Raylib.UnloadSound(RaylibSound);
			}
			State = SoundSourceState.Stopped;
		}

		public bool IsPlaying() => State == SoundSourceState.Playing || State == SoundSourceState.PlayPending;
	}
}
