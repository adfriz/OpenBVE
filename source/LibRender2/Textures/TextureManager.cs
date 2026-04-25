using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using LibRender2.Screens;
using OpenBveApi;
using OpenBveApi.Hosts;
using OpenBveApi.Textures;
using Raylib_cs;
using InterpolationMode = OpenBveApi.Graphics.InterpolationMode;
using PixelFormat = OpenBveApi.Textures.PixelFormat;

namespace LibRender2.Textures
{
	public class TextureManager
	{
		private readonly HostInterface currentHost;
		private readonly BaseRenderer renderer;
		public static Texture[] RegisteredTextures;
		internal static Dictionary<TextureOrigin, Texture> textureCache = new Dictionary<TextureOrigin, Texture>();
		private static Dictionary<TextureOrigin, Texture> animatedTextures;
		public int RegisteredTexturesCount;

		internal TextureManager(HostInterface CurrentHost, BaseRenderer Renderer)
		{
			currentHost = CurrentHost;
			RegisteredTextures = new Texture[16];
			RegisteredTexturesCount = 0;
			renderer = Renderer;
			animatedTextures = new Dictionary<TextureOrigin, Texture>();
		}

		public bool RegisterTexture(string path, out Texture handle)
		{
			return RegisterTexture(path, null, out handle);
		}

		public bool RegisterTexture(string path, TextureParameters parameters, out Texture handle)
		{
			if (!File.Exists(path))
			{
				handle = null;
				return false;
			}
			
			for (int i = 0; i < RegisteredTexturesCount; i++)
			{
				if (RegisteredTextures[i] != null)
				{
					PathOrigin source = RegisteredTextures[i].Origin as PathOrigin;
					if (source != null && source.Path.Equals(path, StringComparison.InvariantCultureIgnoreCase) && source.Parameters == parameters)
					{
						handle = RegisteredTextures[i];
						return true;
					}
				}
			}

			int idx = GetNextFreeTexture();
			RegisteredTextures[idx] = new Texture(path, parameters, currentHost);
			RegisteredTexturesCount++;
			handle = RegisteredTextures[idx];
			return true;
		}

		public bool LoadTexture(ref Texture handle, OpenGlTextureWrapMode wrap, int currentTicks, InterpolationMode Interpolation, int AnisotropicFilteringLevel)
		{
			if (handle == null || handle.OpenGlTextures == null) return false;

			if (handle.MultipleFrames)
			{
				// Handle animated textures...
				if (!animatedTextures.TryGetValue(handle.Origin, out Texture texture))
				{
					if (!handle.Origin.GetTexture(out texture)) return false;
					animatedTextures.Add(handle.Origin, texture);
				}
				// Frame switching logic...
				handle = texture;
			}

			if (handle.OpenGlTextures[(int)wrap].Valid) return true;
			if (handle.Ignore) return false;

			if (handle.Origin.GetTexture(out Texture tex))
			{
				Image img = new Image();
				img.Width = tex.Width;
				img.Height = tex.Height;
				img.Format = PixelFormatToRaylib(tex.PixelFormat);
				img.Mipmaps = 1;
				
				unsafe {
					fixed (byte* ptr = tex.Bytes) {
						img.Data = ptr;
						handle.OpenGlTextures[(int)wrap].RaylibTexture = Raylib.LoadTextureFromImage(img);
					}
				}
				
				handle.OpenGlTextures[(int)wrap].Name = handle.OpenGlTextures[(int)wrap].RaylibTexture.Id;
				handle.OpenGlTextures[(int)wrap].Valid = true;

				// Apply Wrap Mode
				TextureFilter filter = InterpolationToRaylib(Interpolation);
				Raylib.SetTextureFilter(handle.OpenGlTextures[(int)wrap].RaylibTexture, filter);
				
				TextureWrap rayWrap = WrapModeToRaylib(wrap);
				Raylib.SetTextureWrap(handle.OpenGlTextures[(int)wrap].RaylibTexture, rayWrap);

				return true;
			}

			handle.Ignore = true;
			return false;
		}

		private Raylib_cs.PixelFormat PixelFormatToRaylib(PixelFormat format)
		{
			switch (format)
			{
				case PixelFormat.RGB: return Raylib_cs.PixelFormat.PIXELFORMAT_UNCOMPRESSED_R8G8B8;
				case PixelFormat.RGBAlpha: return Raylib_cs.PixelFormat.PIXELFORMAT_UNCOMPRESSED_R8G8B8A8;
				case PixelFormat.Grayscale: return Raylib_cs.PixelFormat.PIXELFORMAT_UNCOMPRESSED_GRAYSCALE;
				default: return Raylib_cs.PixelFormat.PIXELFORMAT_UNCOMPRESSED_R8G8B8A8;
			}
		}

		private TextureFilter InterpolationToRaylib(InterpolationMode mode)
		{
			switch (mode)
			{
				case InterpolationMode.NearestNeighbor: return TextureFilter.TEXTURE_FILTER_POINT;
				case InterpolationMode.Bilinear: return TextureFilter.TEXTURE_FILTER_BILINEAR;
				case InterpolationMode.TrilinearMipmapped: return TextureFilter.TEXTURE_FILTER_TRILINEAR;
				case InterpolationMode.AnisotropicFiltering: return TextureFilter.TEXTURE_FILTER_ANISOTROPIC_16X;
				default: return TextureFilter.TEXTURE_FILTER_BILINEAR;
			}
		}

		private TextureWrap WrapModeToRaylib(OpenGlTextureWrapMode wrap)
		{
			if ((wrap & OpenGlTextureWrapMode.RepeatClamp) != 0) return TextureWrap.TEXTURE_WRAP_REPEAT;
			return TextureWrap.TEXTURE_WRAP_CLAMP;
		}

		public static void UnloadTexture(ref Texture handle)
		{
			if (handle == null) return;
			foreach (var t in handle.OpenGlTextures)
			{
				if (t.Valid)
				{
					Raylib.UnloadTexture(t.RaylibTexture);
					t.Valid = false;
				}
			}
		}

		public int GetNextFreeTexture()
		{
			if (RegisteredTextures.Length == RegisteredTexturesCount)
			{
				Array.Resize(ref RegisteredTextures, RegisteredTextures.Length << 1);
			}
			return RegisteredTexturesCount;
		}
	}
}
