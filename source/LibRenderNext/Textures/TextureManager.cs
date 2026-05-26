using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using LibRenderNext.Screens;
using OpenBveApi;
using OpenBveApi.Hosts;
using OpenBveApi.Textures;
using OpenTK.Graphics.OpenGL;
using InterpolationMode = OpenBveApi.Graphics.InterpolationMode;
using PixelFormat = OpenBveApi.Textures.PixelFormat;

namespace LibRenderNext.Textures
{
	/// <summary>Provides functions for dealing with textures.</summary>
	public class TextureManager
	{
		private readonly HostInterface currentHost;

		private readonly BaseRenderer renderer;

		/// <summary>Holds all currently registered textures.</summary>
		public static List<Texture> RegisteredTextures;
		/// <summary>Holds cached texture origins</summary>
		internal static Dictionary<TextureOrigin, Texture> textureCache = new Dictionary<TextureOrigin, Texture>();

		private static Dictionary<TextureOrigin, Texture> animatedTextures;

		private struct TextureLookupKey : IEquatable<TextureLookupKey>
		{
			public readonly string Path;
			public readonly TextureParameters Parameters;

			public TextureLookupKey(string path, TextureParameters parameters)
			{
				Path = path ?? string.Empty;
				Parameters = parameters;
			}

			public bool Equals(TextureLookupKey other)
			{
				if (!string.Equals(Path, other.Path, StringComparison.InvariantCultureIgnoreCase))
				{
					return false;
				}
				if (ReferenceEquals(Parameters, other.Parameters))
				{
					return true;
				}
				if (Parameters is null || other.Parameters is null)
				{
					return false;
				}
				return Parameters == other.Parameters;
			}

			public override bool Equals(object obj)
			{
				return obj is TextureLookupKey other && Equals(other);
			}

			public override int GetHashCode()
			{
				unchecked
				{
					int hash = StringComparer.InvariantCultureIgnoreCase.GetHashCode(Path);
					if (Parameters != null)
					{
						if (Parameters.ClipRegion != null)
						{
							hash = hash * 397 ^ Parameters.ClipRegion.Left;
							hash = hash * 397 ^ Parameters.ClipRegion.Top;
							hash = hash * 397 ^ Parameters.ClipRegion.Width;
							hash = hash * 397 ^ Parameters.ClipRegion.Height;
						}
						if (Parameters.TransparentColor != null)
						{
							hash = hash * 397 ^ Parameters.TransparentColor.Value.GetHashCode();
						}
					}
					return hash;
				}
			}
		}

		private static Dictionary<TextureLookupKey, Texture> lookupCache = new Dictionary<TextureLookupKey, Texture>();

		/// <summary>The number of currently registered textures.</summary>
		public int RegisteredTexturesCount => RegisteredTextures.Count;

		internal TextureManager(HostInterface CurrentHost, BaseRenderer Renderer)
		{
			currentHost = CurrentHost;
			RegisteredTextures = new List<Texture>();
			renderer = Renderer;
			animatedTextures = new Dictionary<TextureOrigin, Texture>();
			lookupCache = new Dictionary<TextureLookupKey, Texture>();
		}


		// --- register texture ---

		/// <summary>Registers a texture and returns a handle to the texture.</summary>
		/// <param name="path">The path to the file or directory that contains the texture.</param>
		/// <param name="handle">Receives a handle to the texture.</param>
		/// <returns>Whether registering the texture was successful.</returns>
		public bool RegisterTexture(string path, out Texture handle)
		{
			return RegisterTexture(path, null, out handle);
		}

