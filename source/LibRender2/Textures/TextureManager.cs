using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using LibRender2.Screens;
using OpenBveApi;
using OpenBveApi.Hosts;
using OpenBveApi.Textures;
using OpenTK.Graphics.OpenGL;
using InterpolationMode = OpenBveApi.Graphics.InterpolationMode;
using PixelFormat = OpenBveApi.Textures.PixelFormat;

namespace LibRender2.Textures
{
	/// <summary>Handles texture registration, upload, and cleanup.</summary>
	public class TextureManager
	{
		private readonly HostInterface currentHost;

		private readonly BaseRenderer renderer;

		/// <summary>All registered textures.</summary>
		public static Texture[] RegisteredTextures;
		/// <summary>Decoded textures, keyed by origin.</summary>
		internal static Dictionary<TextureOrigin, Texture> textureCache = new Dictionary<TextureOrigin, Texture>();

		/// <summary>Total ms spent decoding texture files.</summary>
		public static long TextureDecodeTime;

		/// <summary>Texture upload requests handled.</summary>
		public static long UploadCount;

		/// <summary>Ms spent in upload requests, including cache hits.</summary>
		public static long UploadMs;

		private static Dictionary<TextureOrigin, Texture> animatedTextures;
		// Reused buffer for paletted GIF frames (avoids a per-frame allocation).
		private static byte[] _palettedExpandBuffer;
		private static readonly object _expandLock = new object();

		/// <summary>Path-based textures, indexed by path.</summary>
		private static readonly Dictionary<string, List<Texture>> RegisteredTextureLookup = new Dictionary<string, List<Texture>>(StringComparer.OrdinalIgnoreCase);

		private static readonly object TextureLookupLock = new object();

		/// <summary>One lock per path hash, so the same file decodes once.</summary>
		private static readonly object[] PathRegisterStripes = CreateStripes();

		private static object[] CreateStripes()
		{
			object[] stripes = new object[256];
			for (int i = 0; i < stripes.Length; i++)
			{
				stripes[i] = new object();
			}
			return stripes;
		}

		internal static bool TryGetCachedTexture(TextureOrigin origin, out Texture cached)
		{
			lock (TextureLookupLock)
			{
				return textureCache.TryGetValue(origin, out cached);
			}
		}

		internal static void StoreCachedTexture(TextureOrigin origin, Texture decoded)
		{
			if (origin == null || decoded == null) return;
			lock (TextureLookupLock)
			{
				textureCache[origin] = decoded;
			}
		}

		/// <summary>The number of currently registered textures.</summary>
		public int RegisteredTexturesCount;

		internal TextureManager(HostInterface CurrentHost, BaseRenderer Renderer)
		{
			currentHost = CurrentHost;
			RegisteredTextures = new Texture[16];
			RegisteredTexturesCount = 0;
			renderer = Renderer;
			animatedTextures = new Dictionary<TextureOrigin, Texture>();
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

		/// <summary>Registers a texture from disk.</summary>
		/// <param name="path">The texture file.</param>
		/// <param name="parameters">How to process it.</param>
		/// <param name="handle">The registered handle.</param>
		public bool RegisterTexture(string path, TextureParameters parameters, out Texture handle)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				// shouldn't happen, but stay graceful
				handle = null;
				return false;
			}

			// Same file, one decode: co-workers wait on this stripe,
			// other files still decode in parallel.
			object stripe = PathRegisterStripes[(uint)path.ToLowerInvariant().GetHashCode() % (uint)PathRegisterStripes.Length];
			lock (stripe)
			{
				if (TryFindRegisteredTexture(path, parameters, out handle))
				{
					return true;
				}

				// CPU-only decode, no GL here yet.
				Texture decoded = new Texture(path, parameters, currentHost);

				// A differently-cased path may have beaten us on another stripe.
				if (TryFindRegisteredTexture(path, parameters, out handle))
				{
					return true;
				}

				if (RegisteredTexturesCount > RegisteredTextures.Length)
				{
					RegisteredTexturesCount = RegisteredTextures.Length;
				}

				int idx = GetNextFreeTexture();
				RegisteredTextures[idx] = decoded;
				RegisteredTexturesCount++;
				handle = RegisteredTextures[idx];

				// Cache the decoded bytes, not the empty handle.
				if (handle.Origin != null && handle.PixelFormat != PixelFormat.Invalid && handle.DecodedTexture != null && !textureCache.ContainsKey(handle.Origin))
				{
					textureCache.Add(handle.Origin, handle.DecodedTexture);
				}

				if (!RegisteredTextureLookup.TryGetValue(path, out List<Texture> list))
				{
					list = new List<Texture>();
					RegisteredTextureLookup[path] = list;
				}
				list.Add(handle);
			}
			return true;
		}

