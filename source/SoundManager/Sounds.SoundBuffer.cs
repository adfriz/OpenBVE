using OpenBveApi.FunctionScripting;
using OpenBveApi.Hosts;
using OpenBveApi.Sounds;
using Sound = OpenBveApi.Sounds.Sound;
using Raylib_cs;

namespace SoundManager
{
	public class SoundBuffer : SoundHandle
	{
		internal readonly SoundOrigin Origin;
		internal double Radius;
		public SoundBufferState Loaded;
		public Wave RaylibWave;
		private double duration;
		public double Duration
		{
			get
			{
				Load();
				return duration;
			}
		}
		internal bool Ignore;
		internal FunctionScript PitchFunction;
		internal FunctionScript VolumeFunction;
		internal double InternalVolumeFactor;

		internal SoundBuffer(HostInterface host, string path, double radius)
		{
			Origin = new PathOrigin(path, host);
			Radius = radius;
			Loaded = SoundBufferState.NotLoaded;
			duration = 0.0;
			InternalVolumeFactor = 0.5;
			Ignore = false;
		}

		internal SoundBuffer(Sound sound, double radius)
		{
			Origin = new RawOrigin(sound);
			Radius = radius;
			Loaded = SoundBufferState.NotLoaded;
			duration = 0.0;
			InternalVolumeFactor = 0.5;
			Ignore = false;
		}

		internal SoundBuffer(SoundOrigin origin)
		{
			Origin = origin;
			Radius = 0.0;
			Loaded = SoundBufferState.NotLoaded;
			duration = 0.0;
			InternalVolumeFactor = 0.5;
			Ignore = false;
		}

		private readonly object loadLock = new object();

		public void Load()
		{
			lock (loadLock)
			{
				if (Loaded == SoundBufferState.Loaded || Ignore) return;

				if (Origin.GetSound(out Sound sound))
				{
					byte[] monoData = sound.GetMonoMix();
					unsafe
					{
						RaylibWave = new Wave
						{
							frameCount = (uint)(monoData.Length / (sound.BitsPerSample / 8)),
							sampleRate = (uint)sound.SampleRate,
							sampleSize = (uint)sound.BitsPerSample,
							channels = 1, // GetMonoMix returns mono
							data = Raylib.MemAlloc(monoData.Length)
						};
						System.Runtime.InteropServices.Marshal.Copy(monoData, 0, (IntPtr)RaylibWave.data, monoData.Length);
					}
					duration = sound.Duration;
					Loaded = SoundBufferState.Loaded;
					return;
				}
				Ignore = true;
			}
		}

		public void Unload()
		{
			lock (loadLock)
			{
				if (Loaded == SoundBufferState.Loaded)
				{
					Raylib.UnloadWave(RaylibWave);
					Loaded = SoundBufferState.NotLoaded;
					Ignore = false;
				}
			}
		}
	}
}