		/// <summary>Registers a texture and returns a handle to the texture.</summary>
		/// <param name="path">The path to the texture.</param>
		/// <param name="parameters">The parameters that specify how to process the texture.</param>
		/// <param name="handle">Receives a handle to the texture.</param>
		/// <returns>Whether registering the texture was successful.</returns>
		public bool RegisterTexture(string path, TextureParameters parameters, out Texture handle)
		{
			if (!File.Exists(path))
			{
				handle = null;
				return false;
			}

			TextureLookupKey key = new TextureLookupKey(path, parameters);
			if (lookupCache.TryGetValue(key, out handle))
			{
				return true;
			}

			handle = new Texture(new PathOrigin(path, parameters, currentHost));
			RegisteredTextures.Add(handle);
			lookupCache[key] = handle;
			return true;
		}

		/// <summary>Registers a texture and returns a handle to the texture.</summary>
		/// <param name="texture">The texture data.</param>
		/// <returns>The handle to the texture.</returns>
		public Texture RegisterTexture(Texture texture)
		{
			RegisteredTextures.Add(texture);
			if (texture.Origin is PathOrigin pathOrigin)
			{
				TextureLookupKey key = new TextureLookupKey(pathOrigin.Path, pathOrigin.Parameters);
				lookupCache[key] = texture;
			}
			return texture;
		}

		/// <summary>Registers a texture and returns a handle to the texture.</summary>
		/// <param name="bitmap">The bitmap that contains the texture.</param>
		/// <param name="parameters">The parameters that specify how to process the texture.</param>
		/// <returns>The handle to the texture.</returns>
		/// <remarks>Be sure not to dispose of the bitmap after calling this function.</remarks>
		public Texture RegisterTexture(Bitmap bitmap, TextureParameters parameters)
		{
			Texture texture = new Texture(bitmap, parameters);
			RegisteredTextures.Add(texture);
			return texture;
		}

		/// <summary>Registers a texture and returns a handle to the texture.</summary>
		/// <param name="bitmap">The bitmap that contains the texture.</param>
		/// <returns>The handle to the texture.</returns>
		/// <remarks>Be sure not to dispose of the bitmap after calling this function.</remarks>
		public Texture RegisterTexture(Bitmap bitmap)
		{
			Texture texture = new Texture(bitmap);
			RegisteredTextures.Add(texture);
			return texture;
		}


		// --- load texture ---

		/// <summary>Loads the specified texture into OpenGL if not already loaded.</summary>
		/// <param name="handle">The handle to the registered texture.</param>
		/// <param name="wrap">The texture type indicating the clamp mode.</param>
		/// <param name="currentTicks">The current system clock-ticks</param>
		/// <param name="Interpolation">The interpolation mode to use when loading the texture</param>
		/// <param name="AnisotropicFilteringLevel">The anisotropic filtering level to use when loading the texture</param>
		/// <returns>Whether loading the texture was successful.</returns>
		public bool LoadTexture(ref Texture handle, OpenGlTextureWrapMode wrap, int currentTicks, InterpolationMode Interpolation, int AnisotropicFilteringLevel)
		{
			if (handle == null || handle.OpenGlTextures == null)
			{
				return false;
			}
			
			if (handle.MultipleFrames)
			{
				Texture texture = null;
				if (!animatedTextures.TryGetValue(handle.Origin, out texture))
				{
					if (!handle.Origin.GetTexture(out texture))
					{
						return false;
					}
					animatedTextures.Add(handle.Origin, texture);
				}
				
				double elapsedTime = CPreciseTimer.GetElapsedTime(handle.LastAccess, currentTicks);
				int elapsedFrames = (int)(elapsedTime / texture.FrameInterval);
				if (elapsedFrames > 0)
				{
					texture.CurrentFrame += elapsedFrames;
					texture.CurrentFrame %= texture.TotalFrames;
					handle.LastAccess = currentTicks;
				}
				handle = texture;
			}
			else
			{
				handle.LastAccess = currentTicks;
			}

			if (handle.OpenGlTextures[(int)wrap].Valid)
			{
				return true;
			}

			if (handle.Ignore)
			{
				return false;
			}

			var task = TextureUploadQueue.AsyncTasks.GetOrAdd(handle, h => new TextureLoadTask());

			if (task.State == AsyncLoadState.NotStarted)
			{
				task.State = AsyncLoadState.Decoding;
				Texture captureHandle = handle;
				System.Threading.ThreadPool.QueueUserWorkItem(_ =>
				{
					try
					{
						Texture decoded;
						if (captureHandle.Origin.GetTexture(out decoded))
						{
							task.DecodedTexture = decoded;
							task.State = AsyncLoadState.ReadyToUpload;
							TextureUploadQueue.Queue.Enqueue(new UploadJob { Handle = captureHandle, Wrap = wrap, DecodedTexture = decoded });
						}
						else
						{
							task.State = AsyncLoadState.Failed;
						}
					}
					catch (Exception ex)
					{
						task.Error = ex;
						task.State = AsyncLoadState.Failed;
					}
				});
			}

			TextureUploadQueue.ProcessUploadQueue(Interpolation, AnisotropicFilteringLevel, renderer, currentHost);

			if (handle.OpenGlTextures[(int)wrap].Valid)
			{
				return true;
			}

			return false;
		}