		/// <summary>Finds an already-registered texture. Call with the path stripe held.</summary>
		private static bool TryFindRegisteredTexture(string path, TextureParameters parameters, out Texture handle)
		{
			lock (TextureLookupLock)
			{
				if (RegisteredTextureLookup.TryGetValue(path, out List<Texture> candidates))
				{
					for (int i = 0; i < candidates.Count; i++)
					{
						if (candidates[i].Origin is PathOrigin source && source.Parameters == parameters)
						{
							handle = candidates[i];
							return true;
						}
					}
				}
			}
			handle = null;
			return false;
		}

		/// <summary>Registers decoded texture data.</summary>
		public Texture RegisterTexture(Texture texture) => RegisterCore(() => new Texture(texture));

		/// <summary>Registers a bitmap. Don't dispose it afterwards.</summary>
		public Texture RegisterTexture(Bitmap bitmap, TextureParameters parameters) => RegisterCore(() => new Texture(bitmap, parameters));

		/// <summary>Registers a bitmap. Don't dispose it afterwards.</summary>
		public Texture RegisterTexture(Bitmap bitmap) => RegisterCore(() => new Texture(bitmap));

		// Shared path for the overloads above. Lock: the index allocator is shared.
		private Texture RegisterCore(Func<Texture> factory)
		{
			lock (TextureLookupLock)
			{
				int idx = GetNextFreeTexture();
				RegisteredTextures[idx] = factory();
				RegisteredTexturesCount++;
				return RegisteredTextures[idx];
			}
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
			Stopwatch uploadTimer = Stopwatch.StartNew();
			bool result = LoadTextureInternal(ref handle, wrap, currentTicks, Interpolation, AnisotropicFilteringLevel);
			UploadCount++;
			UploadMs += uploadTimer.ElapsedMilliseconds;
			return result;
		}

