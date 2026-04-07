using OpenBveApi;
using OpenBveApi.Colors;
using OpenBveApi.Textures;
using System;
using System.IO;
using System.IO.Compression;

namespace Texture.Ace {
	public partial class Plugin
	{
		/// <summary>The un-compressed ACE identifier</summary>
		public const ulong UncompressedACE = 0x40404153494D4953;

		/// <summary>The compressed ACE identifier</summary>
		public const ulong CompressedACE = 0x46404153494D4953;

		public const ulong DataStart = 0x4040404040404040;

		/// <summary>Gets a color from the specified integer.</summary>
		/// <param name="color">The color comprised of 5 red bits in the most significant bits, 6 green bits, and 5 blue bits in the least significant bits.</param>
		/// <returns></returns>
		private static Color32 GetColor(ushort color)
		{
			return new Color32(
				(byte)((color >> 11) << 3),
				(byte)(((color >> 5) & 0x3F) << 2),
				(byte)((color & 0x1F) << 3),
				255
			);
		}

		/// <summary>Gets the color that is half-way between the two specified colors.</summary>
		/// <param name="a">The first color.</param>
		/// <param name="b">The second color.</param>
		/// <returns>The mixed color.</returns>
		private static Color32 GetInterpolatedColor11(Color32 a, Color32 b)
		{
			return new Color32(
				(byte)((uint)a.R + (uint)b.R >> 1),
				(byte)((uint)a.G + (uint)b.G >> 1),
				(byte)((uint)a.B + (uint)b.B >> 1),
				(byte)((uint)a.A + (uint)b.A >> 1)
			);
		}

		/// <summary>Gets the color that is one third the way between the two specified colors.</summary>
		/// <param name="a">The first color.</param>
		/// <param name="b">The second color.</param>
		/// <returns>The mixed color.</returns>
		private static Color32 GetInterpolatedColor12(Color32 a, Color32 b)
		{
			return new Color32(
				(byte)(((uint)a.R + 2 * (uint)b.R) / 3),
				(byte)(((uint)a.G + 2 * (uint)b.G) / 3),
				(byte)(((uint)a.B + 2 * (uint)b.B) / 3),
				(byte)(((uint)a.A + 2 * (uint)b.A) / 3)
			);
		}

		/// <summary>Gets the color that is two thirds the way between the two specified colors.</summary>
		/// <param name="a">The first color.</param>
		/// <param name="b">The second color.</param>
		/// <returns>The mixed color.</returns>
		private static Color32 GetInterpolatedColor21(Color32 a, Color32 b)
		{
			return new Color32(
				(byte)((2 * (uint)a.R + (uint)b.R) / 3),
				(byte)((2 * (uint)a.G + (uint)b.G) / 3),
				(byte)((2 * (uint)a.B + (uint)b.B) / 3),
				(byte)((2 * (uint)a.A + (uint)b.A) / 3)
			);
		}


		// --- can load file ---

		/// <summary>Checks whether the specified file can be loaded as an ACE texture.</summary>
		/// <param name="file">The path to the file.</param>
		/// <returns>Whether the file can be load as an ACE texture.</returns>
		private static bool CanLoadFile(string file)
		{
			using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				if (stream.Length < 8) return false;
				using (BinaryReader reader = new BinaryReader(stream))
				{
					ulong identifier = reader.ReadUInt64();
					if (identifier == UncompressedACE)
					{
						return CanLoadUncompressedData(stream);
					}
					if (identifier == CompressedACE)
					{
						byte[] bytes = new byte[stream.Length];
						stream.Position = 0;
						stream.Read(bytes, 0, bytes.Length);
						return CanLoadUncompressedData(new MemoryStream(DecompressAce(bytes)));
					}
				}
			}
			return false;
		}