		/// <summary>Unloads the specified texture from OpenGL if loaded.</summary>
		/// <param name="handle">The handle to the registered texture.</param>
		public static void UnloadTexture(ref Texture handle)
		{
			//Null check the texture handle, as otherwise this can cause OpenGL to throw a fit
			if (handle == null)
			{
				return;
			}

			if (handle.MultipleFrames)
			{
				for (int i = 0; i < handle.TotalFrames; i++)
				{
					handle.CurrentFrame = i;
					foreach (OpenGlTexture t in handle.OpenGlTextures)
					{
						if (t.Valid)
						{
							GL.DeleteTextures(1, new[] { t.Name });
							t.Valid = false;
						}
					}
				}
				/*
				 * Clone the ref for the search and then re-create the original in the texturemanager array
				 * This allows it to be re-loaded from disk
				 */
				var texture = handle;
				TextureOrigin key = animatedTextures.FirstOrDefault(x => x.Value == texture).Key;
				handle = new Texture(key);
			}
			else
			{
				foreach (OpenGlTexture t in handle.OpenGlTextures)
				{
					if (t.Valid)
					{
						GL.DeleteTextures(1, new[] { t.Name });
						t.Valid = false;
					}
				}
			}
			handle.Ignore = false;
			if (handle.Origin != null)
			{
				textureCache.Remove(handle.Origin);
				if (handle.Origin is PathOrigin pathOrigin)
				{
					TextureLookupKey key = new TextureLookupKey(pathOrigin.Path, pathOrigin.Parameters);
					lookupCache.Remove(key);
				}
			}
		}

		/// <summary>Loads all registered textures.</summary>
		public void LoadAllTextures()
		{
			for (int i = 0; i < RegisteredTextures.Count; i++)
			{
				for (int j = 0; j < 4; j++)
				{
					if (RegisteredTextures[i] != null && RegisteredTextures[i].OpenGlTextures[j].Used)
					{
						Texture t = RegisteredTextures[i];
						LoadTexture(ref t, (OpenGlTextureWrapMode)j, CPreciseTimer.GetClockTicks(), renderer.currentOptions.Interpolation, renderer.currentOptions.AnisotropicFilteringLevel);
						RegisteredTextures[i] = t;
					}
				}
			}
		}

		/// <summary>Unloads all registered textures.</summary>
		public void UnloadAllTextures(bool currentlyReloading)
		{
			for (int i = 0; i < RegisteredTextures.Count; i++)
			{
				Texture t = RegisteredTextures[i];
				UnloadTexture(ref t);
				RegisteredTextures[i] = t;
			}
			if (currentlyReloading)
			{
				foreach(TextureOrigin origin in textureCache.Keys.ToList())
				{
					if (origin is PathOrigin pathOrigin)
					{
						if (!File.Exists(pathOrigin.Path) || pathOrigin.FileSize != new FileInfo(pathOrigin.Path).Length || pathOrigin.LastModificationTime != File.GetLastWriteTime(pathOrigin.Path))
						{
							textureCache.Remove(origin);
							TextureLookupKey key = new TextureLookupKey(pathOrigin.Path, pathOrigin.Parameters);
							lookupCache.Remove(key);
						}
					}
				}
			}
			else
			{
				textureCache.Clear();
				lookupCache.Clear();
			}
			
			GC.Collect(); //Speculative- https://bveworldwide.forumotion.com/t1873-object-routeviewer-out-of-memory#19423
		}