		private bool LoadTextureInternal(ref Texture handle, OpenGlTextureWrapMode wrap, int currentTicks, InterpolationMode Interpolation, int AnisotropicFilteringLevel)
		{

			Texture texture = null;
			//Don't try to load a texture to a null handle, this is a seriously bad idea....
			if (handle == null || handle.OpenGlTextures == null)
			{
				return false;
			}
			
			if (handle.MultipleFrames)
			{
			bool animatedHit = false;
			if (handle.Origin != null)
			{
				lock (TextureLookupLock)
				{
					animatedHit = animatedTextures.TryGetValue(handle.Origin, out texture) && texture != null;
				}
			}
			if (!animatedHit)
			{
				texture = null;
				// Reuse register-time decode from textureCache where possible to avoid decoding the same large animated GIF twice (2× memory). See RegisterTexture pre-seed at line 136.
				// NOTE: textureCache may contain a null value cached by an older ObjectLibrary path (failed GetTexture); treat as a miss and purge it.
				if (handle.Origin != null)
				{
					lock (TextureLookupLock)
					{
						if (textureCache.TryGetValue(handle.Origin, out Texture cachedTexture))
						{
							if (cachedTexture == null)
							{
								textureCache.Remove(handle.Origin);
							}
							else if (cachedTexture.MultipleFrames)
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
					if (handle.Origin == null || !handle.Origin.GetTexture(out texture))
					{
						//Loading animated texture barfed
						return false;
					}
				}
				if (handle.Origin != null && texture != null)
				{
					lock (TextureLookupLock)
					{
						animatedTextures[handle.Origin] = texture;
					}
				}
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
					if (handle.Origin != null && textureCache.TryGetValue(handle.Origin, out Texture cachedTexture))
					{
						if (cachedTexture == null)
						{
							// Purged poisoned entry cached by a failed decode (e.g. missing texture on BVE5/BVE6 .txt route)
							textureCache.Remove(handle.Origin);
						}
						// The cache value is the DecodedTexture (ByteArrayOrigin) created at registration – its Origin is not PathOrigin,
						// so the original check (cachedPathOrigin != null) never succeeds for decoded GIFs and caused a second decode (2× memory).
						// Reuse if the cached entry is animated (GIF video) or its ByteArrayOrigin, and parameters match (or both null).
						else if (cachedTexture.MultipleFrames)
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
				lock (TextureLookupLock)
				{
					if (animatedTextures.TryGetValue(handle.Origin, out Texture animatedTexture) && animatedTexture != null && animatedTexture.MultipleFrames)
					{
						texture = animatedTexture;
					}
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
				if (handle.Origin == null)
				{
					handle.Ignore = true;
					return false;
				}
				handle.Origin.GetTexture(out texture);
			}
			}
			if (texture != null)
			{
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
									PixelInternalFormat.Rgb8,
									texture.Width, texture.Height, 0,
									OpenTK.Graphics.OpenGL.PixelFormat.Rgb,
									PixelType.UnsignedByte, textureBytes);
								break;
							case PixelFormat.RGBAlpha:
								/*
								 * Opaque texture, so the alpha channel is discarded by the RGB8 internal format.
								 * Upload the RGBA data directly rather than stripping it CPU-side.
								 */
								GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
								GL.TexImage2D(TextureTarget.Texture2D, 0,
									PixelInternalFormat.Rgb8,
									texture.Width, texture.Height, 0,
									OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
									PixelType.UnsignedByte, textureBytes);
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
									PixelInternalFormat.Rgba8,
									texture.Width, texture.Height, 0,
									OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
									PixelType.UnsignedByte, textureBytes);
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
					if (texture.MultipleFrames)
					{
						texture.OpenGlTextures[(int)wrap].Valid = true;
						// Cache the decode under the stable handle origin (upsert: the animated
						// block above may already have added it) so later ticks hit the cache
						// instead of re-decoding after a reload cleared everything.
						if (handle.Origin != null)
						{
							lock (TextureLookupLock)
							{
								animatedTextures[handle.Origin] = texture;
							}
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
							if (handle.Origin != null && textureCache.TryGetValue(handle.Origin, out cachedTexture))
							{
								if (cachedTexture == null)
								{
									textureCache.Remove(handle.Origin);
									cachedTexture = null;
								}
								else
								{
									// Compute the transparency type whilst the data is still available,
									// as otherwise a later query would have to re-decode the file from disk
									cachedTexture.GetTransparencyType();
									cachedTexture.ReleaseBytes();
								}
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

		/// <summary>Unloads a texture from OpenGL.</summary>
		/// <param name="preserveUnchangedCache">On reload, keep cache entries whose file didn't change.</param>
		/// <param name="releaseBytes">On route switch, also drop decoded bytes (re-decodes on demand).</param>
		public static void UnloadTexture(ref Texture handle, bool preserveUnchangedCache = false, bool releaseBytes = false)
		{
			// A bad handle makes OpenGL unhappy, so bail early.
			if (handle == null)
			{
				return;
			}

			if (handle.MultipleFrames)
			{
			// One pass per frame slot (at least one, even if TotalFrames is 0).
			int passes = handle.TotalFrames > 0 ? handle.TotalFrames : 1;
			for (int i = 0; i < passes; i++)
			{
				handle.CurrentFrame = i;
				DeleteGlTextures(handle);
			}
			// Re-create the handle so it can reload from disk later.
			var texture = handle;
			// Keep the origin: without it animated GIFs come back hollow after reload.
			handle = new Texture(texture.Origin);
			}
			else
			{
				DeleteGlTextures(handle);
			}
			handle.Ignore = false;
			if (handle.Origin != null)
			{
				// Unchanged files stay cached, so the reloaded scene reuses the decode.
				if (!preserveUnchangedCache || !TextureFileUnchanged(handle.Origin))
				{
					lock (TextureLookupLock)
					{
						textureCache.Remove(handle.Origin);
					}
				}
				if (releaseBytes)
				{
					// Drop the handle's own pixel bytes; it re-decodes when needed.
					handle = new Texture(handle.Origin);
				}
			}
		}

		private static void DeleteGlTextures(Texture handle)
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

		/// <summary>Loads all registered textures.</summary>
		public void LoadAllTextures()
		{
			lock (TextureLookupLock)
			{
				for (int i = 0; i < RegisteredTexturesCount && i < RegisteredTextures.Length; i++)
				{
					for (int j = 0; j < 4; j++)
					{
						if (RegisteredTextures[i] != null && RegisteredTextures[i].OpenGlTextures[j].Used)
						{
							LoadTexture(ref RegisteredTextures[i], (OpenGlTextureWrapMode)j, CPreciseTimer.GetClockTicks(), renderer.currentOptions.Interpolation, renderer.currentOptions.AnisotropicFilteringLevel);
						}

					}

				}
			}
		}

		/// <summary>Unloads all registered textures.</summary>
		/// <param name="currentlyReloading">When true, textures whose source file is unchanged are preserved.</param>
		/// <param name="releaseBytes">When true, decoded pixel bytes are also released (route switch).</param>
		public void UnloadAllTextures(bool currentlyReloading, bool releaseBytes = false)
		{
			// Always clear animated texture cache to prevent memory leak on reload:
			// animatedTextures retains decoded frame data for every GIF ever loaded,
			// doubling memory on each reload because old entries are never removed.
			lock (TextureLookupLock)
			{
				animatedTextures.Clear();
			}

			lock (TextureLookupLock)
			{
				for (int i = 0; i < RegisteredTexturesCount && i < RegisteredTextures.Length; i++)
				{
					/*
					 * On a route reload, preserve textures whose source file is unchanged,
					 * so that the first frame after the reload does not re-upload every texture.
					 */
				if (currentlyReloading && RegisteredTextures[i] != null && !RegisteredTextures[i].MultipleFrames && TextureFileUnchanged(RegisteredTextures[i].Origin))
				{
					continue;
				}
				UnloadTexture(ref RegisteredTextures[i], currentlyReloading, releaseBytes);
				}
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

			// Drop stale handles from the lookup so it only points at survivors.
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
				// Full unload only; reloads don't need the GC hit.
				GC.Collect(0, GCCollectionMode.Optimized);
			}
			
		}

		/// <summary>True when the file behind this texture hasn't changed on disk.</summary>
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
			// Breakpoint in VS stalls the frame: touch everything so nothing unloads by mistake.
			if (TimeElapsed > 1000)
			{
				TouchAllTextures();
			}
#endif
			if (renderer.CurrentInterface == InterfaceType.Normal)
			{
				lock (TextureLookupLock)
				{
					for (int i = 0; i < RegisteredTextures.Length; i++)
					{
						if (RegisteredTextures[i] != null && RegisteredTextures[i].AvailableToUnload && (CPreciseTimer.GetClockTicks() - RegisteredTextures[i].LastAccess) > 20000)
						{
							UnloadTexture(ref RegisteredTextures[i]);
						}
					}
				}
			}
			else
			{
				// Paused or in a menu: textures may be needed right after unpause, so just touch them.
				lock (TextureLookupLock)
				{
					TouchAllTextures();
				}
			}
		}

		// Marks every texture as just-used. Call with TextureLookupLock held, except DEBUG callers.
		private static void TouchAllTextures()
		{
			int now = CPreciseTimer.GetClockTicks();
			foreach (Texture texture in RegisteredTextures)
			{
				if (texture != null)
				{
					texture.LastAccess = now;
				}
			}
		}


		// --- statistics ---

		/// <summary>How many textures are registered.</summary>
		public int GetNumberOfRegisteredTextures()
		{
			lock (TextureLookupLock)
			{
				return RegisteredTexturesCount;
			}
		}

		/// <summary>How many static textures are on the GPU.</summary>
		public int GetNumberOfLoadedTextures() => CountWhere(t => !t.MultipleFrames);

		/// <summary>How many animated textures are on the GPU.</summary>
		public int GetNumberOfLoadedAnimatedTextures() => CountWhere(t => t.MultipleFrames);

		private int CountWhere(Func<Texture, bool> predicate)
		{
			int count = 0;
			Texture[] snapshot = GetRegisteredSnapshot(out int registered);
			for (int i = 0; i < registered && i < snapshot.Length; i++)
			{
				if (snapshot[i] == null || !predicate(snapshot[i]))
				{
					continue;
				}

				if (snapshot[i].OpenGlTextures.Any(t => t.Valid))
				{
					count++;
				}
			}
			return count;
		}


		/// <summary>Next free slot, growing the array when full.</summary>
		private int GetNextFreeTexture()
		{
			// Caller must hold TextureLookupLock.
			if (RegisteredTextures.Length == RegisteredTexturesCount)
			{
				Array.Resize(ref RegisteredTextures, RegisteredTextures.Length << 1);
			}
			else if (RegisteredTexturesCount > RegisteredTextures.Length)
			{
				RegisteredTexturesCount = RegisteredTextures.Length;
				Array.Resize(ref RegisteredTextures, RegisteredTextures.Length << 1);
			}

			return RegisteredTexturesCount;
		}

		private Texture[] GetRegisteredSnapshot(out int count)
		{
			lock (TextureLookupLock)
			{
				count = RegisteredTexturesCount;
				Texture[] copy = new Texture[RegisteredTextures.Length];
				Array.Copy(RegisteredTextures, copy, RegisteredTextures.Length);
				return copy;
			}
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
