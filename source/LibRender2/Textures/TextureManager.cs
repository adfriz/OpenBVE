using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using LibRender2.Objects;
using LibRender2.Screens;
using OpenBveApi;
using OpenBveApi.Hosts;
using OpenBveApi.Objects;
using OpenBveApi.Textures;
using OpenTK.Graphics.OpenGL;
using InterpolationMode = OpenBveApi.Graphics.InterpolationMode;
using PixelFormat = OpenBveApi.Textures.PixelFormat;

namespace LibRender2.Textures
{
	/// <summary>Provides functions for dealing with textures.</summary>
	public class TextureManager
	{
		private readonly HostInterface currentHost;

		private readonly BaseRenderer renderer;

		/// <summary>Holds all currently registered textures.</summary>
		public static Texture[] RegisteredTextures;
		/// <summary>Holds cached texture origins</summary>
		internal static Dictionary<TextureOrigin, Texture> textureCache = new Dictionary<TextureOrigin, Texture>();

		/// <summary>Total time spent decoding texture files, in milliseconds.</summary>
		public static long TextureDecodeTime;

		/// <summary>Number of texture upload requests handled.</summary>
		public static long UploadCount;

		/// <summary>Time spent in texture upload requests, including cached-handle early exits (ms).</summary>
		public static long UploadMs;

		private static Dictionary<TextureOrigin, Texture> animatedTextures;
		// Reused buffer for paletted GIF to avoid per-frame new byte[] leak (Can get StackOverflow in glTexSubImage2D)
		private static byte[] _palettedExpandBuffer;
		private static readonly object _expandLock = new object();

		/// <summary>Holds the registered path-based textures, indexed by path.</summary>
		private static readonly Dictionary<string, List<Texture>> RegisteredTextureLookup = new Dictionary<string, List<Texture>>(StringComparer.OrdinalIgnoreCase);

		private static readonly object TextureLookupLock = new object();

		/// <summary>The number of currently registered textures.</summary>
		public int RegisteredTexturesCount;

		/// <summary>Maximum number of new texture uploads per frame (lazy upload).</summary>
		/// <remarks>Excess uploads are deferred to following frames so a camera teleport or fast
		/// movement spreads the hitch instead of freezing one frame. Deferred faces render
		/// untextured for that frame and retry on the next. Animated frame updates
		/// (TexSubImage2D) are exempt. Loading screens bypass via LoadAllTextures.</remarks>
		public int MaxUploadsPerFrame = 2;
		private int uploadsThisWindow;
		private int uploadWindowStart;

		/// <summary>Whether distance-tiered streaming is active (mirrors the UnloadUnusedTextures option).</summary>
		/// <remarks>Set every frame by the game window. Viewers leave it false for full-quality uploads.</remarks>
		public bool StreamingActive;
		/// <summary>Whether driver-side block compression is active (requires StreamingActive).</summary>
		/// <remarks>Set every frame by the game window from the Texture compression option.</remarks>
		public bool CompressionActive;
		/// <summary>Configured resident texture budget in megabytes (0 = automatic).</summary>
		public int StreamingBudgetMB;
		private int lastTierPassTick;

		internal TextureManager(HostInterface CurrentHost, BaseRenderer Renderer)
		{
			currentHost = CurrentHost;
			RegisteredTextures = new Texture[16];
			RegisteredTexturesCount = 0;
			renderer = Renderer;
			animatedTextures = new Dictionary<TextureOrigin, Texture>();
			uploadWindowStart = CPreciseTimer.GetClockTicks();
		}

		/// <summary>Resets the per-frame upload budget. Called once per render frame.</summary>
		public void BeginFrame()
		{
			uploadsThisWindow = 0;
			uploadWindowStart = CPreciseTimer.GetClockTicks();
		}

		/// <summary>Tries to claim one upload slot from the per-frame budget.</summary>
		/// <remarks>The window also resets on a timeout so render loops that never call
		/// BeginFrame (viewers) degrade to a rate limit instead of stalling forever.</remarks>
		/// <returns>Whether an upload may proceed this frame.</returns>
		private bool ClaimUploadSlot()
		{
			int now = CPreciseTimer.GetClockTicks();
			if (now - uploadWindowStart > 100)
			{
				uploadsThisWindow = 0;
				uploadWindowStart = now;
			}
			if (uploadsThisWindow >= MaxUploadsPerFrame)
			{
				return false;
			}
			uploadsThisWindow++;
			return true;
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
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				// shouldn't happen, but handle gracefully
				handle = null;
				return false;
			}
			/* BUG:
			 * The registered textures count very occasional becomes greater than the array length (Texture loader crashes possibly?)
			 * This then crashes when we attempt to itinerate the array, so reset it...
			 */
			if (RegisteredTexturesCount > RegisteredTextures.Length)
			{
				RegisteredTexturesCount = RegisteredTextures.Length;
			}

			/*
			 * Check if the texture is already registered.
			 * If so, return the existing handle.
			 * */
			lock (TextureLookupLock)
			{
				if (RegisteredTextureLookup.TryGetValue(path, out List<Texture> candidates))
				{
					for (int i = 0; i < candidates.Count; i++)
					{
						try
						{
							PathOrigin source = candidates[i].Origin as PathOrigin;

							if (source != null && source.Parameters == parameters)
							{
								handle = candidates[i];
								return true;
							}
						}
						catch
						{
							// ignored
						}
					}
				}
			}

			/*
			 * Register the texture and return the newly created handle.
			 * */
			int idx = GetNextFreeTexture();
			RegisteredTextures[idx] = new Texture(path, parameters, currentHost);
			RegisteredTexturesCount++;
			handle = RegisteredTextures[idx];
			// New handles participate in UnloadUnusedTextures LRU by default;
			// pinned textures (animated objects) opt out explicitly after load.
			handle.AvailableToUnload = true;
			handle.LastAccess = CPreciseTimer.GetClockTicks();

			lock (TextureLookupLock)
			{
				/*
				 * Pre-seed the texture cache with the decoded texture (not the handle).
				 * The handle itself has no decoded bytes, so storing it would cause a null
				 * reference when the transparency type is subsequently queried.
				 * */
				if (handle.PixelFormat != PixelFormat.Invalid && handle.DecodedTexture != null && !textureCache.ContainsKey(handle.Origin))
				{
					textureCache.Add(handle.Origin, handle.DecodedTexture);
				}

				/*
				 * Maintain the registration lookup table.
				 * */
				if (!RegisteredTextureLookup.TryGetValue(path, out List<Texture> list))
				{
					list = new List<Texture>();
					RegisteredTextureLookup[path] = list;
				}
				list.Add(handle);
			}
			return true;
		}

