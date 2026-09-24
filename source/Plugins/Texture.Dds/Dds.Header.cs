/*
 * Copyright (C) 1999, 2000 NVIDIA Corporation
 * This file is provided without support, instruction, or implied warranty of any
 * kind.  NVIDIA makes no guarantee of its fitness for a particular purpose and is
 * not liable under any circumstances for any damages or loss whatsoever arising
 * from the use or inability to use this file or items derived from it.
 *
 * Converted to C#, assorted changes to make compatible with openBVE texture loading
 * Also some minor enum conversion & cleanup
 */

using System.IO;
using System.Runtime.InteropServices;

namespace Texture.Dds
{
	/// <summary>Represents the header for a DDS texture</summary>
	/// <remarks>
	/// Header layout and validation references:
	/// https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds-pguide
	/// https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header
	/// https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header-dxt10
	/// https://learn.microsoft.com/en-us/windows/uwp/gaming/complete-code-for-ddstextureloader
	/// Limits: 16,384 pixels per side, 16 MP output, and 128 MiB input.
	/// </remarks>
	internal class DdsHeader
	{
		public const int MaxDimension = 16384;
		public const long MaxPixels = 16777216L;

		public uint flags;
		public int height;
		public int width;
		public uint sizeOrPitch;
		public int depth;
		public uint mipmapCount;
		public uint alphaBitDepth;

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		public struct PixelFormat
		{
			public uint flags;
			public FourCC fourcc;
			public int rgbBitCount;
			public uint rBitmask;
			public uint gBitmask;
			public uint bBitmask;
			public uint alphaBitmask;
		}

		public PixelFormat pixelFormat;

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		public struct DdsCaps
		{
			public uint caps1;
			public uint caps2;
			public uint caps3;
			public uint caps4;
		}

		public DdsCaps ddscaps;
		public uint texturestage;

		/// <summary>True when a DX10 extended header was present</summary>
		/// <remarks>Five-DWORD DDS_HEADER_DXT10; see
		/// https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header-dxt10</remarks>
		public bool IsDx10;
		/// <summary>DXGI format when IsDx10 is true</summary>
		public uint DxgiFormat;
		/// <summary>DX10 resource dimension</summary>
		public uint Dx10ResourceDimension;
		/// <summary>DX10 misc flag</summary>
		public uint Dx10MiscFlag;
		/// <summary>DX10 array size (array elements; number of cubes for a cube array)</summary>
		public uint Dx10ArraySize;
		/// <summary>DX10 misc flags 2</summary>
		public uint Dx10MiscFlags2;

