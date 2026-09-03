using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using OpenBveApi.Textures;
using OpenBveApi.Hosts;
using OpenBveApi.Math;
using Plugin.BMP;
using Plugin.GIF;
using Plugin.PNG;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace Plugin {
	public partial class Plugin {
		
		/// <summary>Loads a texture from the specified file.</summary>
		/// <param name="file">The file that holds the texture.</param>
		/// <param name="texture">Receives the texture.</param>
		/// <returns>Whether loading the texture was successful.</returns>
		private bool Parse(string file, out Texture texture) 
		{
			/*
			 * First, check the header bytes for a known signature,
			 * then pass the file to the matching dedicated decoder.
			 */
			try
			{
				bool gifHeader = false;
				bool bmpHeader = false;
				bool pngHeader = false;
				using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read))
				{
					byte[] buffer = new byte[6];
					if (fs.Length > buffer.Length)
					{
						// ReSharper disable once MustUseReturnValue
						fs.Read(buffer, 0, buffer.Length);
					}
					gifHeader = buffer.SequenceEqual(GifDecoder.GIF87Header) || buffer.SequenceEqual(GifDecoder.GIF89Header);
					bmpHeader = Encoding.ASCII.GetString(buffer, 0, 2) == "BM";
					pngHeader = Encoding.ASCII.GetString(buffer, 1, 3) == "PNG";
				}

				if (gifHeader)
				{
					using (GifDecoder decoder = new GifDecoder())
					{
						decoder.Read(file);
						int frameCount = decoder.GetFrameCount();
						if (frameCount > 0)
						{
							Vector2 frameSize = decoder.GetFrameSize();
							int duration = 0;
							for (int i = 0; i < frameCount; i++) duration += decoder.GetDuration(i);
							double interval = frameCount > 0 ? ((double)duration / frameCount) / 10000000.0 : 0;
						if (frameCount >= 1)
						{
							var palette = decoder.GetPalette();
							// Only use the paletted fast path when EVERY frame decoded as indexed:
							// a mid-stream palette merge failure (>256 total colors) leaves later
							// frames as RGBA, which the fallback path below expands correctly.
							if (palette != null && decoder.IsFullyPaletted())
							{
								if (frameCount == 1)
								{
									texture = new Texture((int)frameSize.X, (int)frameSize.Y, OpenBveApi.Textures.PixelFormat.Paletted, decoder.GetIndexedFrame(0), palette);
									return true;
								}
								byte[][] frameBytes = new byte[frameCount][];
								for (int i = 0; i < frameCount; i++) frameBytes[i] = decoder.GetIndexedFrame(i);
								texture = new Texture((int)frameSize.X, (int)frameSize.Y, OpenBveApi.Textures.PixelFormat.Paletted, frameBytes, palette, interval);
								return true;
							}
						}
						// Fallback RGBA (multi-frame with >256 total colors or mixed indexed/RGBA frames; single-frame failures use the GDI+ path below)
						if (frameCount != 1)
						{
							byte[][] frameBytes = new byte[frameCount][];
								for (int i = 0; i < frameCount; i++)
								{
									int[] framePixels = decoder.GetFrame(i);
									if (framePixels == null)
									{
										// Should not happen (every decoded frame has indexed or RGBA data),
										// but never hand a null frame to Texture - it would NRE downstream.
										frameBytes[i] = new byte[(int)frameSize.X * (int)frameSize.Y * sizeof(int)];
										continue;
									}
									frameBytes[i] = new byte[framePixels.Length * sizeof(int)];
									Buffer.BlockCopy(framePixels, 0, frameBytes[i], 0, frameBytes[i].Length);
								}
								texture = new Texture((int)frameSize.X, (int)frameSize.Y, OpenBveApi.Textures.PixelFormat.RGBAlpha, frameBytes, interval);
								return true;
							}
						}
					}
				}

				if (bmpHeader)
				{
					using (BmpDecoder decoder = new BmpDecoder())
					{
						if (decoder.Read(file))
						{
							texture = new Texture(decoder.Width, decoder.Height, OpenBveApi.Textures.PixelFormat.RGBAlpha, decoder.ImageData, decoder.ColorTable);
							return true;
						}
					}
				}

				if (pngHeader && !CurrentOptions.UseGDIDecoders)
				{
					// NB: GDI+ decoders are curerntly enabled by default as they are marginally faster (~10ms or so per texture unless massively interlaced which is worse)
					//     If / when mobile device support is added, these will likely be removed
					using (PngDecoder decoder = new PngDecoder())
					{
						if (decoder.Read(file))
						{
							texture = new Texture(decoder.Width, decoder.Height, (OpenBveApi.Textures.PixelFormat)decoder.BytesPerPixel, decoder.pixelBuffer, (OpenBveApi.Colors.Color24[])null);
							return true;
						}
					}
				}
			}
			catch
			{
				texture = null;
				return false;
			}
			/*
			 * Otherwise, read the bitmap. This will be a bitmap of just
			 * any format, not necessarily the one that allows
			 * us to extract the bitmap data easily.
			 */
			using (Bitmap bitmap = new Bitmap(file))
			{
				int width, height;
				byte[] raw = GetRawBitmapData(bitmap, out width, out height);
				if (raw != null)
				{
					texture = new Texture(width, height, OpenBveApi.Textures.PixelFormat.RGBAlpha, raw, (OpenBveApi.Colors.Color24[])null);
					return true;
				}
				texture = null;
				return false;
			}
			
		}

		private byte[] GetRawBitmapData(Bitmap bitmap, out int width, out int height)
		{
			Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
			// Caller owns bitmap; only converted is disposed.
			Bitmap source = bitmap;
			Bitmap converted = null;
			if (bitmap.PixelFormat != PixelFormat.Format32bppArgb) {
				converted = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
				using (Graphics graphics = Graphics.FromImage(converted))
					graphics.DrawImage(bitmap, rect, rect, GraphicsUnit.Pixel);
				source = converted;
			}
			BitmapData data = source.LockBits(rect, ImageLockMode.ReadOnly, source.PixelFormat);
			try {
				if (data.Stride != 4 * data.Width) {
					CurrentHost.ReportProblem(ProblemType.InvalidOperation, "Invalid stride encountered.");
					width = 0;
					height = 0;
					return null;
				}
				byte[] raw = new byte[data.Stride * data.Height];
				System.Runtime.InteropServices.Marshal.Copy(data.Scan0, raw, 0, raw.Length);
				width = source.Width;
				height = source.Height;
				for (int i = 0; i < raw.Length; i += 4) { // BGRA -> RGBA
					byte t = raw[i];
					raw[i] = raw[i + 2];
					raw[i + 2] = t;
				}
				return raw;
			}
			finally {
				source.UnlockBits(data);
				if (converted != null)
					converted.Dispose();
			}
		}
		
	}
}