		/// <summary>Registers a texture and returns a handle to the texture.</summary>
		/// <param name="texture">The texture data.</param>
		/// <returns>The handle to the texture.</returns>
		public Texture RegisterTexture(Texture texture)
		{
			/*
			 * Register the texture and return the newly created handle.
			 * */
			int idx = GetNextFreeTexture();
			RegisteredTextures[idx] = new Texture(texture);
			RegisteredTextures[idx].AvailableToUnload = true;
			RegisteredTextures[idx].LastAccess = CPreciseTimer.GetClockTicks();
			RegisteredTexturesCount++;
			return RegisteredTextures[idx];
		}

		/// <summary>Registers a texture and returns a handle to the texture.</summary>
		/// <param name="bitmap">The bitmap that contains the texture.</param>
		/// <param name="parameters">The parameters that specify how to process the texture.</param>
		/// <returns>The handle to the texture.</returns>
		/// <remarks>Be sure not to dispose of the bitmap after calling this function.</remarks>
		public Texture RegisterTexture(Bitmap bitmap, TextureParameters parameters)
		{
			/*
			 * Register the texture and return the newly created handle.
			 * */
			int idx = GetNextFreeTexture();
			RegisteredTextures[idx] = new Texture(bitmap, parameters);
			RegisteredTextures[idx].AvailableToUnload = true;
			RegisteredTextures[idx].LastAccess = CPreciseTimer.GetClockTicks();
			RegisteredTexturesCount++;
			return RegisteredTextures[idx];
		}

		/// <summary>Registers a texture and returns a handle to the texture.</summary>
		/// <param name="bitmap">The bitmap that contains the texture.</param>
		/// <returns>The handle to the texture.</returns>
		/// <remarks>Be sure not to dispose of the bitmap after calling this function.</remarks>
		public Texture RegisterTexture(Bitmap bitmap)
		{
			/*
			 * Register the texture and return the newly created handle.
			 * */
			int idx = GetNextFreeTexture();
			RegisteredTextures[idx] = new Texture(bitmap);
			RegisteredTextures[idx].AvailableToUnload = true;
			RegisteredTextures[idx].LastAccess = CPreciseTimer.GetClockTicks();
			RegisteredTexturesCount++;
			return RegisteredTextures[idx];
		}


		// --- load texture ---

		/// <summary>Loads the specified texture into OpenGL if not already loaded.</summary>
		/// <param name="handle">The handle to the registered texture.</param>
		/// <param name="wrap">The texture type indicating the clamp mode.</param>
		/// <param name="currentTicks">The current system clock-ticks</param>
		/// <param name="Interpolation">The interpolation mode to use when loading the texture</param>
		/// <param name="AnisotropicFilteringLevel">The anisotropic filtering level to use when loading the texture</param>
		/// <param name="bypassBudget">When true, ignores the per-frame lazy-upload budget (loading screens).</param>
		/// <returns>Whether loading the texture was successful.</returns>
		public bool LoadTexture(ref Texture handle, OpenGlTextureWrapMode wrap, int currentTicks, InterpolationMode Interpolation, int AnisotropicFilteringLevel, bool bypassBudget = false)
		{
			Stopwatch uploadTimer = Stopwatch.StartNew();
			bool result = LoadTextureInternal(ref handle, wrap, currentTicks, Interpolation, AnisotropicFilteringLevel, bypassBudget);
			UploadCount++;
			UploadMs += uploadTimer.ElapsedMilliseconds;
			return result;
		}

