//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2026, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
//ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
//ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

/*
 * EnvironmentMap
 * --------------
 * Image-based lighting for the PBR path, deliberately kept small:
 *  - Shipped equirect .hdrs (Radiance RGBE, pure C# decode, no native deps),
 *    selectable at runtime (ObjectViewer lighting tab)
 *  - Uploaded once as a half-float texture with mipmaps; roughness picks the LOD
 *  - No cubemap prefilter pass, no BRDF LUT: diffuse reads the smallest mip,
 *    specular reads LOD ~ roughness * maxLod (box-filter approx, good enough for v1)
 *
 * The shipped files are pine_picnic_1k.hdr and studio_small_08_1k.hdr,
 * both by Poly Haven (CC0, no attribution required).
 * Everything here is GLSL 410 / OpenGL 4.1 safe (macOS included).
 */

using System;
using System.IO;
using System.Text;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Lightings
{
	/// <summary>Decoded RGBE image in linear float RGB, top row first</summary>
	public sealed class RgbeImage
	{
		/// <summary>Image width in pixels</summary>
		public readonly int Width;
		/// <summary>Image height in pixels</summary>
		public readonly int Height;
		/// <summary>Width * Height * 3 linear floats</summary>
		public readonly float[] Pixels;

		/// <summary>Creates a decoded image</summary>
		public RgbeImage(int width, int height, float[] pixels)
		{
			Width = width;
			Height = height;
			Pixels = pixels;
		}
	}

	/// <summary>Minimal Radiance RGBE (.hdr) decoder: RLE and flat scanlines, equirect assumed</summary>
	public static class RgbeDecoder
	{
		/// <summary>Decodes an RGBE stream</summary>
		/// <exception cref="InvalidDataException">Not an RGBE file or truncated</exception>
		public static RgbeImage Decode(Stream stream)
		{
			StringBuilder line = new StringBuilder();
			// Header lines end at the first blank line; the resolution line
			// ("-Y 512 +X 1024") follows it, per the RGBE spec.
			while (true)
			{
				int c = stream.ReadByte();
				if (c < 0) throw new InvalidDataException("Not a Radiance RGBE file (no header).");
				if (c == '\n')
				{
					if (line.Length == 0) break;
					line.Length = 0;
				}
				else if (c != '\r')
				{
					line.Append((char)c);
				}
			}
			string resolution = null;
			while (resolution == null)
			{
				int c = stream.ReadByte();
				if (c < 0) throw new InvalidDataException("Radiance header has no resolution line.");
				if (c == '\n')
				{
					if (line.Length == 0) continue;
					string text = line.ToString();
					line.Length = 0;
					if (text[0] == '-' || text[0] == '+') resolution = text;
				}
				else if (c != '\r')
				{
					line.Append((char)c);
				}
			}
			string[] parts = resolution.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length != 4) throw new InvalidDataException("Unparseable Radiance resolution line.");
			int height = int.Parse(parts[1]);
			int width = int.Parse(parts[3]);
			if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
				throw new InvalidDataException("Radiance image size out of range.");
			float[] pixels = new float[width * height * 3];
			byte[] scanline = new byte[width * 4];
			for (int y = 0; y < height; y++)
			{
				ReadScanline(stream, scanline, width);
				for (int x = 0; x < width; x++)
				{
					float r = scanline[x * 4];
					float g = scanline[x * 4 + 1];
					float b = scanline[x * 4 + 2];
					int e = scanline[x * 4 + 3];
					float scale = e == 0 ? 0.0f : (float)Math.Pow(2.0, e - 128 - 8);
					int o = (y * width + x) * 3;
					pixels[o] = r * scale;
					pixels[o + 1] = g * scale;
					pixels[o + 2] = b * scale;
				}
			}
			return new RgbeImage(width, height, pixels);
		}

		private static void ReadScanline(Stream stream, byte[] scanline, int width)
		{
			if (width < 8 || width > 0x7fff)
			{
				ReadFlat(stream, scanline, width * 4);
				return;
			}
			int b0 = stream.ReadByte();
			int b1 = stream.ReadByte();
			int b2 = stream.ReadByte();
			int b3 = stream.ReadByte();
			if (b0 < 0) throw new InvalidDataException("Truncated Radiance scanline.");
			if (b0 != 2 || b1 != 2 || (b2 & 0x80) != 0)
			{
				// Old flat format: those four bytes are the first pixel
				scanline[0] = (byte)b0;
				scanline[1] = (byte)b1;
				scanline[2] = (byte)b2;
				scanline[3] = (byte)b3;
				ReadFlat(stream, scanline, width * 4 - 4, 4);
				return;
			}
			if ((((b2 << 8) | b3)) != width) throw new InvalidDataException("Radiance scanline width mismatch.");
			for (int channel = 0; channel < 4; channel++)
			{
				int x = 0;
				while (x < width)
				{
					int count = stream.ReadByte();
					if (count < 0) throw new InvalidDataException("Truncated Radiance scanline.");
					if (count > 128)
					{
						count -= 128;
						int value = stream.ReadByte();
						if (value < 0) throw new InvalidDataException("Truncated Radiance run.");
						for (int i = 0; i < count; i++) scanline[(x + i) * 4 + channel] = (byte)value;
						x += count;
					}
					else
					{
						for (int i = 0; i < count; i++)
						{
							int value = stream.ReadByte();
							if (value < 0) throw new InvalidDataException("Truncated Radiance run.");
							scanline[(x + i) * 4 + channel] = (byte)value;
						}
						x += count;
					}
				}
			}
		}

		private static void ReadFlat(Stream stream, byte[] buffer, int count, int offset = 0)
		{
			while (count > 0)
			{
				int read = stream.Read(buffer, offset, count);
				if (read <= 0) throw new InvalidDataException("Truncated Radiance image.");
				offset += read;
				count -= read;
			}
		}
	}

	/// <summary>Owns the GL texture for image-based lighting (one shipped equirect HDR)</summary>
	public sealed class EnvironmentMap : IDisposable
	{
		private int textureName;
		private bool disposed;

		/// <summary>GL texture name, or 0 when unavailable</summary>
		public int TextureName
		{
			get { return textureName; }
		}

		/// <summary>Deepest mipmap level (diffuse reads this LOD)</summary>
		public float MaxLod { get; private set; }

		/// <summary>Whether a texture is ready to bind</summary>
		public bool IsReady
		{
			get { return textureName != 0; }
		}

		private EnvironmentMap()
		{
		}

		/// <summary>Decodes RGBE and uploads it as RGB16F with mipmaps. Returns null on failure.</summary>
		/// <remarks>Requires a current GL context. Never throws: failure means no IBL, not a crash.</remarks>
		public static EnvironmentMap TryLoad(Stream source)
		{
			RgbeImage image;
			try
			{
				image = RgbeDecoder.Decode(source);
			}
			catch
			{
				return null;
			}
			EnvironmentMap map = new EnvironmentMap();
			try
			{
				GL.GenTextures(1, out map.textureName);
				GL.BindTexture(TextureTarget.Texture2D, map.textureName);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb16f,
					image.Width, image.Height, 0, PixelFormat.Rgb, PixelType.Float, image.Pixels);
				GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
				GL.BindTexture(TextureTarget.Texture2D, 0);
				map.MaxLod = (float)Math.Floor(Math.Log(Math.Max(image.Width, image.Height), 2.0));
				return map;
			}
			catch
			{
				map.Dispose();
				return null;
			}
		}

		/// <summary>Binds the environment texture to the given unit</summary>
		public void Bind(TextureUnit unit)
		{
			if (textureName == 0) return;
			GL.ActiveTexture(unit);
			GL.BindTexture(TextureTarget.Texture2D, textureName);
			GL.ActiveTexture(TextureUnit.Texture0);
		}

		/// <summary>Releases the GL texture</summary>
		public void Dispose()
		{
			if (disposed) return;
			disposed = true;
			if (textureName != 0)
			{
				try
				{
					GL.DeleteTexture(textureName);
				}
				catch
				{
					// Shutdown path: context may already be gone
				}
				textureName = 0;
			}
			GC.SuppressFinalize(this);
		}
	}
}