		private static bool CanLoadUncompressedData(Stream stream)
		{
			long pos = stream.Position;
			try
			{
				using (BinaryReader reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
				{
					if (stream.Length - pos < 16) return false;
					ulong identifier = reader.ReadUInt64();
					if (identifier != UncompressedACE) return false;
					identifier = reader.ReadUInt64();
					if (identifier != DataStart) return false;
					return true;
				}
			}
			finally
			{
				stream.Position = pos;
			}
		}

		private static void QueryDimensionsFromFile(string file, out int width, out int height)
		{
			using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				using (BinaryReader reader = new BinaryReader(stream))
				{
					ulong identifier = reader.ReadUInt64();
					if (identifier == UncompressedACE)
					{
						stream.Position = 0;
						QueryDimensionsFromStream(stream, out width, out height);
					}
					else if (identifier == CompressedACE)
					{
						byte[] bytes = new byte[stream.Length];
						stream.Position = 0;
						stream.Read(bytes, 0, bytes.Length);
						using (MemoryStream ms = new MemoryStream(DecompressAce(bytes)))
						{
							QueryDimensionsFromStream(ms, out width, out height);
						}
					}
					else
					{
						throw new InvalidDataException();
					}
				}
			}
		}

		private static void QueryDimensionsFromStream(Stream stream, out int width, out int height)
		{
			using (BinaryReader reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
			{
				ulong identifier = reader.ReadUInt64();
				if (identifier != UncompressedACE) throw new InvalidDataException();
				identifier = reader.ReadUInt64();
				if (identifier != DataStart) throw new InvalidDataException();
				if (reader.ReadInt32() != 1) throw new InvalidDataException();
				reader.ReadInt32(); // unknown
				width = reader.ReadInt32();
				height = reader.ReadInt32();
			}
		}

		private static OpenBveApi.Textures.Texture LoadFromFile(string file)
		{
			using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				using (BinaryReader reader = new BinaryReader(stream))
				{
					ulong identifier = reader.ReadUInt64();
					if (identifier == UncompressedACE)
					{
						stream.Position = 0;
						return LoadFromStream(stream);
					}
					if (identifier == CompressedACE)
					{
						byte[] bytes = new byte[stream.Length];
						stream.Position = 0;
						stream.Read(bytes, 0, bytes.Length);
						using (MemoryStream ms = new MemoryStream(DecompressAce(bytes)))
						{
							return LoadFromStream(ms);
						}
					}
					throw new InvalidDataException();
				}
			}
		}

