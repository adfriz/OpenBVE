using System;
using OpenBveApi.Colors;
using OpenBveApi.Textures;

namespace LibRender2.Textures
{
	/// <summary>CPU downscaling for distance-tiered uploads (no external dependencies).</summary>
	/// <remarks>Working formats mirror the uploader assumptions in TextureManager so the
	/// VRAM estimator must stays accurate: grayscale stays 1Bpp, opaque RGB-family stays 3Bpp,
	/// anything with alpha becomes RGBA 4Bpp.</remarks>
	public static class TextureDownscale
	{
		/// <summary>Builds an upload-sized copy of the texture, or returns the source.</summary>
		/// <param name="source">The full-size decoded texture.</param>
		/// <param name="fullBytes">Its pixel bytes (already fetched by the caller).</param>
		/// <param name="tier">Desired tier (0 returns the source).</param>
		/// <param name="opaque">Transparency result computed from the full texture.</param>
		public static Texture CreateTierTexture(Texture source, byte[] fullBytes, int tier, bool opaque)
		{
			if (tier <= 0 || source == null || fullBytes == null || fullBytes.Length == 0)
			{
				return source;
			}
			int tierWidth, tierHeight;
			TexturePolicy.TierDimensions(source.Width, source.Height, tier, out tierWidth, out tierHeight);
			if (tierWidth >= source.Width && tierHeight >= source.Height)
			{
				return source;
			}
			PixelFormat workFormat;
			byte[] working = ToWorkingBuffer(source, fullBytes, opaque, out workFormat);
			if (working == null)
			{
				return source;
			}
			int bpp = workFormat.BytesPerPixel();
			if (bpp <= 0 || working.Length < source.Width * source.Height * bpp)
			{
				return source;
			}
			byte[] small = BoxDownscale(working, source.Width, source.Height, bpp, tierWidth, tierHeight);
			return new Texture(tierWidth, tierHeight, workFormat, small, (Color24[])null);
		}

		private static byte[] ToWorkingBuffer(Texture source, byte[] srcBytes, bool opaque, out PixelFormat workFormat)
		{
			switch (source.PixelFormat)
			{
				case PixelFormat.Grayscale:
					workFormat = PixelFormat.Grayscale;
					return srcBytes;
				case PixelFormat.RGB:
					if (opaque)
					{
						workFormat = PixelFormat.RGB;
						return srcBytes;
					}
					break;
				case PixelFormat.RGBAlpha:
					workFormat = PixelFormat.RGBAlpha;
					return srcBytes;
				case PixelFormat.Paletted:
					if (source.Palette32 == null)
					{
						break; // cannot expand without a palette: upload full
					}
					if (opaque)
					{
						workFormat = PixelFormat.RGB;
						return ExpandPaletted(source, srcBytes, false);
					}
					break; // transparent paletted expands to RGBA below
				default:
					break;
			}
			// Alpha-carrying fallback: expand to RGBA.
			workFormat = PixelFormat.RGBAlpha;
			return ExpandToRGBA(source, srcBytes);
		}

		private static byte[] ExpandPaletted(Texture source, byte[] srcBytes, bool withAlpha)
		{
			int pixels = source.Width * source.Height;
			if (srcBytes.Length < pixels || source.Palette32 == null)
			{
				return null;
			}
			Color32[] palette = source.Palette32;
			if (withAlpha)
			{
				byte[] rgba = new byte[pixels * 4];
				for (int p = 0; p < pixels; p++)
				{
					int idx = srcBytes[p] & 0xFF;
					Color32 c = idx < palette.Length ? palette[idx] : new Color32(0, 0, 0, 255);
					rgba[p * 4] = c.R;
					rgba[p * 4 + 1] = c.G;
					rgba[p * 4 + 2] = c.B;
					rgba[p * 4 + 3] = c.A;
				}
				return rgba;
			}
			byte[] rgb = new byte[pixels * 3];
			for (int p = 0; p < pixels; p++)
			{
				int idx = srcBytes[p] & 0xFF;
				Color32 c = idx < palette.Length ? palette[idx] : new Color32(0, 0, 0, 255);
				rgb[p * 3] = c.R;
				rgb[p * 3 + 1] = c.G;
				rgb[p * 3 + 2] = c.B;
			}
			return rgb;
		}

		private static byte[] ExpandToRGBA(Texture source, byte[] srcBytes)
		{
			int pixels = source.Width * source.Height;
			switch (source.PixelFormat)
			{
				case PixelFormat.GrayscaleAlpha:
					if (srcBytes.Length < pixels * 2)
					{
						return null;
					}
					byte[] ga = new byte[pixels * 4];
					for (int p = 0; p < pixels; p++)
					{
						ga[p * 4] = srcBytes[p * 2];
						ga[p * 4 + 1] = srcBytes[p * 2];
						ga[p * 4 + 2] = srcBytes[p * 2];
						ga[p * 4 + 3] = srcBytes[p * 2 + 1];
					}
					return ga;
				case PixelFormat.RGB:
					// Non-opaque RGB (defensive; the uploader treats RGB as opaque).
					if (srcBytes.Length < pixels * 3)
					{
						return null;
					}
					byte[] rgb = new byte[pixels * 4];
					for (int p = 0; p < pixels; p++)
					{
						rgb[p * 4] = srcBytes[p * 3];
						rgb[p * 4 + 1] = srcBytes[p * 3 + 1];
						rgb[p * 4 + 2] = srcBytes[p * 3 + 2];
						rgb[p * 4 + 3] = 255;
					}
					return rgb;
				case PixelFormat.Paletted:
					return ExpandPaletted(source, srcBytes, true);
				default:
					return null;
			}
		}

		/// <summary>Area-average box filter.</summary>
		public static byte[] BoxDownscale(byte[] src, int srcWidth, int srcHeight, int bytesPerPixel, int dstWidth, int dstHeight)
		{
			byte[] dst = new byte[dstWidth * dstHeight * bytesPerPixel];
			double xRatio = (double)srcWidth / dstWidth;
			double yRatio = (double)srcHeight / dstHeight;
			for (int y = 0; y < dstHeight; y++)
			{
				int y0 = (int)(y * yRatio);
				int y1 = Math.Max(y0 + 1, (int)((y + 1) * yRatio));
				if (y1 > srcHeight)
				{
					y1 = srcHeight;
				}
				for (int x = 0; x < dstWidth; x++)
				{
					int x0 = (int)(x * xRatio);
					int x1 = Math.Max(x0 + 1, (int)((x + 1) * xRatio));
					if (x1 > srcWidth)
					{
						x1 = srcWidth;
					}
					for (int c = 0; c < bytesPerPixel; c++)
					{
						int sum = 0;
						int count = 0;
						for (int sy = y0; sy < y1; sy++)
						{
							for (int sx = x0; sx < x1; sx++)
							{
								sum += src[(sy * srcWidth + sx) * bytesPerPixel + c];
								count++;
							}
						}
						dst[(y * dstWidth + x) * bytesPerPixel + c] = (byte)(sum / Math.Max(1, count));
					}
				}
			}
			return dst;
		}
	}
}
