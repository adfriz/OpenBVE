using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using OpenBveApi.Hosts;
using OpenBveApi.Interface;
using OpenBveApi.Sounds;
using Sound = OpenBveApi.Sounds.Sound;
using Raylib_cs;

namespace SoundManager
{
	public abstract partial class SoundsBase
	{
		private List<SoundBuffer> Buffers = new List<SoundBuffer>();
		protected internal static SoundSource[] Sources = new SoundSource[16];
		protected internal static int SourceCount = 0;
		protected const double GainThreshold = 0.0001;
		public bool GlobalMute = false;
		private readonly HostInterface CurrentHost;
		public static bool SuppressSoundEvents = false;
		protected internal int SystemMaxSounds = int.MaxValue;
		public double OuterRadiusFactor;
		protected double OuterRadiusFactorSpeed;
		protected double OuterRadiusFactorMaximumSpeed;
		protected double OuterRadiusFactorMinimum;
		protected double OuterRadiusFactorMaximum;

		internal readonly Thread SoundLoaderThread;
		private bool soundThread = true;
		private readonly ConcurrentQueue<ThreadStart> SoundLoaderQueue = new ConcurrentQueue<ThreadStart>();

		protected SoundsBase(HostInterface currentHost)
		{
			CurrentHost = currentHost;
			SoundLoaderThread = new Thread(SoundThread);
		}

		public void Initialize(SoundRange range)
		{
			DeInitialize();

			switch (range)
			{
				case SoundRange.Low:
					OuterRadiusFactorMinimum = 2.0;
					OuterRadiusFactorMaximum = 8.0;
					OuterRadiusFactorMaximumSpeed = 1.0;
					break;
				case SoundRange.Medium:
					OuterRadiusFactorMinimum = 4.0;
					OuterRadiusFactorMaximum = 16.0;
					OuterRadiusFactorMaximumSpeed = 2.0;
					break;
				case SoundRange.High:
					OuterRadiusFactorMinimum = 6.0;
					OuterRadiusFactorMaximum = 24.0;
					OuterRadiusFactorMaximumSpeed = 3.0;
					break;
			}
			OuterRadiusFactor = Math.Sqrt(OuterRadiusFactorMinimum * OuterRadiusFactorMaximum);
			OuterRadiusFactorSpeed = 0.0;

			Raylib.InitAudioDevice();
			
			if (Raylib.IsAudioDeviceReady())
			{
				SoundLoaderThread.Start();
				return;
			}
			
			CurrentHost.AddMessage(MessageType.Error, false, "Failed to initialize Raylib Audio Device.");
		}

		public void DeInitialize()
		{
			soundThread = false;
			StopAllSounds();
			UnloadAllBuffers();
			
			if (Raylib.IsAudioDeviceReady())
			{
				Raylib.CloseAudioDevice();
			}
		}

		private void SoundThread()
		{
			soundThread = true;
			while (soundThread)
			{
				if (SoundLoaderQueue.TryDequeue(out ThreadStart result))
				{
					result.Invoke();
				}
				else
				{
					Thread.Sleep(100);	
				}
			}
		}

		private readonly object buffersLock = new object();

		public SoundBuffer RegisterBuffer(string path, double radius)
		{
			if (!File.Exists(path)) return null;
			lock (buffersLock)
			{
				foreach (var buffer in Buffers)
				{
					if (buffer.Origin is PathOrigin po && po.Path == path) return buffer;
				}

				try
				{
					SoundBuffer registeredBuffer = new SoundBuffer(CurrentHost, path, radius);
					Buffers.Add(registeredBuffer);
					return registeredBuffer;
				}
				catch
				{
					return null;
				}
			}
		}

		public SoundBuffer RegisterBuffer(Sound data, double radius)
		{
			lock (buffersLock)
			{
				try
				{
					SoundBuffer registeredBuffer = new SoundBuffer(data, radius);
					Buffers.Add(registeredBuffer);
					return registeredBuffer;
				}
				catch
				{
					return null;
				}
			}
		}

		public void LoadBuffer(SoundBuffer buffer)
		{
			SoundLoaderQueue.Enqueue(buffer.Load);
		}

		internal void UnloadAllBuffers()
		{
			lock (buffersLock)
			{
				foreach (var buffer in Buffers) buffer.Unload();
				Buffers.Clear();
			}
		}

		public SoundSource PlaySound(SoundHandle buffer, double pitch, double volume, OpenBveApi.Math.Vector3 position, object parent, bool looped)
		{
			if (buffer is SoundBuffer b)
			{
				if (Sources.Length == SourceCount)
				{
					Array.Resize(ref Sources, Sources.Length << 1);
				}
				Sources[SourceCount] = new SoundSource(b, b.Radius, pitch, volume, position, parent, looped);
				SourceCount++;
				return Sources[SourceCount - 1];
			}
			return null;
		}

		public void StopSound(SoundSource source)
		{
			if (source != null)
			{
				source.Stop();
			}
		}

		public void StopAllSounds()
		{
			for (int i = 0; i < SourceCount; i++)
			{
				Sources[i]?.Stop();
			}
		}

		public virtual void StopAllSounds(object train)
		{
			for (int i = 0; i < SourceCount; i++)
			{
				if (Sources[i] != null && Sources[i].Parent == train)
				{
					Sources[i].Stop();
				}
			}
		}
	}
}