		private static OpenBveApi.Textures.Texture LoadFromStream(Stream stream)
		{
			using (BinaryReader reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
			{
				// --- header ---
				if (reader.ReadUInt64() != UncompressedACE) throw new InvalidDataException();
				if (reader.ReadUInt64() != DataStart) throw new InvalidDataException();
				if (reader.ReadInt32() != 1) throw new InvalidDataException();
				reader.ReadInt32(); // unknown
				int width = reader.ReadInt32();
				int height = reader.ReadInt32();
				int type = reader.ReadInt32();
				if (type != 14 & type != 16 & type != 17 & type != 18) throw new InvalidDataException("Unsupported ACE compression type.");
				int channels = reader.ReadInt32();
				if (channels != 3 & channels != 4 & channels != 5) throw new InvalidDataException("Invalid number of ACE channels.");
				reader.ReadInt32(); // unknown
				reader.ReadBytes(16); // author
				reader.ReadBytes(72); // copyright
				reader.ReadInt32(); // unknown
				switch (channels)
				{
					case 3: reader.ReadBytes(80); break;
					case 4: reader.ReadBytes(96); break;
					case 5: reader.ReadBytes(112); break;
				}

				// --- actual pixel data ---
				if (type == 14 & channels == 3)
				{
					// --- rgb ---
					byte[] bytes = new byte[3 * width * height];
					int[] streamOffsets = new int[height];
					for (int y = 0; y < height; y++) streamOffsets[y] = 16 + reader.ReadInt32();
					int offset = 0;
					int offsetIncrement = -3 * width + 1;
					for (int y = 0; y < height; y++)
					{
						stream.Position = streamOffsets[y];
						for (int x = 0; x < width; x++) { bytes[offset] = reader.ReadByte(); offset += 3; }
						offset += offsetIncrement;
						for (int x = 0; x < width; x++) { bytes[offset] = reader.ReadByte(); offset += 3; }
						offset += offsetIncrement;
						for (int x = 0; x < width; x++) { bytes[offset] = reader.ReadByte(); offset += 3; }
						offset -= 2;
					}
					return new OpenBveApi.Textures.Texture(width, height, PixelFormat.RGB, bytes, null);
				}
				else if (type == 16 & channels == 4)
				{
					// --- rgb (1-bit transparency) ---
					byte[] bytes = new byte[4 * width * height];
					int[] streamOffsets = new int[height];
					for (int y = 0; y < height; y++) streamOffsets[y] = 16 + reader.ReadInt32();
					int offset = 0;
					int offsetIncrement = -4 * width + 1;
					for (int y = 0; y < height; y++)
					{
						stream.Position = streamOffsets[y];
						for (int x = 0; x < width; x++) { bytes[offset] = reader.ReadByte(); offset += 4; }
						offset += offsetIncrement;
						for (int x = 0; x < width; x++) { bytes[offset] = reader.ReadByte(); offset += 4; }
						offset += offsetIncrement;
						for (int x = 0; x < width; x++) { bytes[offset] = reader.ReadByte(); offset += 4; }
						offset += offsetIncrement;
						int value = 0; int counter = 0;
						for (int x = 0; x < width; x++)
						{
							var mask = (byte)(0x80 >> (x & 0x7));
							if (counter == 0) { value = reader.ReadByte(); counter = 7; } else { counter--; }
							bytes[offset] = (value & mask) == 0 ? (byte)0 : (byte)255;
							offset += 4;
						}
						offset -= 3;
					}
					return new OpenBveApi.Textures.Texture(width, height, PixelFormat.RGBAlpha, bytes, null);
				}
				else if (type == 17 & channels == 5)
				{
					// --- rgb (8-bit alpha) ---
					byte[] bytes = new byte[4 * width * height];
					int[] streamOffsets = new int[height];
					for (int y = 0; y < height; y++) streamOffsets[y] = 16 + reader.ReadInt32();
					int offset = 0;
					int offsetIncrement = -4 * width + 1;
					for (int y = 0; y < height; y++)
					{
						stream.Position = streamOffsets[y];
						for (int x = 0; x < width; x++) { bytes[offset] = reader.ReadByte(); offset += 4; }
						offset += offsetIncrement;
						for (int x = 0; x < width; x++) { bytes[offset] = reader.ReadByte(); offset += 4; }
						offset += offsetIncrement;
						for (int x = 0; x < width; x++) { bytes[offset] = reader.ReadByte(); offset += 4; }
						offset += offsetIncrement;
						stream.Position += (width + 7) / 8;
						for (int x = 0; x < width; x++) { bytes[offset] = reader.ReadByte(); offset += 4; }
						offset -= 3;
					}
					return new OpenBveApi.Textures.Texture(width, height, PixelFormat.RGBAlpha, bytes, null);
				}
				else if (type == 18 & (channels == 3 | channels == 4))
				{
					// --- dxt1 ---
					byte[] bytes = channels == 3 ? new byte[3 * width * height] : new byte[4 * width * height];
					int mipmapOffset0 = reader.ReadInt32() + 20;
					stream.Position = mipmapOffset0;
					int offset = 0;
					int offsetIncrementY = channels == 3 ? 9 * width : 12 * width;
					int offsetIncrementX = channels == 3 ? -12 * width + 12 : -16 * width + 16;
					int offsetIncrementDy = channels == 3 ? 3 * width - 12 : 4 * width - 16;
					Color32[] colors = new Color32[4];
					Color32 black = channels == 4 ? Color32.Transparent : Color32.Black;
					for (int y = 0; y < height; y += 4)
					{
						for (int x = 0; x < width; x += 4)
						{
							ushort entry0 = reader.ReadUInt16();
							ushort entry1 = reader.ReadUInt16();
							colors[0] = GetColor(entry0);
							colors[1] = GetColor(entry1);
							if (entry0 > entry1) { colors[2] = GetInterpolatedColor21(colors[0], colors[1]); colors[3] = GetInterpolatedColor12(colors[0], colors[1]); }
							else { colors[2] = GetInterpolatedColor11(colors[0], colors[1]); colors[3] = black; }
							uint lookup = reader.ReadUInt32();
							for (int dy = 0; dy < 4; dy++) {
								for (int dx = 0; dx < 4; dx++) {
									uint index = lookup & 3; lookup >>= 2;
									bytes[offset++] = colors[index].R;
									bytes[offset++] = colors[index].G;
									bytes[offset++] = colors[index].B;
									if (channels == 4)
									{
										bytes[offset++] = colors[index].A;
									}
								}
								offset += offsetIncrementDy;
							}
							offset += offsetIncrementX;
						}
						offset += offsetIncrementY;
					}
					return new OpenBveApi.Textures.Texture(width, height, channels == 3 ? PixelFormat.RGB :  PixelFormat.RGBAlpha, bytes, null);
				}
				else throw new NotSupportedException();
			}
		}

		/// <summary>The header bytes for a bitmap</summary>
		private static readonly byte[] bitmapHeaderBytes = { 0x53, 0x49, 0x4D, 0x49, 0x53, 0x41, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40 };

		// --- decompress ace ---

		/// <summary>Decompresses the specified zlib-compressed data.</summary>
		/// <param name="data">The compressed data including the ACE header.</param>
		/// <returns>The uncompressed data including the ACE header.</returns>
		private static byte[] DecompressAce(byte[] data)
		{
			// --- decompress data ---
			byte[] result;
			using (MemoryStream stream = new MemoryStream(data))
			{
				using (BinaryReader reader = new BinaryReader(stream))
				{
					// --- ACE header ---
					ulong identifier = reader.ReadUInt64();
					if (identifier != CompressedACE)
					{
						throw new InvalidDataException();
					}

					int uncompressedLength = reader.ReadInt32();
					identifier = reader.ReadUInt32();
					if (identifier != 0x40404040)
					{
						throw new InvalidDataException();
					}

					// --- zlib header ---
					byte cmf = reader.ReadByte();
					int cm = cmf & 15;
					if (cm != 8)
					{
						throw new InvalidDataException();
					}

					byte flg = reader.ReadByte();
					// int fcheck = flg & 31;
					if ((256 * cmf + flg) % 31 != 0)
					{
						throw new InvalidDataException();
					}

					// --- deflate data ---
					result = new byte[uncompressedLength + 16];
					Array.Copy(bitmapHeaderBytes, result, 16);

					using (DeflateStream deflate = new DeflateStream(stream, CompressionMode.Decompress, true))
					{
						using (BufferedStream bufferedStream = new BufferedStream(deflate, 8192))
						{
							int length = bufferedStream.Read(result, 16, uncompressedLength);
							if (length != uncompressedLength)
							{
								throw new InvalidDataException();
							}
						}
					}
				}
			}

			return result;
		}

		/// <summary>Gets a little-endian Int32 from the specified offset in a byte array</summary>
		/// <param name="byteArray">The byte array</param>
		/// <param name="offset">The starting offset of the Int32</param>
		internal static int ToInt32(byte[] byteArray, int offset)
		{
			return (byteArray[offset] & 0xFF) | ((byteArray[offset + 1] & 0xFF) << 8) | ((byteArray[offset + 2] & 0xFF) << 16) | ((byteArray[offset + 3] & 0xFF) << 24);
		}

		
	}
}
