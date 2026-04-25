using OpenBveApi.FunctionScripting;
using OpenBveApi.Hosts;
using OpenBveApi.Sounds;
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

		public void Load()
		{
			if (Loaded == SoundBufferState.Loaded || Ignore) return;

			if (Origin.GetSound(out Sound sound))
			{
				unsafe {
					fixed (byte* ptr = sound.GetMonoMix()) {
						// Raylib Wave expects data pointer
						// We need to be careful about ownership here
						// For now, let's use Raylib.LoadWave to avoid complexity if possible
						// but since we have the raw bytes from OpenBveApi:
						
						RaylibWave = new Wave {
							FrameCount = (uint)(sound.Bytes.Length / (sound.BitsPerSample / 8) / (sound.IsStereo ? 2 : 1)),
							SampleRate = (int)sound.SampleRate,
							SampleSize = (int)sound.BitsPerSample,
							Channels = 1, // GetMonoMix returns mono
							Data = Raylib.MemAlloc((uint)sound.Bytes.Length)
						};
						System.Runtime.InteropServices.Marshal.Copy(sound.Bytes, 0, (IntPtr)RaylibWave.Data, sound.Bytes.Length);
					}
				}
				duration = sound.Duration;
				Loaded = SoundBufferState.Loaded;
				return;
			}
			Ignore = true;
		}

		public void Unload()
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