		private bool LoadTextureInternal(ref Texture handle, OpenGlTextureWrapMode wrap, int currentTicks, InterpolationMode Interpolation, int AnisotropicFilteringLevel, bool bypassBudget = false)
		{

			Texture texture = null;
			//Don't try to load a texture to a null handle, this is a seriously bad idea....
			if (handle == null || handle.OpenGlTextures == null)
			{
				return false;
			}
			
			if (handle.MultipleFrames)
			{
			if (!animatedTextures.TryGetValue(handle.Origin, out texture))
			{
				// Reuse register-time decode from textureCache where possible to avoid decoding the same large animated GIF twice (2× memory). See RegisterTexture pre-seed at line 136.
				lock (TextureLookupLock)
				{
						if (textureCache.TryGetValue(handle.Origin, out Texture cachedTexture) && cachedTexture.MultipleFrames)
						{
							PathOrigin cachedPathOrigin = cachedTexture.Origin as PathOrigin;
							PathOrigin handlePathOrigin = handle.Origin as PathOrigin;
							// PathOrigin equality is path-only, so check Parameters explicitly; ByteArrayOrigin path never hits here (handled below)
							if (cachedPathOrigin != null && handlePathOrigin != null)
							{
								if (cachedPathOrigin.Parameters == handlePathOrigin.Parameters)
									texture = cachedTexture;
							}
							else if (handle.Origin is ByteArrayOrigin || cachedTexture.Origin is ByteArrayOrigin)
							{
								texture = cachedTexture;
							}
							else if (cachedPathOrigin == null && handlePathOrigin == null)
							{
								texture = cachedTexture;
							}
						}
					}
				if (texture == null)
				{
					// Reuse the register-time decode when the on-disk file is unchanged (the caches
					// were dropped by the unload): avoids decoding the same GIF into a 2nd pixel copy.
					// (DecodedTexture already has this handle's parameters applied at registration.)
					if (handle.Origin is PathOrigin && TextureFileUnchanged(handle.Origin) && handle.DecodedTexture != null && handle.DecodedTexture.MultipleFrames)
					{
						texture = handle.DecodedTexture;
					}
				}
				if (texture == null)
				{
					if (!handle.Origin.GetTexture(out texture))
					{
						//Loading animated texture barfed
						return false;
					}
				}
				animatedTextures.Add(handle.Origin, texture);
			}
				
				double elapsedTime = CPreciseTimer.GetElapsedTime(handle.LastAccess, currentTicks);
				int elapsedFrames = (int)(elapsedTime / texture.FrameInterval);
				if (elapsedFrames > 0)
				{
					int oldFrame = texture.CurrentFrame;
					texture.CurrentFrame += elapsedFrames;
					texture.CurrentFrame %= texture.TotalFrames;
					handle.LastAccess = currentTicks;
					// If frame changed and GL texture already uploaded, update in-place via TexSubImage2D to avoid creating a lot of GL calls and per-frame alloc leak
					if (oldFrame != texture.CurrentFrame && texture.OpenGlTextures[(int)wrap].Valid && handle.OpenGlTextures[(int)wrap].Valid)
					{
						// Reuse same GL name across frames, update existing texture
						GL.BindTexture(TextureTarget.Texture2D, handle.OpenGlTextures[(int)wrap].Name);
						byte[] subBytes = texture.Bytes; // current frame's bytes (paletted or RGBA)
						// For paletted, expand via reused static buffer to avoid per-frame new byte[] GC pressure. (I think this how web browser do frame discarding)
						if (texture.PixelFormat == PixelFormat.Paletted)
						{
							bool opaque = texture.GetTransparencyType() == TextureTransparencyType.Opaque;
							int need = texture.Width * texture.Height * (opaque ? 3 : 4);
							// Hold the lock across fill + upload: the static buffer is shared, so releasing
							// the lock before TexSubImage2D would let a concurrent upload corrupt the data.
							lock (_expandLock)
							{
								if (_palettedExpandBuffer == null || _palettedExpandBuffer.Length < need) _palettedExpandBuffer = new byte[need];
								byte[] pooled = _palettedExpandBuffer;
								var pal = texture.Palette32;
								if (opaque)
								{
									for (int p = 0; p < texture.Width * texture.Height; p++)
									{
										int idx = subBytes[p] & 0xFF;
										var c = pal != null && idx < pal.Length ? pal[idx] : new OpenBveApi.Colors.Color32(0,0,0,255);
										pooled[p*3] = c.R; pooled[p*3+1] = c.G; pooled[p*3+2] = c.B;
									}
									GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
									GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, texture.Width, texture.Height, OpenTK.Graphics.OpenGL.PixelFormat.Rgb, PixelType.UnsignedByte, pooled);
								}
								else
								{
									for (int p = 0; p < texture.Width * texture.Height; p++)
									{
										int idx = subBytes[p] & 0xFF;
										var c = pal != null && idx < pal.Length ? pal[idx] : new OpenBveApi.Colors.Color32(0,0,0,255);
										pooled[p*4] = c.R; pooled[p*4+1] = c.G; pooled[p*4+2] = c.B; pooled[p*4+3] = c.A;
									}
									GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
									GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, texture.Width, texture.Height, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, pooled);
								}
							}
							GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
						}
						else
						{
							// RGB/RGBA direct – no expansion
							if (texture.PixelFormat == PixelFormat.RGBAlpha || texture.GetTransparencyType() != TextureTransparencyType.Opaque)
							{
								GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
								GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, texture.Width, texture.Height, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, subBytes);
							}
							else if (texture.PixelFormat == PixelFormat.RGB)
							{
								GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
								GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, texture.Width, texture.Height, OpenTK.Graphics.OpenGL.PixelFormat.Rgb, PixelType.UnsignedByte, subBytes);
							}
							else
							{
								GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
								GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, texture.Width, texture.Height, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, subBytes);
							}
							GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
						}
						// Keep the stable handle in sync without swapping identity: the handle keeps
						// its origin so the animated cache keeps hitting every tick (swapping to the
						// decoded texture gives it a fresh ByteArrayOrigin and the cache never hits again)
						handle.CurrentFrame = texture.CurrentFrame;
						return true;
					}
				}
			}
			else
			{
				handle.LastAccess = currentTicks;
			}
			//Set last access time

			// Only early-out when there is nothing new to upload: a freshly decoded texture
			// whose GL slot is not uploaded yet must fall through to the upload section,
			// even if the handle still carries a stale Valid flag from before an unload.
			if (handle.OpenGlTextures[(int)wrap].Valid && (texture == null || texture.OpenGlTextures[(int)wrap].Valid))
			{
				return true;
			}

			
			
			if (handle.Ignore)
			{
				return false;
			}

			if (texture == null)
			{
				/*
				 * Reuse the register-time decode held by the texture cache where possible,
				 * as the cached instance already has the origin's parameters applied,
				 * avoiding a second full decode of the same file.
				 * NB: Origin equality only compares the path, so the cached instance must be
				 * checked against this handle's parameters before reuse- files registered with
				 * differing parameters (e.g. different clip regions) require their own decode.
				 */
				lock (TextureLookupLock)
				{
					Texture cachedTexture;
					if (textureCache.TryGetValue(handle.Origin, out cachedTexture))
					{
						// The cache value is the DecodedTexture (ByteArrayOrigin) created at registration – its Origin is not PathOrigin,
						// so the original check (cachedPathOrigin != null) never succeeds for decoded GIFs and caused a second decode (2× memory).
						// Reuse if the cached entry is animated (GIF video) or its ByteArrayOrigin, and parameters match (or both null).
						if (cachedTexture.MultipleFrames)
						{
							// Animated: reuse single copy to halve memory usage for large GIFs
							texture = cachedTexture;
						}
						else
						{
							PathOrigin cachedPathOrigin = cachedTexture.Origin as PathOrigin;
							PathOrigin handlePathOrigin = handle.Origin as PathOrigin;
							if (cachedPathOrigin != null && handlePathOrigin != null && cachedPathOrigin.Parameters == handlePathOrigin.Parameters)
							{
								texture = cachedTexture;
							}
							else if (cachedTexture.Origin is ByteArrayOrigin && handlePathOrigin != null)
							{
								// DecodedTexture path: key's Parameters are in handle.Origin; value has no Parameters to compare.
								// Reuse when handle has no special parameters to avoid duplicate decode.
								if (handlePathOrigin.Parameters == null)
									texture = cachedTexture;
							}
						}
					}
				}
			if (texture == null && handle.Origin != null)
			{
				// Reuse a live animated decode (e.g. after a reload dropped the register-time
				// pre-seed from the texture cache) instead of decoding the same GIF twice.
				if (animatedTextures.TryGetValue(handle.Origin, out Texture animatedTexture) && animatedTexture.MultipleFrames)
				{
					texture = animatedTexture;
				}
			}
			if (texture == null && handle.Origin is PathOrigin && TextureFileUnchanged(handle.Origin) && handle.DecodedTexture != null)
			{
				// Reuse the register-time decode when the on-disk file is unchanged (the cache
				// entry was dropped by the unload): same bytes GetTexture would decode, no 2nd copy.
				texture = handle.DecodedTexture;
			}
			if (texture == null)
			{
				handle.Origin.GetTexture(out texture);
			}
			}
			if (texture != null)
			{
				if (!bypassBudget && !ClaimUploadSlot())
				{
					// Lazy upload: budget for this frame is spent. Leave the handle invalid
					// (without marking Ignore) so the face renders untextured this frame and
					// retries on the next. This spreads teleport/fast-movement hitches.
					return false;
				}
				if (texture.MultipleFrames)
				{
					handle.MultipleFrames = true;
				}
				//if (texture.BitsPerPixel == 32)
				{
					int[] names = new int[1];
					GL.GenTextures(1, names);
					GL.BindTexture(TextureTarget.Texture2D, names[0]);
					handle.OpenGlTextures[(int)wrap].Name = names[0];
					if (texture.MultipleFrames)
					{
						texture.OpenGlTextures[(int)wrap].Name = names[0];
					}

					handle.Size = texture.Size;
					handle.Transparency = texture.GetTransparencyType();
					// Fetch the pixel data once; the getter may lazily re-decode released instances, which must not happen per access
					byte[] textureBytes = texture.Bytes;
					if (texture.Width <= 0 || texture.Height <= 0 || textureBytes == null || textureBytes.Length == 0)
					{
						// Nothing valid to upload: release the half-created GL texture and get out
						// before GenerateMipmap below can raise GL_INVALID_OPERATION on it
						GL.BindTexture(TextureTarget.Texture2D, 0);
						GL.DeleteTexture(names[0]);
						handle.OpenGlTextures[(int)wrap].Name = 0;
						return false;
					}
					int uploadTier = 0;
					if (StreamingActive && !bypassBudget && !texture.MultipleFrames)
					{
						uploadTier = handle.DesiredTier;
						if (uploadTier < 0 || uploadTier > TexturePolicy.MaxTier || TexturePolicy.IsProtected(texture))
						{
							uploadTier = 0;
						}
						if (uploadTier > 0)
						{
							Texture tierTexture = TextureDownscale.CreateTierTexture(texture, textureBytes, uploadTier, handle.Transparency == TextureTransparencyType.Opaque);
							if (!ReferenceEquals(tierTexture, texture))
							{
								texture = tierTexture;
								textureBytes = texture.Bytes;
							}
							else
							{
								uploadTier = 0;
							}
						}
					}
					switch (Interpolation)
					{
						case InterpolationMode.NearestNeighbor:
							GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (float)TextureMinFilter.Nearest);
							GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (float)TextureMagFilter.Nearest);
							break;
						case InterpolationMode.Bilinear:
							GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (float)TextureMinFilter.Linear);
							GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (float)TextureMagFilter.Linear);
							break;
						case InterpolationMode.NearestNeighborMipmapped:
							GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (float)TextureMinFilter.NearestMipmapNearest);
							GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (float)TextureMagFilter.Nearest);
							break;
						case InterpolationMode.BilinearMipmapped:
							GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (float)TextureMinFilter.NearestMipmapLinear);
							GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (float)TextureMagFilter.Linear);
							break;
						case InterpolationMode.TrilinearMipmapped:
							GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (float)TextureMinFilter.LinearMipmapLinear);
							GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (float)TextureMagFilter.Linear);
							break;
						default:
							GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (float)TextureMinFilter.LinearMipmapLinear);
							GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (float)TextureMagFilter.Linear);
							break;
					}

					if ((wrap & OpenGlTextureWrapMode.RepeatClamp) != 0)
					{
						GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (float)TextureWrapMode.Repeat);
					}
					else
					{
						GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (float)TextureWrapMode.ClampToEdge);
					}

					if ((wrap & OpenGlTextureWrapMode.ClampRepeat) != 0)
					{
						GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (float)TextureWrapMode.Repeat);
					}
					else
					{
						GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (float)TextureWrapMode.ClampToEdge);
					}

					if (Interpolation == InterpolationMode.AnisotropicFiltering && AnisotropicFilteringLevel > 0)
					{
						GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, AnisotropicFilteringLevel);
					}

					// Driver-side block compression for direct RGB/RGBA uploads (JPG/PNG scenery).
					// The driver compresses the uncompressed bytes on upload; expanded paletted and
					// grayscale paths below stay uncompressed. BC7 is preferred for quality, S3TC
					// (BC1 opaque, BC3 alpha) otherwise. Requires a successful capability probe.
					PixelInternalFormat rgbInternal = PixelInternalFormat.Rgb8;
					PixelInternalFormat rgbFromRgbaInternal = PixelInternalFormat.Rgb8;
					PixelInternalFormat rgbaInternal = PixelInternalFormat.Rgba8;
					int opaqueBlockBytes = 0;
					int alphaBlockBytes = 0;
					if (CompressionActive && !texture.MultipleFrames && TextureCapabilities.Probed)
					{
						if (TextureCapabilities.CanBPTC)
						{
							rgbInternal = PixelInternalFormat.CompressedRgbaBptcUnorm;
							rgbFromRgbaInternal = PixelInternalFormat.CompressedRgbaBptcUnorm;
							rgbaInternal = PixelInternalFormat.CompressedRgbaBptcUnorm;
							opaqueBlockBytes = 16;
							alphaBlockBytes = 16;
						}
						else if (TextureCapabilities.CanS3TC)
						{
							rgbInternal = PixelInternalFormat.CompressedRgbS3tcDxt1Ext;
							rgbFromRgbaInternal = PixelInternalFormat.CompressedRgbaS3tcDxt1Ext;
							rgbaInternal = PixelInternalFormat.CompressedRgbaS3tcDxt5Ext;
							opaqueBlockBytes = 8;
							alphaBlockBytes = 16;
						}
					}
					int compressedBlockBytes = 0;
					
					if (handle.Transparency == TextureTransparencyType.Opaque)
					{
						switch (texture.PixelFormat)
						{
							case PixelFormat.Paletted:
								{
									// Expand indexed to RGB (alpha discarded for opaque)
									byte[] expanded = new byte[texture.Width * texture.Height * 3];
									var pal = texture.Palette32;
									for (int p = 0; p < texture.Width * texture.Height; p++)
									{
										int idx = textureBytes[p] & 0xFF;
										var c = pal != null && idx < pal.Length ? pal[idx] : new OpenBveApi.Colors.Color32(0,0,0,255);
										expanded[p*3] = c.R; expanded[p*3+1] = c.G; expanded[p*3+2] = c.B;
									}
									GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
									GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb8, texture.Width, texture.Height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgb, PixelType.UnsignedByte, expanded);
									break;
								}
						case PixelFormat.Grayscale:
							// LUMINANCE was removed from GL core profiles (3.1+), and our shaders are
							// #version 410 core, so always upload via the Red channel with a swizzle
							// n.b. Make sure to set the unpack alignment as otherwise we corrupt textures where stride > width
							GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
							GL.TexImage2D(TextureTarget.Texture2D, 0,
								PixelInternalFormat.R8,
								texture.Width, texture.Height, 0,
								OpenTK.Graphics.OpenGL.PixelFormat.Red,
								PixelType.UnsignedByte, textureBytes);

							// Replicate the single red channel into RGB and force alpha to 1 (opaque).
							// TEXTURE_SWIZZLE_* only accepts the symbolic values ZERO/ONE/RED/GREEN/BLUE/ALPHA
							// (see the glTexParameter reference); All.Red is 0x1903 (was hardcoded as 6403) and All.One is 1.
							GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleRgba, new[] { (int)All.Red, (int)All.Red, (int)All.Red, (int)All.One });
							break;
						case PixelFormat.GrayscaleAlpha:
							// Opaque use: alpha channel is discarded, upload the gray bytes via the Red channel
							{
								byte[] gray = new byte[texture.Width * texture.Height];
								for (int p = 0; p < gray.Length; p++)
								{
									gray[p] = textureBytes[2 * p];
								}
								GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
								GL.TexImage2D(TextureTarget.Texture2D, 0,
									PixelInternalFormat.R8,
									texture.Width, texture.Height, 0,
									OpenTK.Graphics.OpenGL.PixelFormat.Red,
									PixelType.UnsignedByte, gray);
								GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleRgba, new[] { (int)All.Red, (int)All.Red, (int)All.Red, (int)All.One });
								break;
							}
							case PixelFormat.RGB:
								// send as is
								// n.b. Make sure to set the unpack alignment as otherwise we corrupt textures where stride > width
								GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
								GL.TexImage2D(TextureTarget.Texture2D, 0,
									rgbInternal,
									texture.Width, texture.Height, 0,
									OpenTK.Graphics.OpenGL.PixelFormat.Rgb,
									PixelType.UnsignedByte, textureBytes);
								compressedBlockBytes = opaqueBlockBytes;
								break;
							case PixelFormat.RGBAlpha:
								/*
								 * Opaque texture, so the alpha channel is discarded by the RGB internal format.
								 * Upload the RGBA data directly rather than stripping it CPU-side.
								 */
								GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
								GL.TexImage2D(TextureTarget.Texture2D, 0,
									rgbFromRgbaInternal,
									texture.Width, texture.Height, 0,
									OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
									PixelType.UnsignedByte, textureBytes);
								compressedBlockBytes = opaqueBlockBytes;
								break;
							default:
								// Unknown / invalid format: must not reach GenerateMipmap with no level-0 image
								GL.BindTexture(TextureTarget.Texture2D, 0);
								GL.DeleteTexture(names[0]);
								handle.OpenGlTextures[(int)wrap].Name = 0;
								return false;
						}
					}
					else
					{
						switch (texture.PixelFormat)
						{
						case PixelFormat.Paletted:
							{
								byte[] expanded = new byte[texture.Width * texture.Height * 4];
								var pal = texture.Palette32;
								for (int p = 0; p < texture.Width * texture.Height; p++)
								{
									int idx = textureBytes[p] & 0xFF;
									var c = pal != null && idx < pal.Length ? pal[idx] : new OpenBveApi.Colors.Color32(0,0,0,255);
									expanded[p*4] = c.R; expanded[p*4+1] = c.G; expanded[p*4+2] = c.B; expanded[p*4+3] = c.A;
								}
								GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
								GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, texture.Width, texture.Height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, expanded);
								break;
							}
					case PixelFormat.GrayscaleAlpha:
						// NOTE: LuminanceAlpha was removed from GL core profiles (3.1+), so always upconvert to RGBA
						{
							int stride = (4 * (texture.Width + 1) >> 2) << 2;
							byte[] newBytes = new byte[stride * texture.Height];
							int i = 0, j = 0;

							for (int y = 0; y < texture.Height; y++)
							{
								for (int x = 0; x < texture.Width; x++)
								{
									newBytes[j + 0] = textureBytes[i + 0];
									newBytes[j + 1] = textureBytes[i + 0];
									newBytes[j + 2] = textureBytes[i + 0];
									newBytes[j + 3] = textureBytes[i + 1];
									i += 2;
									j += 4;
								}

								j += stride - 4 * texture.Width;
							}
							GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
							GL.TexImage2D(TextureTarget.Texture2D, 0,
								PixelInternalFormat.Rgba8,
								texture.Width, texture.Height, 0,
								OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
								PixelType.UnsignedByte, newBytes);
						}
						break;
						case PixelFormat.Grayscale:
							// Transparent use of a gray image: expand to RGBA, fully opaque
							{
								byte[] expanded = new byte[texture.Width * texture.Height * 4];
								for (int p = 0; p < texture.Width * texture.Height; p++)
								{
									expanded[p * 4] = textureBytes[p];
									expanded[p * 4 + 1] = textureBytes[p];
									expanded[p * 4 + 2] = textureBytes[p];
									expanded[p * 4 + 3] = 255;
								}
								GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
								GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, texture.Width, texture.Height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, expanded);
								break;
							}
						case PixelFormat.RGB:
							// Transparent use of an RGB image: expand to RGBA, fully opaque
							{
								byte[] expanded = new byte[texture.Width * texture.Height * 4];
								for (int p = 0; p < texture.Width * texture.Height; p++)
								{
									expanded[p * 4] = textureBytes[p * 3];
									expanded[p * 4 + 1] = textureBytes[p * 3 + 1];
									expanded[p * 4 + 2] = textureBytes[p * 3 + 2];
									expanded[p * 4 + 3] = 255;
								}
								GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
								GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, texture.Width, texture.Height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, expanded);
								break;
							}
							case PixelFormat.RGBAlpha:
								/*
								* The texture uses its alpha channel, so send the bitmap data
								* in 32-bits per channel as-is.
								* */
								// n.b. Must reset the unpack alignment in case of changes
								GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
								GL.TexImage2D(TextureTarget.Texture2D, 0,
									rgbaInternal,
									texture.Width, texture.Height, 0,
									OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
									PixelType.UnsignedByte, textureBytes);
								compressedBlockBytes = alphaBlockBytes;
								break;
							default:
								// Unknown / invalid format: must not reach GenerateMipmap with no level-0 image
								GL.BindTexture(TextureTarget.Texture2D, 0);
								GL.DeleteTexture(names[0]);
								handle.OpenGlTextures[(int)wrap].Name = 0;
								return false;
						}
						
					}
					GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
                    handle.OpenGlTextures[(int)wrap].Valid = true;
					handle.ResidentTier = uploadTier;
					handle.TierChangeTick = currentTicks;
					handle.ResidentCompressed = compressedBlockBytes > 0;
					handle.ResidentBlockBytes = compressedBlockBytes;
					if (texture.MultipleFrames)
					{
						texture.OpenGlTextures[(int)wrap].Valid = true;
						// Cache the decode under the stable handle origin (upsert: the animated
						// block above may already have added it) so later ticks hit the cache
						// instead of re-decoding after a reload cleared everything.
						if (handle.Origin != null)
						{
							animatedTextures[handle.Origin] = texture;
						}
					}
					else
					{
						/*
						 * The pixel data now lives in OpenGL, so release the retained CPU-side copies:
						 * the transient upload instance, the register-time decode held by the handle,
						 * and the entry in the texture cache.
						 * The data is lazily re-decoded from the origin if required again.
						 */
						texture.ReleaseBytes();
						Texture cachedTexture = null;
						lock (TextureLookupLock)
						{
							if (textureCache.TryGetValue(handle.Origin, out cachedTexture))
							{
								// Compute the transparency type whilst the data is still available,
								// as otherwise a later query would have to re-decode the file from disk
								cachedTexture.GetTransparencyType();
								cachedTexture.ReleaseBytes();
							}
						}
						if (handle.DecodedTexture != null && handle.DecodedTexture != cachedTexture)
						{
							handle.DecodedTexture.GetTransparencyType();
							handle.DecodedTexture.ReleaseBytes();
						}
					}
					return true;
				}
			}

			handle.Ignore = true;
			return false;
		}

		/// <summary>Unloads the specified texture from OpenGL if loaded.</summary>
		/// <param name="handle">The handle to the registered texture.</param>
		/// <param name="preserveUnchangedCache">When true (route/object reload), decoded cache entries whose source file is unchanged are kept so the reloaded scene reuses them instead of decoding again.</param>
		public static void UnloadTexture(ref Texture handle, bool preserveUnchangedCache = false)
		{
			//Null check the texture handle, as otherwise this can cause OpenGL to throw a fit
			if (handle == null)
			{
				return;
			}

		if (handle.MultipleFrames)
		{
			// Single GL name design: one pass per frame slot, at least one pass.
			// (TotalFrames is 0 on re-created handles, which previously skipped deletion
			// entirely and leaked the live GL names with stale Valid flags.)
			int passes = handle.TotalFrames > 0 ? handle.TotalFrames : 1;
			for (int i = 0; i < passes; i++)
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
			// Preserve the origin directly so the texture can be re-loaded from disk.
			// (Do not gate this on animatedTextures.ContainsKey: that cache may already have
			// been cleared by UnloadAllTextures, which previously produced hollow handles with
			// a null origin here and killed animated GIFs after a reload / filtering change.)
			handle = new Texture(texture.Origin);
			// Animated handles stay pinned (pre-existing behavior): they are re-uploaded
			// per-frame via TexSubImage2D and must not be evicted by the LRU.
			handle.AvailableToUnload = false;
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
			handle.ResidentTier = -1;
			handle.ResidentCompressed = false;
			handle.ResidentBlockBytes = 0;
			if (handle.Origin != null)
			{
				// On reload, keep cache entries whose source file is unchanged: the reloaded
				// scene reuses the decode instead of decoding the same file again.
				// (UnloadAllTextures prunes changed files separately below.)
				if (!preserveUnchangedCache || !TextureFileUnchanged(handle.Origin))
				{
					lock (TextureLookupLock)
					{
						textureCache.Remove(handle.Origin);
					}
				}
			}
		}

		/// <summary>Loads all registered textures.</summary>
		/// <remarks>Bypasses the lazy-upload budget: loading screens expect blocking completion.</remarks>
		public void LoadAllTextures()
		{
			for (int i = 0; i < RegisteredTexturesCount; i++)
			{
				for (int j = 0; j < 4; j++)
				{
					if (RegisteredTextures[i] != null && RegisteredTextures[i].OpenGlTextures[j].Used)
					{
						LoadTexture(ref RegisteredTextures[i], (OpenGlTextureWrapMode)j, CPreciseTimer.GetClockTicks(), renderer.currentOptions.Interpolation, renderer.currentOptions.AnisotropicFilteringLevel, true);
					}

				}

			}
		}

		/// <summary>Unloads all registered textures.</summary>
		public void UnloadAllTextures(bool currentlyReloading)
		{
			// Always clear animated texture cache to prevent memory leak on reload:
			// animatedTextures retains decoded frame data for every GIF ever loaded,
			// doubling memory on each reload because old entries are never removed.
			animatedTextures.Clear();

			for (int i = 0; i < RegisteredTexturesCount; i++)
			{
				/*
				 * On a route reload, preserve textures whose source file is unchanged,
				 * so that the first frame after the reload does not re-upload every texture.
				 */
				if (currentlyReloading && RegisteredTextures[i] != null && !RegisteredTextures[i].MultipleFrames && TextureFileUnchanged(RegisteredTextures[i].Origin))
				{
					continue;
				}
				UnloadTexture(ref RegisteredTextures[i], currentlyReloading);
			}
			if (currentlyReloading)
			{
				lock (TextureLookupLock)
				{
					foreach (TextureOrigin origin in textureCache.Keys.ToList())
					{
						if (origin is PathOrigin && !TextureFileUnchanged(origin))
						{
							textureCache.Remove(origin);
						}
					}
				}
			}
			else
			{
				lock (TextureLookupLock)
				{
					textureCache.Clear();
				}
			}

			/*
			 * Rebuild the registration lookup table from the surviving textures,
			 * so that it does not retain handles which have been unloaded.
			 * */
			lock (TextureLookupLock)
			{
				RegisteredTextureLookup.Clear();
				for (int i = 0; i < RegisteredTexturesCount; i++)
				{
					Texture texture = RegisteredTextures[i];
					if (texture != null && texture.Origin is PathOrigin pathOrigin)
					{
						if (!RegisteredTextureLookup.TryGetValue(pathOrigin.Path, out List<Texture> list))
						{
							list = new List<Texture>();
							RegisteredTextureLookup[pathOrigin.Path] = list;
						}
						list.Add(texture);
					}
				}
			}

			if (!currentlyReloading)
			{
				// Only force GC on full unload, not on route reload
				GC.Collect(0, GCCollectionMode.Optimized);
			}
			
		}

		/// <summary>Checks whether the on-disk source file of the given texture origin is unchanged.</summary>
		internal static bool TextureFileUnchanged(TextureOrigin origin)
		{
			if (!(origin is PathOrigin pathOrigin))
			{
				return false;
			}
			// Refresh() first, as FileSystemInfo caches size/last write time.
			try
			{
				FileInfo info = new FileInfo(pathOrigin.Path);
				info.Refresh();
				return info.Exists && pathOrigin.FileSize == info.Length && pathOrigin.LastModificationTime == info.LastWriteTime;
			}
			catch
			{
				return false;
			}
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
				int now = CPreciseTimer.GetClockTicks();
				for (int i = 0; i < RegisteredTexturesCount; i++)
				{
					if (RegisteredTextures[i] != null && RegisteredTextures[i].AvailableToUnload && (now - RegisteredTextures[i].LastAccess) > 20000)
					{
						UnloadTexture(ref RegisteredTextures[i]);
					}
				}
				if (StreamingActive)
				{
					long budgetBytes = (long)TexturePolicy.ResolveBudgetMB(StreamingBudgetMB) * 1048576L;
					bool allowUpgrades = GetEstimatedResidentVramBytes() <= budgetBytes;
					UpdateTextureTiers(now, allowUpgrades);
					EnforceTextureBudget();
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

		// --- streaming tiers ---

		/// <summary>Reference-identity comparer for textures (Texture overrides Equals without GetHashCode).</summary>
		private sealed class ReferenceTextureComparer : IEqualityComparer<Texture>
		{
			public bool Equals(Texture a, Texture b)
			{
				return ReferenceEquals(a, b);
			}

			public int GetHashCode(Texture texture)
			{
				return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(texture);
			}
		}

		private static bool AnySlotValid(Texture texture)
		{
			foreach (OpenGlTexture slot in texture.OpenGlTextures)
			{
				if (slot.Valid)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Whether the texture takes part in tier/budget streaming.</summary>
		private static bool IsStreamingResident(Texture texture)
		{
			return texture != null && !texture.MultipleFrames && texture.AvailableToUnload && AnySlotValid(texture);
		}

		/// <summary>Seeds the desired tier for an object's textures when it becomes visible.</summary>
		/// <remarks>Cheap and thread-safe enough for the visibility thread: plain int writes only,
		/// no decoding, no GL calls. Only seeds non-resident textures so it never fights the tier pass.</remarks>
		public void SeedDesiredTier(ObjectState state)
		{
			if (!StreamingActive || state == null || state.Prototype == null || state.Prototype.Mesh == null || state.Prototype.Mesh.Materials == null)
			{
				return;
			}
			if (renderer.CameraTrackFollower == null)
			{
				return;
			}
			double cameraTrackPosition = renderer.CameraTrackFollower.TrackPosition;
			double viewingDistance = renderer.currentOptions.ViewingDistance;
			double distance = TexturePolicy.DistanceToCamera(state.StartingDistance, state.EndingDistance, cameraTrackPosition);
			int tier = TexturePolicy.TierForDistance(distance, viewingDistance);
			foreach (var material in state.Prototype.Mesh.Materials)
			{
				if (material == null)
				{
					continue;
				}
				SeedTextureTier(material.DaytimeTexture, tier);
				SeedTextureTier(material.NighttimeTexture, tier);
			}
		}

		private static void SeedTextureTier(Texture texture, int tier)
		{
			if (texture == null || texture.MultipleFrames)
			{
				return;
			}
			if (TexturePolicy.IsProtected(texture))
			{
				texture.DesiredTier = 0;
				return;
			}
			if (texture.ResidentTier < 0)
			{
				texture.DesiredTier = tier;
			}
		}

		private void ApplyTierChange(int index, int tier, ref int swaps)
		{
			RegisteredTextures[index].DesiredTier = tier;
			UnloadTexture(ref RegisteredTextures[index]);
			swaps++;
		}

		private static void CollectFaceDistances(System.Collections.ObjectModel.ReadOnlyCollection<FaceState> faces, double cameraTrackPosition, Dictionary<Texture, double> distances, HashSet<Texture> overlayTextures)
		{
			if (faces == null)
			{
				return;
			}
			foreach (FaceState faceState in faces)
			{
				if (faceState == null || faceState.Object == null)
				{
					continue;
				}
				double distance = TexturePolicy.DistanceToCamera(faceState.Object.StartingDistance, faceState.Object.EndingDistance, cameraTrackPosition);
				var materials = faceState.Object.Prototype.Mesh.Materials;
				if (faceState.Face.Material < 0 || faceState.Face.Material >= materials.Length)
				{
					continue;
				}
				var material = materials[faceState.Face.Material];
				if (material == null)
				{
					continue;
				}
				AccumulateDistance(distances, material.DaytimeTexture, distance);
				AccumulateDistance(distances, material.NighttimeTexture, distance);
				if (overlayTextures != null)
				{
					if (material.DaytimeTexture != null)
					{
						overlayTextures.Add(material.DaytimeTexture);
					}
					if (material.NighttimeTexture != null)
					{
						overlayTextures.Add(material.NighttimeTexture);
					}
				}
			}
		}

		private static void AccumulateDistance(Dictionary<Texture, double> distances, Texture texture, double distance)
		{
			if (texture == null || texture.MultipleFrames)
			{
				return;
			}
			double current;
			if (!distances.TryGetValue(texture, out current) || distance < current)
			{
				distances[texture] = distance;
			}
		}

		/// <summary>Re-evaluates desired tiers from visible-face distances; unloads mismatches for re-upload.</summary>
		private void UpdateTextureTiers(int now, bool allowUpgrades)
		{
			if (now - lastTierPassTick < TexturePolicy.TierPassIntervalTicks)
			{
				return;
			}
			lastTierPassTick = now;
			if (renderer.CameraTrackFollower == null)
			{
				return;
			}
			double cameraTrackPosition = renderer.CameraTrackFollower.TrackPosition + renderer.Camera.Alignment.Position.Z;
			double viewingDistance = renderer.currentOptions.ViewingDistance;

			var distances = new Dictionary<Texture, double>(new ReferenceTextureComparer());
			var overlayTextures = new HashSet<Texture>(new ReferenceTextureComparer());
			lock (renderer.VisibleObjects.LockObject)
			{
				CollectFaceDistances(renderer.VisibleObjects.OpaqueFaces, cameraTrackPosition, distances, null);
				CollectFaceDistances(renderer.VisibleObjects.AlphaFaces, cameraTrackPosition, distances, null);
				CollectFaceDistances(renderer.VisibleObjects.OverlayOpaqueFaces, cameraTrackPosition, distances, overlayTextures);
				CollectFaceDistances(renderer.VisibleObjects.OverlayAlphaFaces, cameraTrackPosition, distances, overlayTextures);
			}

			int swaps = 0;
			for (int i = 0; i < RegisteredTexturesCount && swaps < TexturePolicy.MaxTierSwapsPerPass; i++)
			{
				Texture handle = RegisteredTextures[i];
				if (!IsStreamingResident(handle))
				{
					continue;
				}
				if (TexturePolicy.IsProtected(handle) || overlayTextures.Contains(handle))
				{
					handle.DesiredTier = 0;
					if (handle.ResidentTier > 0)
					{
						ApplyTierChange(i, 0, ref swaps);
					}
					continue;
				}
				double distance;
				if (!distances.TryGetValue(handle, out distance))
				{
					continue; // not used by visible faces: leave to the time-based LRU
				}
				int desired = TexturePolicy.TierForDistance(distance, viewingDistance);
				if (desired == handle.ResidentTier)
				{
					handle.DesiredTier = desired;
					continue;
				}
				if (desired < handle.ResidentTier)
				{
					if (!allowUpgrades)
					{
						continue; // over budget: hold the smaller tier until memory fits
					}
					ApplyTierChange(i, desired, ref swaps);
				}
				else
				{
					handle.DesiredTier = desired;
					if (now - handle.TierChangeTick > TexturePolicy.DowngradeCooldownTicks)
					{
						ApplyTierChange(i, desired, ref swaps);
					}
				}
			}
		}

		/// <summary>Downgrades then evicts resident textures until the configured budget fits.</summary>
		/// <remarks>Runs on the render thread; residency only decreases here, so the running
		/// total is tracked with exact deltas instead of rescanning per operation.</remarks>
		private void EnforceTextureBudget()
		{
			long budgetBytes = (long)TexturePolicy.ResolveBudgetMB(StreamingBudgetMB) * 1048576L;
			long total = GetEstimatedResidentVramBytes();
			if (total <= budgetBytes)
			{
				return;
			}
			var candidates = new List<KeyValuePair<int, long>>();
			for (int i = 0; i < RegisteredTexturesCount; i++)
			{
				Texture texture = RegisteredTextures[i];
				if (!IsStreamingResident(texture))
				{
					continue;
				}
				if (TexturePolicy.IsProtected(texture) || texture.ResidentTier >= TexturePolicy.MaxTier)
				{
					continue;
				}
				long estimate = EstimateVramBytes(texture);
				if (estimate > 0)
				{
					candidates.Add(new KeyValuePair<int, long>(i, estimate));
				}
			}
			candidates.Sort((a, b) => b.Value.CompareTo(a.Value));
			int ops = 0;
			foreach (KeyValuePair<int, long> candidate in candidates)
			{
				if (ops >= TexturePolicy.MaxTierSwapsPerPass || total <= budgetBytes)
				{
					break;
				}
				Texture texture = RegisteredTextures[candidate.Key];
				if (!IsStreamingResident(texture))
				{
					continue;
				}
				int resident = texture.ResidentTier < 0 ? 0 : texture.ResidentTier;
				texture.DesiredTier = Math.Min(TexturePolicy.MaxTier, resident + 1);
				total -= candidate.Value * 3 / 4; // one tier up quarters the pixels
				UnloadTexture(ref RegisteredTextures[candidate.Key]);
				ops++;
			}
			while (ops < TexturePolicy.MaxTierSwapsPerPass && total > budgetBytes)
			{
				int oldest = -1;
				for (int i = 0; i < RegisteredTexturesCount; i++)
				{
					Texture texture = RegisteredTextures[i];
					if (!IsStreamingResident(texture))
					{
						continue;
					}
					if (oldest < 0 || texture.LastAccess < RegisteredTextures[oldest].LastAccess)
					{
						oldest = i;
					}
				}
				if (oldest < 0)
				{
					break;
				}
				total -= EstimateVramBytes(RegisteredTextures[oldest]);
				UnloadTexture(ref RegisteredTextures[oldest]);
				ops++;
			}
		}


		// --- statistics ---

		/// <summary>Gets the number of registered textures.</summary>
		/// <returns>The number of registered textures.</returns>
		public int GetNumberOfRegisteredTextures()
		{
			return RegisteredTexturesCount;
		}

		/// <summary>Gets the number of loaded textures.</summary>
		/// <returns>The number of loaded textures.</returns>
		public int GetNumberOfLoadedTextures()
		{
			int count = 0;

			for (int i = 0; i < RegisteredTexturesCount; i++)
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
			for (int i = 0; i < RegisteredTexturesCount; i++)
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

		/// <summary>Estimates the VRAM footprint of a single texture in bytes, including the mipmap chain.</summary>
		/// <remarks>Matches the uncompressed upload path (Rgb8/Rgba8/R8 + GenerateMipmap).
		/// Returns 0 when no GL slot is currently resident.</remarks>
		public static long EstimateVramBytes(Texture texture)
		{
			if (texture == null || texture.Width <= 0 || texture.Height <= 0)
			{
				return 0;
			}
			bool resident = false;
			foreach (OpenGlTexture t in texture.OpenGlTextures)
			{
				if (t.Valid)
				{
					resident = true;
					break;
				}
			}
			if (!resident)
			{
				return 0;
			}
			int bytesPerPixel;
			switch (texture.PixelFormat)
			{
				case PixelFormat.Grayscale:
				case PixelFormat.Paletted:
					// Grayscale uploads via R8; paletted opaque expands to RGB (transparent to RGBA)
					bytesPerPixel = texture.Transparency == TextureTransparencyType.Opaque ? (texture.PixelFormat == PixelFormat.Grayscale ? 1 : 3) : 4;
					break;
				case PixelFormat.GrayscaleAlpha:
					bytesPerPixel = 4; // upconverted to RGBA on upload
					break;
				case PixelFormat.RGB:
					bytesPerPixel = texture.Transparency == TextureTransparencyType.Opaque ? 3 : 4;
					break;
				default:
					bytesPerPixel = 4;
					break;
			}
			int residentWidth, residentHeight;
			TexturePolicy.TierDimensions(texture.Width, texture.Height, texture.ResidentTier < 0 ? 0 : texture.ResidentTier, out residentWidth, out residentHeight);
			if (texture.ResidentCompressed && texture.ResidentBlockBytes > 0)
			{
				// Block-compressed upload: 4x4 texel blocks per mip level.
				long compressedTotal = 0;
				int levelWidth = residentWidth;
				int levelHeight = residentHeight;
				for (int level = 0; level < 16; level++)
				{
					compressedTotal += (long)((levelWidth + 3) / 4) * ((levelHeight + 3) / 4) * texture.ResidentBlockBytes;
					if (levelWidth == 1 && levelHeight == 1)
					{
						break;
					}
					levelWidth = Math.Max(1, levelWidth / 2);
					levelHeight = Math.Max(1, levelHeight / 2);
				}
				return compressedTotal;
			}
			long level0 = (long)residentWidth * residentHeight * bytesPerPixel;
			return level0 + level0 / 3; // mipmap chain ≈ 4/3
		}

		/// <summary>Estimates the total resident VRAM footprint of all registered textures in bytes.</summary>
		public long GetEstimatedResidentVramBytes()
		{
			long total = 0;
			for (int i = 0; i < RegisteredTexturesCount; i++)
			{
				total += EstimateVramBytes(RegisteredTextures[i]);
			}
			return total;
		}


		/// <summary>Gets the next free texture, resizing the base textures array if appropriate</summary>
		/// <returns>The index of the next free texture</returns>
		public int GetNextFreeTexture()
		{
			if (RegisteredTextures.Length == RegisteredTexturesCount)
			{
				Array.Resize(ref RegisteredTextures, RegisteredTextures.Length << 1);
			}
			else if (RegisteredTexturesCount > RegisteredTextures.Length)
			{
				/* BUG:
				 * The registered textures count very occasional becomes greater than the array length (Texture loader crashes possibly?)
				 * This then crashes when we attempt to itinerate the array, so reset it...
				 */
				RegisteredTexturesCount = RegisteredTextures.Length;
				Array.Resize(ref RegisteredTextures, RegisteredTextures.Length << 1);
			}

			return RegisteredTexturesCount;
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