		/// <summary>Unloads any textures which have not been accessed</summary>
		/// <param name="TimeElapsed">The time elapsed since the last call to this function</param>
		public void UnloadUnusedTextures(double TimeElapsed)
		{
#if DEBUG
			//HACK: If when running in debug mode the frame time exceeds 1s, we can assume VS has hit a breakpoint
			//Don't unload textures in this case, as it just causes texture bugs
			if (TimeElapsed > 1000)
			{
				foreach (var Texture in RegisteredTextures)
				{
					if (Texture != null)
					{
						Texture.LastAccess = CPreciseTimer.GetClockTicks();
					}
				}
			}
#endif
			if (renderer.CurrentInterface == InterfaceType.Normal)
			{
				for (int i = 0; i < RegisteredTextures.Count; i++)
				{
					if (RegisteredTextures[i] != null && RegisteredTextures[i].AvailableToUnload && (CPreciseTimer.GetClockTicks() - RegisteredTextures[i].LastAccess) > 20000)
					{
						Texture t = RegisteredTextures[i];
						UnloadTexture(ref t);
						RegisteredTextures[i] = t;
					}
				}
			}
			else
			{
				//Don't unload textures if we are in a menu/ paused, as they may be required immediately after unpause
				foreach (Texture Texture in RegisteredTextures)
				{
					//Texture can be null in certain cases....
					if (Texture != null)
					{
						Texture.LastAccess = CPreciseTimer.GetClockTicks();
					}
				}
			}
		}


		// --- statistics ---

		/// <summary>Gets the number of registered textures.</summary>
		/// <returns>The number of registered textures.</returns>
		public int GetNumberOfRegisteredTextures()
		{
			return RegisteredTextures.Count;
		}

		/// <summary>Gets the number of loaded textures.</summary>
		/// <returns>The number of loaded textures.</returns>
		public int GetNumberOfLoadedTextures()
		{
			int count = 0;

			for (int i = 0; i < RegisteredTextures.Count; i++)
			{
				if (RegisteredTextures[i] == null || RegisteredTextures[i].MultipleFrames)
				{
					continue;
				}

				if (RegisteredTextures[i].OpenGlTextures.Any(t => t.Valid))
				{
					count++;
				}
			}
			return count;
		}

		public int GetNumberOfLoadedAnimatedTextures()
		{
			int count = 0;
			for (int i = 0; i < RegisteredTextures.Count; i++)
			{
				if (RegisteredTextures[i] == null || RegisteredTextures[i].MultipleFrames == false)
				{
					continue;
				}

				if (RegisteredTextures[i].OpenGlTextures.Any(t => t.Valid))
				{
					count++;
				}
			}
			return count;
		}


		/// <summary>Gets the next free texture, resizing the base textures array if appropriate</summary>
		/// <returns>The index of the next free texture</returns>
		public int GetNextFreeTexture()
		{
			return RegisteredTextures.Count;
		}


		// --- functions ---

		/// <summary>Takes a positive value and rounds it up to the next highest power of two.</summary>
		/// <param name="value">The value.</param>
		/// <returns>The next highest power of two, or the original value if already a power of two.</returns>
		public int RoundUpToPowerOfTwo(int value)
		{
			if (value <= 0)
			{
				throw new ArgumentException("The specified value is not positive.");
			}

			value -= 1;

			for (int i = 1; i < sizeof(int) * 8; i <<= 1)
			{
				value |= value >> i;
			}

			return value + 1;
		}
	}
}
