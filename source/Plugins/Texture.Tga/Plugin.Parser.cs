using System;
using System.IO;
using OpenBveApi.Hosts;
using OpenBveApi.Textures;
using OpenBveApi.Colors;

namespace Texture.Tga
{
	public partial class Plugin
	{
		private enum ImageTypes : byte
		{
			None = 0,
			UncompressedColorMapped = 1,
			UncompressedTrueColor = 2,
			UncompressedGreyscale = 3,
			CompressedColorMapped = 9,
			CompressedRGB = 10,
			CompressedGreyscale = 11
		}

		internal bool Parse(string fileName, out OpenBveApi.Textures.Texture texture)
		{
			try
			{
				using (FileStream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					if (stream.Length < 18)
					{
						texture = null;
						return false;
					}

					using (BinaryReader binReader = new BinaryReader(stream))
					{
						// Header parsing
						byte imageIDLength = binReader.ReadByte();
						bool hasColorMap = binReader.ReadByte() != 0;
						ImageTypes imageType = (ImageTypes)binReader.ReadByte();
						binReader.ReadInt16(); // colorMapStart
						int colorMapLength = binReader.ReadInt16();
						byte colorMapEntrySize = binReader.ReadByte();
						binReader.ReadInt16(); // xOrigin
						binReader.ReadInt16(); // yOrigin
						int width = binReader.ReadInt16();
						int height = binReader.ReadInt16();
						byte pixelDepth = binReader.ReadByte();
						byte descriptor = binReader.ReadByte();

						if (width <= 0 || height <= 0)
						{
							texture = null;
							return false;
						}

						// Skip Image ID
						if (imageIDLength > 0)
						{
							binReader.BaseStream.Seek(imageIDLength, SeekOrigin.Current);
						}

						// Read Color Map if any
						Color32[] colorMap = null;
						if (hasColorMap && colorMapLength > 0)
						{
							colorMap = new Color32[colorMapLength];
							for (int i = 0; i < colorMapLength; i++)
							{
								if (colorMapEntrySize == 15 || colorMapEntrySize == 16)
								{
									ushort val = binReader.ReadUInt16();
									colorMap[i] = GetColorFrom2Bytes((byte)(val >> 8), (byte)(val & 0xFF), true);
								}
								else if (colorMapEntrySize == 24)
								{
									byte b = binReader.ReadByte();
									byte g = binReader.ReadByte();
									byte r = binReader.ReadByte();
									colorMap[i] = new Color32(r, g, b, 255);
								}
								else if (colorMapEntrySize == 32)
								{
									byte b = binReader.ReadByte();
									byte g = binReader.ReadByte();
									byte r = binReader.ReadByte();
									byte a = binReader.ReadByte();
									colorMap[i] = new Color32(r, g, b, a);
								}
							}
						}

						// Orientation and attributes
						bool isTopDown = (descriptor & 0x20) != 0;
						bool isRightLeft = (descriptor & 0x10) != 0;
						int alphaBits = descriptor & 0x0F;

						// Allocation for target RGBA buffer
						byte[] result = new byte[width * height * 4];
						
						// Pixel reading logic
						bool compressed = imageType == ImageTypes.CompressedColorMapped || imageType == ImageTypes.CompressedRGB || imageType == ImageTypes.CompressedGreyscale;

						int totalPixels = width * height;
						int p = 0;

						while (p < totalPixels)
						{
							int count;
							bool isRle = false;
							if (compressed)
							{
								byte packet = binReader.ReadByte();
								count = (packet & 0x7F) + 1;
								isRle = (packet & 0x80) != 0;
							}
							else
							{
								count = totalPixels - p;
							}

							if (isRle)
							{
								// Read one pixel and repeat
								Color32 color = ReadPixel(binReader, pixelDepth, colorMap, alphaBits);
								for (int i = 0; i < count && p < totalPixels; i++, p++)
								{
									WritePixel(result, p, width, height, isTopDown, isRightLeft, color);
								}
							}
							else
							{
								// Read 'count' raw pixels
								for (int i = 0; i < count && p < totalPixels; i++, p++)
								{
									Color32 color = ReadPixel(binReader, pixelDepth, colorMap, alphaBits);
									WritePixel(result, p, width, height, isTopDown, isRightLeft, color);
								}
							}
						}

						texture = new OpenBveApi.Textures.Texture(width, height, PixelFormat.RGBAlpha, result, null);
						return true;
					}
				}
			}
			catch (Exception ex)
			{
				CurrentHost.ReportProblem(ProblemType.InvalidOperation, "Failed to load TGA: " + ex.Message);
				texture = null;
				return false;
			}
		}

		private Color32 ReadPixel(BinaryReader reader, int depth, Color32[] colorMap, int alphaBits)
		{
			switch (depth)
			{
				case 8:
					byte idx = reader.ReadByte();
					if (colorMap != null && idx < colorMap.Length)
					{
						return colorMap[idx];
					}
					// Greyscale
					return new Color32(idx, idx, idx, 255);
				case 15:
				case 16:
					ushort val = reader.ReadUInt16();
					return GetColorFrom2Bytes((byte)(val >> 8), (byte)(val & 0xFF), alphaBits > 0);
				case 24:
					byte b = reader.ReadByte();
					byte g = reader.ReadByte();
					byte r = reader.ReadByte();
					return new Color32(r, g, b, 255);
				case 32:
					byte b4 = reader.ReadByte();
					byte g4 = reader.ReadByte();
					byte r4 = reader.ReadByte();
					byte a4 = reader.ReadByte();
					if (alphaBits == 0) a4 = 255; // Opaque 32-bit TGA
					return new Color32(r4, g4, b4, a4);
				default:
					return new Color32(0, 0, 0, 255);
			}
		}

		private void WritePixel(byte[] buffer, int pixelIndex, int width, int height, bool topDown, bool rightLeft, Color32 color)
		{
			int x = pixelIndex % width;
			int y = pixelIndex / width;

			if (rightLeft) x = width - 1 - x;
			if (!topDown) y = height - 1 - y;

			int offset = (y * width + x) * 4;
			buffer[offset] = color.R;
			buffer[offset + 1] = color.G;
			buffer[offset + 2] = color.B;
			buffer[offset + 3] = color.A;
		}

		internal static Color32 GetColorFrom2Bytes(byte one, byte two, bool hasAlpha)
		{
			int r = ((one & 0x7C) >> 2) << 3;
			int g = (((one & 0x03) << 3) | ((two & 0xE0) >> 5)) << 3;
			int b = (two & 0x1F) << 3;
			// Accurate 5-bit to 8-bit mapping
			r |= (r >> 5);
			g |= (g >> 5);
			b |= (b >> 5);
			int a = 255;
			if (hasAlpha)
			{
				a = (one & 0x80) != 0 ? 255 : 0;
			}
			return new Color32((byte)r, (byte)g, (byte)b, (byte)a);
		}
	}
}