		internal DdsHeader(BinaryReader reader)
		{
			byte[] signature;
			try
			{
				signature = reader.ReadBytes(4);
			}
			catch (EndOfStreamException ex)
			{
				throw new InvalidDataException("DDS header truncated.", ex);
			}
			if (signature.Length < 4 || !(signature[0] == 'D' && signature[1] == 'D' && signature[2] == 'S' && signature[3] == ' '))
			{
				throw new InvalidDataException("DDS Header invalid.");
			}

			if (ReadUInt32(reader) != 124)
			{
				throw new InvalidDataException("DDS Header size invalid.");
			}

			flags = ReadUInt32(reader);
			uint rawHeight = ReadUInt32(reader);
			uint rawWidth = ReadUInt32(reader);
			sizeOrPitch = ReadUInt32(reader);
			uint rawDepth = ReadUInt32(reader);
			mipmapCount = ReadUInt32(reader);
			alphaBitDepth = ReadUInt32(reader);

			for (int i = 0; i < 10; i++)
			{
				ReadUInt32(reader); // Reserved DWORD values.
			}

			if (ReadUInt32(reader) != 32)
			{
				throw new InvalidDataException("Pixel Format size invalid.");
			}

			pixelFormat.flags = ReadUInt32(reader);
			pixelFormat.fourcc = (FourCC)ReadUInt32(reader);
			uint rawRgbBitCount = ReadUInt32(reader);
			bool isFourCC = (pixelFormat.flags & DDPF_FOURCC) != 0;
			if (rawRgbBitCount == 0)
			{
				// Preserve the legacy zero-bit-count fallback.
				rawRgbBitCount = 16;
			}
			else if (!isFourCC && rawRgbBitCount > 128)
			{
				throw new InvalidDataException("DDS RGB bit count invalid.");
			}
			// RGBBitCount is unused for FourCC formats; keep a safe placeholder.
			pixelFormat.rgbBitCount = isFourCC ? 16 : (int)rawRgbBitCount;
			pixelFormat.rBitmask = ReadUInt32(reader);
			pixelFormat.gBitmask = ReadUInt32(reader);
			pixelFormat.bBitmask = ReadUInt32(reader);
			pixelFormat.alphaBitmask = ReadUInt32(reader);
			ddscaps.caps1 = ReadUInt32(reader);
			ddscaps.caps2 = ReadUInt32(reader);
			ddscaps.caps3 = ReadUInt32(reader);
			ddscaps.caps4 = ReadUInt32(reader);
			texturestage = ReadUInt32(reader);

			bool hasFourCC = (pixelFormat.flags & DDPF_FOURCC) != 0;
			if (!hasFourCC && pixelFormat.fourcc == FourCC.DX10)
			{
				throw new InvalidDataException("DDS DX10 marker is missing DDPF_FOURCC.");
			}

			if (hasFourCC && pixelFormat.fourcc == FourCC.DX10)
			{
				// DDS_HEADER_DXT10 (5 DWORDs):
				// https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header-dxt10
				DxgiFormat = ReadUInt32(reader);
				Dx10ResourceDimension = ReadUInt32(reader);
				Dx10MiscFlag = ReadUInt32(reader);
				Dx10ArraySize = ReadUInt32(reader);
				Dx10MiscFlags2 = ReadUInt32(reader);
				IsDx10 = true;

				if (Dx10ArraySize == 0)
				{
					throw new InvalidDataException("DDS DX10 array size is zero.");
				}
				if ((Dx10MiscFlags2 & DdsMiscFlags2AlphaModeMask) > DdsAlphaModeCustom)
				{
					throw new InvalidDataException("DDS DX10 alpha mode is invalid.");
				}

				width = ToDimension(rawWidth, "width");
				switch (Dx10ResourceDimension)
				{
					case 2: // DDS_DIMENSION_TEXTURE1D
						if (rawHeight > 1 || ((flags & DDSD_HEIGHT) != 0 && rawHeight != 0 && rawHeight != 1))
						{
							throw new InvalidDataException("DDS 1D height is invalid.");
						}
						height = 1;
						depth = 1;
						break;
					case 3: // DDS_DIMENSION_TEXTURE2D
						height = ToDimension(rawHeight, "height");
						depth = 1;
						break;
					case 4: // DDS_DIMENSION_TEXTURE3D
						height = ToDimension(rawHeight, "height");
						depth = rawDepth == 0 ? 1 : ToDimension(rawDepth, "depth");
						if (Dx10ArraySize != 1)
						{
							throw new InvalidDataException("DDS 3D array size must be one.");
						}
						break;
					default:
						throw new InvalidDataException("DDS DX10 resource dimension is invalid.");
				}
				if ((Dx10MiscFlag & DdsResourceMiscTextureCube) != 0 && Dx10ResourceDimension != 3)
				{
					throw new InvalidDataException("DDS cube flag requires a 2D resource.");
				}
			}
			else
			{
				width = ToDimension(rawWidth, "width");
				height = ToDimension(rawHeight, "height");
				depth = rawDepth == 0 ? 1 : ToDimension(rawDepth, "depth");
			}

			// Bound the CPU decoder's working set; output is 32-bit RGBA.
			long pixels = (long)width * (long)height;
			if (pixels > MaxPixels || pixels * 4L > int.MaxValue)
			{
				throw new InvalidDataException("DDS dimensions too large.");
			}
		}

		private static uint ReadUInt32(BinaryReader reader)
		{
			try
			{
				return reader.ReadUInt32();
			}
			catch (EndOfStreamException ex)
			{
				throw new InvalidDataException("DDS header truncated.", ex);
			}
		}

		private static int ToDimension(uint value, string name)
		{
			if (value == 0 || value > MaxDimension)
			{
				throw new InvalidDataException("DDS " + name + " is invalid.");
			}
			return (int)value;
		}

		public const uint DDPF_FOURCC = 0x00000004;
		public const uint DDSD_HEIGHT = 0x00000002;
		public const uint DdsMiscFlags2AlphaModeMask = 0x00000007;
		public const uint DdsAlphaModeUnknown = 0;
		public const uint DdsAlphaModeStraight = 1;
		public const uint DdsAlphaModePremultiplied = 2;
		public const uint DdsAlphaModeOpaque = 3;
		public const uint DdsAlphaModeCustom = 4;
		public const uint DdsResourceMiscTextureCube = 0x00000004;
	}
}
