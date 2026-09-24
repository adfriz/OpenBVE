/*
 * Copyright (C) 1999, 2000 NVIDIA Corporation
 * This file is provided without support, instruction, or implied warranty of any
 * kind.  NVIDIA makes no guarantee of its fitness for a particular purpose and is
 * not liable under any circumstances for any damages or loss whatsoever arising
 * from the use or inability to use this file or items derived from it.
 *
 * Converted to C#, assorted changes to make compatible with openBVE texture loading
 * Also some minor enum conversion & cleanup
 *
 * DDS parsing, DX10 mapping, pitch handling, and top-mip streaming references:
 * - Layout and headers:
 *   https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds-pguide
 *   https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header
 *   https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header-dxt10
 * - DXGI formats:
 *   https://learn.microsoft.com/en-us/windows/win32/api/dxgiformat/ne-dxgiformat-dxgi_format
 * - File layout and reference loader:
 *   https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds-reference
 *   https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-file-layout-for-textures
 *   https://learn.microsoft.com/en-us/windows/uwp/gaming/complete-code-for-ddstextureloader
 *   https://github.com/microsoft/DirectXTex
 * - Block sizes:
 *   https://learn.microsoft.com/en-us/windows/win32/direct3d11/texture-block-compression-in-direct3d-11
 * - Legacy FourCC aliases:
 *   https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds-pguide
 *   https://learn.microsoft.com/en-us/windows/uwp/gaming/complete-code-for-ddstextureloader
 */

// ReSharper disable UnusedMember.Local
// ReSharper disable NotAccessedField.Local

using System;
using System.IO;
using System.Text;
using OpenBveApi.Colors;

namespace Texture.Dds
{
	public class DDSImage
	{
		private const long MaxInputBytes = 128L * 1024L * 1024L;
		internal OpenBveApi.Textures.Texture myTexture;

		public DDSImage(byte[] ddsImage)
		{
			if (ddsImage == null) return;
			if (ddsImage.Length == 0) return;
			using (MemoryStream stream = new MemoryStream(ddsImage, false))
			{
				using (BinaryReader reader = new BinaryReader(stream))
				{
					this.Parse(reader);
				}
			}
		}

		public DDSImage(Stream stream)
		{
			if (stream == null) return;
			// leaveOpen: the caller owns the stream (e.g. LoadTexture's FileStream).
			using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, true))
			{
				this.Parse(reader);
			}
		}

		private void Parse(BinaryReader reader)
		{
			DdsHeader header = new DdsHeader(reader);
			// Depth > 1 (volume) / cubemap arrays: only the first slice is decoded.
			// Mipmaps: only the top level is decoded (fast + saves memory).
			int srcPitch;
			PixelFormat pixelFormat = GetFormat(header, out srcPitch);
			if (header.IsDx10)
			{
				uint alphaMode = header.Dx10MiscFlags2 & DdsHeader.DdsMiscFlags2AlphaModeMask;
				if (alphaMode == DdsHeader.DdsAlphaModePremultiplied || alphaMode == DdsHeader.DdsAlphaModeCustom)
				{
					throw new InvalidDataException("DDS alpha mode is not supported.");
				}
			}
			byte[] data = this.ReadTopLevelData(reader, header, pixelFormat, srcPitch);
			if (data == null)
			{
				throw new InvalidDataException("No data read from DDS file.");
			}
			byte[] rawData = this.DecompressData(header, data, pixelFormat, srcPitch);
			if (header.IsDx10 && (header.Dx10MiscFlags2 & DdsHeader.DdsMiscFlags2AlphaModeMask) == DdsHeader.DdsAlphaModeOpaque)
			{
				for (int i = 3; i < rawData.Length; i += 4)
				{
					rawData[i] = 255;
				}
			}
			CreateTexture(header.width, header.height, rawData);
		}

		private byte[] ReadTopLevelData(BinaryReader reader, DdsHeader header, PixelFormat pixelFormat, int srcPitch)
		{
			// Read only the first slice/face and top mip; skip remaining surfaces.
			// Block sizes and file layout:
			// https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-file-layout-for-textures
			// https://learn.microsoft.com/en-us/windows/win32/direct3d11/texture-block-compression-in-direct3d-11
			long expected;
			switch (pixelFormat)
			{
				case PixelFormat.DXT1:
				case PixelFormat.ATI1N:
				case PixelFormat.R8_BC4:
					expected = (long)((header.width + 3) / 4) * ((header.height + 3) / 4) * 8;
					break;
				case PixelFormat.DXT2:
				case PixelFormat.DXT3:
				case PixelFormat.DXT4:
				case PixelFormat.DXT5:
				case PixelFormat.THREEDC:
				case PixelFormat.RG8_BC5:
				case PixelFormat.BC7:
				case PixelFormat.RXGB:
					expected = (long)((header.width + 3) / 4) * ((header.height + 3) / 4) * 16;
					break;
				case PixelFormat.R16F:
					// Legacy FourCC (srcPitch == 0): tightly packed. DX10: honors file pitch.
					expected = srcPitch > 0 ? (long)srcPitch * header.height : (long)header.width * header.height * 2;
					break;
				case PixelFormat.G16R16F:
					expected = srcPitch > 0 ? (long)srcPitch * header.height : (long)header.width * header.height * 4;
					break;
				case PixelFormat.R8:
					expected = srcPitch > 0 ? (long)srcPitch * header.height : (long)header.width * header.height;
					break;
				case PixelFormat.RG8:
					expected = srcPitch > 0 ? (long)srcPitch * header.height : (long)header.width * header.height * 2;
					break;
				case PixelFormat.R16:
					expected = srcPitch > 0 ? (long)srcPitch * header.height : (long)header.width * header.height * 2;
					break;
				case PixelFormat.RG16:
					expected = srcPitch > 0 ? (long)srcPitch * header.height : (long)header.width * header.height * 4;
					break;
				case PixelFormat.A16B16G16R16F:
				case PixelFormat.A16B16G16R16:
				case PixelFormat.R16G16B16A16:
					expected = srcPitch > 0 ? (long)srcPitch * header.height : (long)header.width * header.height * 8;
					break;
				case PixelFormat.R32F:
					expected = srcPitch > 0 ? (long)srcPitch * header.height : (long)header.width * header.height * 4;
					break;
				case PixelFormat.R32_FLOAT:
					expected = srcPitch > 0 ? (long)srcPitch * header.height : (long)header.width * header.height * 4;
					break;
				case PixelFormat.G32R32F:
					expected = srcPitch > 0 ? (long)srcPitch * header.height : (long)header.width * header.height * 8;
					break;
				case PixelFormat.A32B32G32R32F:
					expected = srcPitch > 0 ? (long)srcPitch * header.height : (long)header.width * header.height * 16;
					break;
				case PixelFormat.A8:
				case PixelFormat.LUMINANCE:
					expected = (long)srcPitch * header.height;
					break;
				default:
					// Uncompressed incl. RGBA/RGB/LUMINANCE_ALPHA/RG8/R10G10B10A2 etc.
					expected = (long)srcPitch * header.height;
					break;
			}

			if (expected <= 0 || expected > MaxInputBytes || expected > int.MaxValue)
			{
				throw new InvalidDataException("DDS data size invalid.");
			}
			int count = (int)expected;
			Stream stream = reader.BaseStream;
			if (stream.CanSeek)
			{
				long remaining = stream.Length - stream.Position;
				if (remaining < count)
				{
					throw new InvalidDataException("DDS file truncated.");
				}
			}
			return ReadExactly(reader, count);
		}

		private static byte[] ReadExactly(BinaryReader reader, int count)
		{
			byte[] data = new byte[count];
			int offset = 0;
			while (offset < count)
			{
				int read = reader.Read(data, offset, count - offset);
				if (read == 0)
				{
					throw new InvalidDataException("DDS file truncated.");
				}
				offset += read;
			}
			return data;
		}

		private void CreateTexture(int width, int height, byte[] rawData)
		{
			int expected = width * height * 4;
			if (rawData == null || rawData.Length != expected)
			{
				throw new InvalidDataException("Decompressed DDS data is shorter than expected.");
			}
			myTexture = new OpenBveApi.Textures.Texture(width, height, OpenBveApi.Textures.PixelFormat.RGBAlpha, rawData, (OpenBveApi.Colors.Color24[])null);
		}

		internal static void ValidateHeader(DdsHeader header)
		{
			int srcPitch;
			GetFormat(header, out srcPitch);
			if (header.IsDx10)
			{
				uint alphaMode = header.Dx10MiscFlags2 & DdsHeader.DdsMiscFlags2AlphaModeMask;
				if (alphaMode == DdsHeader.DdsAlphaModePremultiplied || alphaMode == DdsHeader.DdsAlphaModeCustom)
				{
					throw new InvalidDataException("DDS alpha mode is not supported.");
				}
			}
		}

		internal static PixelFormat GetFormat(DdsHeader header, out int srcPitch)
		{
			srcPitch = 0;
			if (header.IsDx10)
			{
				return GetDx10Format(header, out srcPitch);
			}

			PixelFormat format;
			uint knownFlags = DDPF_ALPHAPIXELS | DDPF_ALPHA | DDPF_FOURCC | DDPF_PALETTEINDEXED8 | DDPF_RGB | DDPF_LUMINANCE | DDPF_YUV;
			if ((header.pixelFormat.flags & ~knownFlags) != 0 || (header.pixelFormat.flags & DDPF_YUV) != 0)
			{
				throw new InvalidDataException("Unsupported DDS pixel-format flags.");
			}
			if ((header.pixelFormat.flags & DDPF_FOURCC) == DDPF_FOURCC)
			{
				switch (header.pixelFormat.fourcc)
				{
					case FourCC.DXT1:
						format = PixelFormat.DXT1;
						break;
					case FourCC.DXT2:
						format = PixelFormat.DXT2;
						break;
					case FourCC.DXT3:
						format = PixelFormat.DXT3;
						break;
					case FourCC.DXT4:
						format = PixelFormat.DXT4;
						break;
					case FourCC.DXT5:
						format = PixelFormat.DXT5;
						break;
					case FourCC.ATI1:
					case FourCC.BC4U:
						format = PixelFormat.ATI1N;
						break;
					case FourCC.BC4S:
						throw new InvalidDataException("BC4 SNORM is not supported.");
					case FourCC.ATI2:
						// Legacy ATI2 is used by the 3DC/normal-map path.
						format = PixelFormat.THREEDC;
						break;
					case FourCC.BC5U:
						format = PixelFormat.RG8_BC5;
						break;
					case FourCC.BC5S:
						throw new InvalidDataException("BC5 SNORM is not supported.");
					case FourCC.RXGB:
						format = PixelFormat.RXGB;
						break;
					case FourCC.DOLLARNULL:
						format = PixelFormat.A16B16G16R16;
						break;
					case FourCC.oNULL:
						format = PixelFormat.R16F;
						break;
					case FourCC.pNULL:
						format = PixelFormat.G16R16F;
						break;
					case FourCC.qNULL:
						format = PixelFormat.A16B16G16R16F;
						break;
					case FourCC.rNULL:
						format = PixelFormat.R32F;
						break;
					case FourCC.sNULL:
						format = PixelFormat.G32R32F;
						break;
					case FourCC.tNULL:
						format = PixelFormat.A32B32G32R32F;
						break;
					default:
						throw new InvalidDataException("Compressed DDS PixelFormat value invalid");
				}
				int bits;
				switch (format)
				{
					case PixelFormat.A16B16G16R16:
					case PixelFormat.A16B16G16R16F:
						bits = 64;
						break;
					case PixelFormat.G16R16F:
						bits = 32;
						break;
					case PixelFormat.G32R32F:
						bits = 64;
						break;
					case PixelFormat.A32B32G32R32F:
						bits = 128;
						break;
					case PixelFormat.R16F:
						bits = 16;
						break;
					case PixelFormat.R32F:
						bits = 32;
						break;
					default:
						bits = 0;
						break;
				}
				if (bits > 0)
				{
					header.pixelFormat.rgbBitCount = bits;
					srcPitch = ComputeSrcPitch(header);
				}
				return format;
			}

			if ((header.pixelFormat.flags & DDPF_PALETTEINDEXED8) == DDPF_PALETTEINDEXED8)
			{
				throw new InvalidDataException("Paletted DDS textures are not supported.");
			}

			if ((header.pixelFormat.flags & DDPF_LUMINANCE) == DDPF_LUMINANCE)
			{
				format = (header.pixelFormat.flags & DDPF_ALPHAPIXELS) == DDPF_ALPHAPIXELS ? PixelFormat.LUMINANCE_ALPHA : PixelFormat.LUMINANCE;
			}
			else if ((header.pixelFormat.flags & DDPF_ALPHA) == DDPF_ALPHA && (header.pixelFormat.flags & DDPF_RGB) == 0)
			{
				format = PixelFormat.A8;
			}
			else
			{
				if ((header.pixelFormat.flags & DDPF_RGB) == 0)
				{
					throw new InvalidDataException("Unsupported DDS pixel-format flags.");
				}
				format = (header.pixelFormat.flags & DDPF_ALPHAPIXELS) == DDPF_ALPHAPIXELS ? PixelFormat.RGBA : PixelFormat.RGB;
			}

			srcPitch = ComputeSrcPitch(header);
			return format;
		}

		private static PixelFormat GetDx10Format(DdsHeader header, out int srcPitch)
		{
			// DXGI_FORMAT values and DDS format mappings:
			// https://learn.microsoft.com/en-us/windows/win32/api/dxgiformat/ne-dxgiformat-dxgi_format
			// https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds-pguide
			// https://learn.microsoft.com/en-us/windows/win32/direct3d11/texture-block-compression-in-direct3d-11
			srcPitch = 0;
			switch (header.DxgiFormat)
			{
				case 28: // R8G8B8A8_UNORM
					header.pixelFormat.rgbBitCount = 32;
					header.pixelFormat.rBitmask = 0x000000FF;
					header.pixelFormat.gBitmask = 0x0000FF00;
					header.pixelFormat.bBitmask = 0x00FF0000;
					header.pixelFormat.alphaBitmask = 0xFF000000;
					srcPitch = ComputeSrcPitch(header);
					return PixelFormat.RGBA;
				case 27: // R8G8B8A8_TYPELESS
				case 29: // R8G8B8A8_UNORM_SRGB
				case 30: case 31: case 32: // UINT/SNORM/SINT
					throw new InvalidDataException("Only concrete UNORM RGBA8 is supported.");
				case 87: // B8G8R8A8_UNORM
					header.pixelFormat.rgbBitCount = 32;
					header.pixelFormat.rBitmask = 0x00FF0000;
					header.pixelFormat.gBitmask = 0x0000FF00;
					header.pixelFormat.bBitmask = 0x000000FF;
					header.pixelFormat.alphaBitmask = 0xFF000000;
					srcPitch = ComputeSrcPitch(header);
					return PixelFormat.RGBA;
				case 90: case 91: // TYPELESS / SRGB
					throw new InvalidDataException("Only concrete UNORM BGRA8 is supported.");
				case 88: // B8G8R8X8_UNORM
					header.pixelFormat.rgbBitCount = 32;
					header.pixelFormat.rBitmask = 0x00FF0000;
					header.pixelFormat.gBitmask = 0x0000FF00;
					header.pixelFormat.bBitmask = 0x000000FF;
					header.pixelFormat.alphaBitmask = 0x00000000;
					srcPitch = ComputeSrcPitch(header);
					return PixelFormat.RGB;
				case 92: case 93: // TYPELESS / SRGB
					throw new InvalidDataException("Only concrete UNORM BGRX8 is supported.");
				case 61: // R8_UNORM
					header.pixelFormat.rgbBitCount = 8;
					header.pixelFormat.rBitmask = 0xFF;
					header.pixelFormat.gBitmask = 0;
					header.pixelFormat.bBitmask = 0;
					header.pixelFormat.alphaBitmask = 0;
					srcPitch = ComputeSrcPitch(header);
					return PixelFormat.R8;
				case 60: case 62: case 63: case 64: // TYPELESS / UINT / SNORM / SINT
					throw new InvalidDataException("Only concrete UNORM R8 is supported.");
				case 65: // A8_UNORM
					header.pixelFormat.rgbBitCount = 8;
					header.pixelFormat.rBitmask = 0;
					header.pixelFormat.gBitmask = 0;
					header.pixelFormat.bBitmask = 0;
					header.pixelFormat.alphaBitmask = 0xFF;
					srcPitch = ComputeSrcPitch(header);
					return PixelFormat.A8;
				case 49: // R8G8_UNORM
					header.pixelFormat.rgbBitCount = 16;
					header.pixelFormat.rBitmask = 0x00FF;
					header.pixelFormat.gBitmask = 0xFF00;
					header.pixelFormat.bBitmask = 0;
					header.pixelFormat.alphaBitmask = 0;
					srcPitch = ComputeSrcPitch(header);
					return PixelFormat.RG8;
				case 48: case 50: case 51: case 52: // TYPELESS / UINT / SNORM / SINT
					throw new InvalidDataException("Only concrete UNORM R8G8 is supported.");
				case 35: // R16G16_UNORM
					header.pixelFormat.rgbBitCount = 32;
					header.pixelFormat.rBitmask = 0x0000FFFF;
					header.pixelFormat.gBitmask = 0xFFFF0000;
					header.pixelFormat.bBitmask = 0;
					header.pixelFormat.alphaBitmask = 0;
					srcPitch = ComputeSrcPitch(header);
					return PixelFormat.RG16;
				case 56: // R16_UNORM
					header.pixelFormat.rgbBitCount = 16;
					header.pixelFormat.rBitmask = 0x0000FFFF;
					header.pixelFormat.gBitmask = 0;
					header.pixelFormat.bBitmask = 0;
					header.pixelFormat.alphaBitmask = 0;
					srcPitch = ComputeSrcPitch(header);
					return PixelFormat.R16;
				case 33: case 34: case 36: case 37: case 38: // R16G16 typeless/float/uint/snorm/sint
					if (header.DxgiFormat == 34)
					{
						srcPitch = ComputeFloatPitch(header, 4);
						return PixelFormat.G16R16F;
					}
					throw new InvalidDataException("Only concrete UNORM R16G16 or FLOAT R16G16 is supported.");
				case 71: case 70: // BC1_UNORM / BC1_TYPELESS
					return PixelFormat.DXT1;
				case 72: // BC1_UNORM_SRGB
					throw new InvalidDataException("sRGB BC1 textures are not supported.");
				case 74: case 73: // BC2_UNORM / BC2_TYPELESS
					return PixelFormat.DXT3;
				case 75: // BC2_UNORM_SRGB
					throw new InvalidDataException("sRGB BC2 textures are not supported.");
				case 77: case 76: // BC3_UNORM / BC3_TYPELESS
					return PixelFormat.DXT5;
				case 78: // BC3_UNORM_SRGB
					throw new InvalidDataException("sRGB BC3 textures are not supported.");
				case 80: case 79: // BC4_UNORM / BC4_TYPELESS
					return PixelFormat.R8_BC4;
				case 81: // BC4_SNORM
					throw new InvalidDataException("BC4 SNORM is not supported.");
				case 83: case 82: // BC5_UNORM / BC5_TYPELESS
					return PixelFormat.RG8_BC5;
				case 84: // BC5_SNORM
					throw new InvalidDataException("BC5 SNORM is not supported.");
				case 97: case 98: // BC7_TYPELESS / BC7_UNORM
					// Decoder spec: https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format
					return PixelFormat.BC7;
				case 99: // BC7_UNORM_SRGB
					throw new InvalidDataException("sRGB BC7 textures are not supported.");
				case 85: // B5G6R5_UNORM
					header.pixelFormat.rgbBitCount = 16;
					header.pixelFormat.rBitmask = 0xF800;
					header.pixelFormat.gBitmask = 0x07E0;
					header.pixelFormat.bBitmask = 0x001F;
					header.pixelFormat.alphaBitmask = 0;
					srcPitch = ComputeSrcPitch(header);
					return PixelFormat.RGB;
				case 86: // B5G5R5A1_UNORM
					header.pixelFormat.rgbBitCount = 16;
					header.pixelFormat.rBitmask = 0x7C00;
					header.pixelFormat.gBitmask = 0x03E0;
					header.pixelFormat.bBitmask = 0x001F;
					header.pixelFormat.alphaBitmask = 0x8000;
					srcPitch = ComputeSrcPitch(header);
					return PixelFormat.RGBA;
				case 115: // B4G4R4A4_UNORM
					header.pixelFormat.rgbBitCount = 16;
					header.pixelFormat.rBitmask = 0x0F00;
					header.pixelFormat.gBitmask = 0x00F0;
					header.pixelFormat.bBitmask = 0x000F;
					header.pixelFormat.alphaBitmask = 0xF000;
					srcPitch = ComputeSrcPitch(header);
					return PixelFormat.RGBA;
				case 24: // R10G10B10A2_UNORM
					header.pixelFormat.rgbBitCount = 32;
					header.pixelFormat.rBitmask = 0x000003FF;
					header.pixelFormat.gBitmask = 0x000FFC00;
					header.pixelFormat.bBitmask = 0x3FF00000;
					header.pixelFormat.alphaBitmask = 0xC0000000;
					srcPitch = ComputeSrcPitch(header);
					return PixelFormat.RGBA;
				case 23: case 25: // TYPELESS / UINT
					throw new InvalidDataException("Only concrete UNORM R10G10B10A2 is supported.");
				case 11: // R16G16B16A16_UNORM
					header.pixelFormat.rgbBitCount = 64;
					srcPitch = ComputeSrcPitch(header);
					return PixelFormat.R16G16B16A16;
				case 10: // R16G16B16A16_FLOAT
					srcPitch = ComputeFloatPitch(header, 8);
					return PixelFormat.A16B16G16R16F;
				case 2: // R32G32B32A32_FLOAT
					srcPitch = ComputeFloatPitch(header, 16);
					return PixelFormat.A32B32G32R32F;
				case 41: // R32_FLOAT
					srcPitch = ComputeFloatPitch(header, 4);
					return PixelFormat.R32_FLOAT;
				case 39:
					throw new InvalidDataException("R32 TYPELESS is not supported.");
				case 54: // R16_FLOAT
					srcPitch = ComputeFloatPitch(header, 2);
					return PixelFormat.R16F;
				case 53:
					throw new InvalidDataException("R16 TYPELESS is not supported.");
				case 16: // R32G32_FLOAT
					srcPitch = ComputeFloatPitch(header, 8);
					return PixelFormat.G32R32F;
				case 15:
					throw new InvalidDataException("R32G32 TYPELESS is not supported.");
				default:
					throw new InvalidDataException("Unsupported DXGI format (" + header.DxgiFormat + "). BC6H HDR textures are not supported.");
			}
		}

		private static int ComputeSrcPitch(DdsHeader header)
		{
			// Tight pitch is (width * bits + 7) / 8; honor a sane DDSD_PITCH value.
			// https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds-pguide
			int bits = header.pixelFormat.rgbBitCount;
			if (bits <= 0) bits = 32;
			long rowBytesLong = ((long)header.width * bits + 7L) / 8L;
			if (rowBytesLong <= 0 || rowBytesLong > int.MaxValue) throw new InvalidDataException("DDS pitch invalid.");
			int rowBytes = (int)rowBytesLong;
			// Accept up to 4 KiB of row padding, but reject invalid pitch/data sizes.
			if ((header.flags & DDSD_PITCH) != 0 && (long)header.sizeOrPitch >= rowBytesLong)
			{
				long padding = (long)header.sizeOrPitch - rowBytesLong;
				if (padding <= 4096)
				{
					long total = (long)header.sizeOrPitch * header.height;
					if (total > 0 && total <= int.MaxValue)
					{
						return (int)header.sizeOrPitch;
					}
				}
			}
			return rowBytes;
		}

		private static int ComputeFloatPitch(DdsHeader header, int bytesPerPixel)
		{
			// Tight default; honors file pitch when present via ComputeSrcPitch.
			header.pixelFormat.rgbBitCount = bytesPerPixel * 8;
			header.pixelFormat.rBitmask = 0;
			header.pixelFormat.gBitmask = 0;
			header.pixelFormat.bBitmask = 0;
			header.pixelFormat.alphaBitmask = 0;
			return ComputeSrcPitch(header);
		}

		private static void DecodeMask(uint mask, out int shift, out int bits)
		{
			if (mask == 0)
			{
				shift = 0;
				bits = 0;
				return;
			}
			shift = 0;
			uint m = mask;
			while ((m & 1U) == 0)
			{
				m >>= 1;
				shift++;
			}
			bits = 0;
			while ((m & 1U) != 0)
			{
				bits++;
				m >>= 1;
			}
		}

		private static byte ScaleChannel(uint pixel, uint mask, int shift, int bits)
		{
			if (bits == 0) return 0;
			uint v = (pixel & mask) >> shift;
			if (bits >= 8)
			{
				return (byte)(v >> (bits - 8));
			}
			uint max = (1U << bits) - 1U;
			return (byte)((v * 255U + (max >> 1)) / max);
		}

		private static byte FloatToByte(float f)
		{
			if (float.IsNaN(f)) return 0;
			float v = f * 255.0f + 0.5f;
			if (v <= 0.0f) return 0;
			if (v >= 255.0f) return 255;
			return (byte)v;
		}

		private static void CorrectPremult(uint pixnum, byte[] buffer)
		{
			// DXT2/DXT4 are premultiplied; undo that on the CPU.
			// https://learn.microsoft.com/en-us/windows/win32/direct3d11/texture-block-compression-in-direct3d-11
			for (uint i = 0; i < pixnum; i++)
			{
				uint o = i * 4U;
				byte alpha = buffer[o + 3];
				if (alpha == 0 || alpha == 255) continue;
				int r = (buffer[o] * 255) / alpha;
				int g = (buffer[o + 1] * 255) / alpha;
				int b = (buffer[o + 2] * 255) / alpha;
				buffer[o] = (byte)(r > 255 ? 255 : r);
				buffer[o + 1] = (byte)(g > 255 ? 255 : g);
				buffer[o + 2] = (byte)(b > 255 ? 255 : b);
			}
		}

		private static ushort ReadUInt16(byte[] data, int offset)
		{
			return (ushort)(data[offset] | (data[offset + 1] << 8));
		}

		private static uint ReadUInt32(byte[] data, int offset)
		{
			return (uint)data[offset]
				| ((uint)data[offset + 1] << 8)
				| ((uint)data[offset + 2] << 16)
				| ((uint)data[offset + 3] << 24);
		}

		private static ulong ReadUInt64(byte[] data, int offset)
		{
			return (uint)data[offset]
				| ((ulong)data[offset + 1] << 8)
				| ((ulong)data[offset + 2] << 16)
				| ((ulong)data[offset + 3] << 24)
				| ((ulong)data[offset + 4] << 32)
				| ((ulong)data[offset + 5] << 40)
				| ((ulong)data[offset + 6] << 48)
				| ((ulong)data[offset + 7] << 56);
		}

		private static ulong ReadUInt48(byte[] data, int offset)
		{
			return (uint)data[offset]
				| ((ulong)data[offset + 1] << 8)
				| ((ulong)data[offset + 2] << 16)
				| ((ulong)data[offset + 3] << 24)
				| ((ulong)data[offset + 4] << 32)
				| ((ulong)data[offset + 5] << 40);
		}

		// Fixed-point color interpolation from bcdec v0.985:
		// https://github.com/iOrange/bcdec
		private static int InterpolateDxt5TwoThirds(int a, int b)
		{
			return ((2 * a + b) * 351 + 61) >> 7;
		}

		private static int InterpolateDxt5OneThird(int a, int b)
		{
			return ((a + 2 * b) * 351 + 61) >> 7;
		}

		private static int InterpolateDxt6TwoThirds(int a, int b)
		{
			return ((2 * a + b) * 2763 + 1039) >> 11;
		}

		private static int InterpolateDxt6OneThird(int a, int b)
		{
			return ((a + 2 * b) * 2763 + 1039) >> 11;
		}

		private static int InterpolateDxt5Average(int a, int b)
		{
			return ((a + b) * 1053 + 125) >> 8;
		}

		private static int InterpolateDxt6Average(int a, int b)
		{
			return ((a + b) * 4145 + 1019) >> 11;
		}

		private static void DecodeDxtColors(ushort color0, ushort color1, bool allowBinaryAlpha, Color32[] colors)
		{
			int r0 = (color0 >> 11) & 31;
			int g0 = (color0 >> 5) & 63;
			int b0 = color0 & 31;
			int r1 = (color1 >> 11) & 31;
			int g1 = (color1 >> 5) & 63;
			int b1 = color1 & 31;
			colors[0] = new Color32((byte)((r0 * 527 + 23) >> 6), (byte)((g0 * 259 + 33) >> 6), (byte)((b0 * 527 + 23) >> 6), 255);
			colors[1] = new Color32((byte)((r1 * 527 + 23) >> 6), (byte)((g1 * 259 + 33) >> 6), (byte)((b1 * 527 + 23) >> 6), 255);
			if (allowBinaryAlpha && color0 <= color1)
			{
				colors[2] = new Color32((byte)InterpolateDxt5Average(r0, r1), (byte)InterpolateDxt6Average(g0, g1), (byte)InterpolateDxt5Average(b0, b1), 255);
				colors[3] = new Color32(0, 0, 0, 0);
			}
			else
			{
				colors[2] = new Color32((byte)InterpolateDxt5TwoThirds(r0, r1), (byte)InterpolateDxt6TwoThirds(g0, g1), (byte)InterpolateDxt5TwoThirds(b0, b1), 255);
				colors[3] = new Color32((byte)InterpolateDxt5OneThird(r0, r1), (byte)InterpolateDxt6OneThird(g0, g1), (byte)InterpolateDxt5OneThird(b0, b1), 255);
			}
		}

		private static uint HalfToFloat(ushort y)
		{
			int s = (y >> 15) & 0x00000001;
			int e = (y >> 10) & 0x0000001f;
			int m = y & 0x000003ff;

			if (e == 0)
			{
				if (m == 0)
				{
					//
					// Plus or minus zero
					//
					return (uint)(s << 31);
				}

				//
				// Denormalized number -- renormalize it
				//
				while ((m & 0x00000400) == 0)
				{
					m <<= 1;
					e -= 1;
				}

				e += 1;
				m &= ~0x00000400;
			}
			else if (e == 31)
			{
				if (m == 0)
				{
					//
					// Positive or negative infinity
					//
					return (uint)((s << 31) | 0x7f800000);
				}

				//
				// Nan -- preserve sign and significand bits
				//
				return (uint)((s << 31) | 0x7f800000 | (m << 13));
			}

			//
			// Normalized number
			//
			e += 127 - 15;
			m <<= 13;

			//
			// Assemble s, e and m.
			//
			return (uint)((s << 31) | (e << 23) | m);
		}

		private static unsafe float HalfToFloatF(ushort y)
		{
			uint u = HalfToFloat(y);
			return *(float*)&u;
		}

		private byte[] DecompressData(DdsHeader header, byte[] data, PixelFormat pixelFormat, int srcPitch)
		{
			switch (pixelFormat)
			{
				case PixelFormat.RGBA:
				case PixelFormat.RGB:
					return DecompressUncompressed(header, data, pixelFormat, srcPitch);
				case PixelFormat.R8:
				case PixelFormat.A8:
				case PixelFormat.LUMINANCE:
				case PixelFormat.LUMINANCE_ALPHA:
				case PixelFormat.RG8:
				case PixelFormat.R16:
				case PixelFormat.RG16:
				case PixelFormat.R16G16B16A16:
					return DecompressUncompressed(header, data, pixelFormat, srcPitch);
				case PixelFormat.DXT1:
					return DecompressDXT1(header, data);
				case PixelFormat.DXT2:
					return DecompressDXT2(header, data);
				case PixelFormat.DXT3:
					return DecompressDXT3(header, data);
				case PixelFormat.DXT4:
					return DecompressDXT4(header, data);
				case PixelFormat.DXT5:
					return DecompressDXT5(header, data);
				case PixelFormat.THREEDC:
					return Decompress3Dc(header, data, true);
				case PixelFormat.RG8_BC5:
					return Decompress3Dc(header, data, false);
				case PixelFormat.ATI1N:
				case PixelFormat.R8_BC4:
					return DecompressAti1n(header, data);
				case PixelFormat.BC7:
					return Bc7Decoder.Decompress(header, data);
				case PixelFormat.RXGB:
					return DecompressRXGB(header, data);
				case PixelFormat.R16F:
				case PixelFormat.G16R16F:
				case PixelFormat.A16B16G16R16F:
				case PixelFormat.R32F:
				case PixelFormat.R32_FLOAT:
				case PixelFormat.G32R32F:
				case PixelFormat.A32B32G32R32F:
				case PixelFormat.A16B16G16R16:
					return DecompressFloat(header, data, pixelFormat, srcPitch);
				default:
					throw new InvalidDataException("Unsupported DDS PixelFormat value");
			}
		}

		private static unsafe void RequireNativeAlignment(byte[] data, int alignment)
		{
			fixed (byte* pointer = data)
			{
				IntPtr address = (IntPtr)pointer;
				if ((address.ToInt64() & (alignment - 1)) != 0)
				{
					throw new InvalidDataException("DDS block data is not aligned for native decoding.");
				}
			}
		}

		private byte[] DecompressDXT1(DdsHeader header, byte[] data)
		{
			int width = header.width;
			int height = header.height;
			int bps = width * 4;
			byte[] rawData = new byte[width * height * 4];
			Color32[] colours = new Color32[4];
			int blockOffset = 0;
			for (int y = 0; y < height; y += 4)
			{
				for (int x = 0; x < width; x += 4)
				{
					if (blockOffset + 8 > data.Length) throw new InvalidDataException("DDS DXT1 block truncated.");
					ushort colour0 = ReadUInt16(data, blockOffset);
					ushort colour1 = ReadUInt16(data, blockOffset + 2);
					uint bitmask = ReadUInt32(data, blockOffset + 4);
					DecodeDxtColors(colour0, colour1, true, colours);
					blockOffset += 8;
					for (int j = 0, k = 0; j < 4; j++)
					{
						int py = y + j;
						if (py >= height)
						{
							k += 4;
							continue;
						}
						int rowOff = py * bps;
						for (int i = 0; i < 4; i++, k++)
						{
							int px = x + i;
							if (px >= width) continue;
							Color32 col = colours[(int)((bitmask >> (k * 2)) & 3)];
							int offset = rowOff + px * 4;
							rawData[offset] = col.R;
							rawData[offset + 1] = col.G;
							rawData[offset + 2] = col.B;
							rawData[offset + 3] = col.A;
						}
					}
				}
			}
			return rawData;
		}

		private byte[] DecompressDXT2(DdsHeader header, byte[] data)
		{
			byte[] rawData = DecompressDXT3(header, data);
			CorrectPremult((uint)(header.width * header.height), rawData);
			return rawData;
		}

		private byte[] DecompressDXT3(DdsHeader header, byte[] data)
		{
			int width = header.width;
			int height = header.height;
			int bps = width * 4;
			byte[] rawData = new byte[width * height * 4];
			Color32[] colours = new Color32[4];
			int blockOffset = 0;
			for (int y = 0; y < height; y += 4)
			{
				for (int x = 0; x < width; x += 4)
				{
					if (blockOffset + 16 > data.Length) throw new InvalidDataException("DDS DXT3 block truncated.");
					DecodeDxtColors(ReadUInt16(data, blockOffset + 8), ReadUInt16(data, blockOffset + 10), false, colours);
					uint bitmask = ReadUInt32(data, blockOffset + 12);
					for (int j = 0, k = 0; j < 4; j++)
					{
						int py = y + j;
						ushort word = ReadUInt16(data, blockOffset + 2 * j);
						for (int i = 0; i < 4; k++, i++)
						{
							int px = x + i;
							byte a4 = (byte)(word & 0x0F);
							a4 = (byte)(a4 | (a4 << 4));
							word >>= 4;
							if (px >= width || py >= height) continue;
							Color32 col = colours[(int)((bitmask >> (k * 2)) & 3)];
							int offset = py * bps + px * 4;
							rawData[offset] = col.R;
							rawData[offset + 1] = col.G;
							rawData[offset + 2] = col.B;
							rawData[offset + 3] = a4;
						}
					}
					blockOffset += 16;
				}
			}
			return rawData;
		}

		private byte[] DecompressDXT4(DdsHeader header, byte[] data)
		{
			byte[] rawData = DecompressDXT5(header, data);
			CorrectPremult((uint)(header.width * header.height), rawData);
			return rawData;
		}

		private byte[] DecompressDXT5(DdsHeader header, byte[] data)
		{
			int width = header.width;
			int height = header.height;
			int bps = width * 4;
			byte[] rawData = new byte[width * height * 4];
			Color32[] colours = new Color32[4];
			byte[] alphas = new byte[8];
			int blockOffset = 0;
			for (int y = 0; y < height; y += 4)
			{
				for (int x = 0; x < width; x += 4)
				{
					if (blockOffset + 16 > data.Length) throw new InvalidDataException("DDS DXT5 block truncated.");
					alphas[0] = data[blockOffset];
					alphas[1] = data[blockOffset + 1];
					ulong alphaBits = ReadUInt48(data, blockOffset + 2);
					DecodeDxtColors(ReadUInt16(data, blockOffset + 8), ReadUInt16(data, blockOffset + 10), false, colours);
					uint bitmask = ReadUInt32(data, blockOffset + 12);
					if (alphas[0] > alphas[1])
					{
						alphas[2] = (byte)((6 * alphas[0] + alphas[1] + 3) / 7);
						alphas[3] = (byte)((5 * alphas[0] + 2 * alphas[1] + 3) / 7);
						alphas[4] = (byte)((4 * alphas[0] + 3 * alphas[1] + 3) / 7);
						alphas[5] = (byte)((3 * alphas[0] + 4 * alphas[1] + 3) / 7);
						alphas[6] = (byte)((2 * alphas[0] + 5 * alphas[1] + 3) / 7);
						alphas[7] = (byte)((alphas[0] + 6 * alphas[1] + 3) / 7);
					}
					else
					{
						alphas[2] = (byte)((4 * alphas[0] + alphas[1] + 2) / 5);
						alphas[3] = (byte)((3 * alphas[0] + 2 * alphas[1] + 2) / 5);
						alphas[4] = (byte)((2 * alphas[0] + 3 * alphas[1] + 2) / 5);
						alphas[5] = (byte)((alphas[0] + 4 * alphas[1] + 2) / 5);
						alphas[6] = 0;
						alphas[7] = 255;
					}
					for (int j = 0; j < 4; j++)
					{
						int py = y + j;
						for (int i = 0; i < 4; i++)
						{
							int px = x + i;
							if (px >= width || py >= height) continue;
							int index = j * 4 + i;
							Color32 col = colours[(int)((bitmask >> (index * 2)) & 3)];
							int offset = py * bps + px * 4;
							rawData[offset] = col.R;
							rawData[offset + 1] = col.G;
							rawData[offset + 2] = col.B;
							rawData[offset + 3] = alphas[(int)((alphaBits >> (index * 3)) & 7)];
						}
					}
					blockOffset += 16;
				}
			}
			return rawData;
		}

		private unsafe byte[] DecompressUncompressed(DdsHeader header, byte[] data, PixelFormat pixelFormat, int srcPitch)
		{
			int width = header.width;
			int height = header.height;
			byte[] rawData = new byte[width * height * 4];

			uint rMask = header.pixelFormat.rBitmask;
			uint gMask = header.pixelFormat.gBitmask;
			uint bMask = header.pixelFormat.bBitmask;
			uint aMask = header.pixelFormat.alphaBitmask;

			int rShift, rBits, gShift, gBits, bShift, bBits, aShift, aBits;
			DecodeMask(rMask, out rShift, out rBits);
			DecodeMask(gMask, out gShift, out gBits);
			DecodeMask(bMask, out bShift, out bBits);
			DecodeMask(aMask, out aShift, out aBits);

			int bits = header.pixelFormat.rgbBitCount;
			if (bits <= 0) bits = 32;
			int pixSize = (bits + 7) / 8;
			if (pixSize <= 0 || pixSize > 8) throw new InvalidDataException("DDS bit count invalid.");
			long minRowBytes = ((long)width * bits + 7L) / 8L;
			if (srcPitch < minRowBytes || (long)srcPitch * (height - 1) + minRowBytes > data.Length)
			{
				throw new InvalidDataException("DDS pitch/data size invalid.");
			}
			if (pixSize >= 2 && (srcPitch & 1) != 0)
			{
				throw new InvalidDataException("DDS pitch is not aligned.");
			}

			bool isLum = pixelFormat == PixelFormat.LUMINANCE || pixelFormat == PixelFormat.LUMINANCE_ALPHA || pixelFormat == PixelFormat.R8;
			bool isRG = pixelFormat == PixelFormat.RG8;
			bool isR16 = pixelFormat == PixelFormat.R16;
			bool isRG16 = pixelFormat == PixelFormat.RG16;
			bool isA8 = pixelFormat == PixelFormat.A8;
			bool isR16G16B16A16 = pixelFormat == PixelFormat.R16G16B16A16;

			fixed (byte* srcBase = data)
			{
				fixed (byte* dstBase = rawData)
				{
					if (isR16)
					{
						for (int y = 0; y < height; y++)
						{
							byte* srcRow = srcBase + y * srcPitch;
							byte* dstRow = dstBase + y * width * 4;
							for (int x = 0; x < width; x++)
							{
								int offset = x * 2;
								byte value = srcRow[offset + 1];
								dstRow[x * 4 + 0] = value;
								dstRow[x * 4 + 1] = value;
								dstRow[x * 4 + 2] = value;
								dstRow[x * 4 + 3] = 255;
							}
						}
						return rawData;
					}

					if (isRG16)
					{
						for (int y = 0; y < height; y++)
						{
							byte* srcRow = srcBase + y * srcPitch;
							byte* dstRow = dstBase + y * width * 4;
							for (int x = 0; x < width; x++)
							{
								int offset = x * 4;
								dstRow[x * 4 + 0] = srcRow[offset + 1];
								dstRow[x * 4 + 1] = srcRow[offset + 3];
								dstRow[x * 4 + 2] = 0;
								dstRow[x * 4 + 3] = 255;
							}
						}
						return rawData;
					}

					if (isR16G16B16A16)
					{
						// 64-bit: 4x16-bit UNORM -> 8-bit
						for (int y = 0; y < height; y++)
						{
							byte* srcRow = srcBase + y * srcPitch;
							byte* dstRow = dstBase + y * width * 4;
							for (int x = 0; x < width; x++)
							{
								byte* p = srcRow + x * 8;
								dstRow[x * 4 + 0] = p[1];
								dstRow[x * 4 + 1] = p[3];
								dstRow[x * 4 + 2] = p[5];
								dstRow[x * 4 + 3] = p[7];
							}
						}
						return rawData;
					}

					if (isA8)
					{
						for (int y = 0; y < height; y++)
						{
							byte* srcRow = srcBase + y * srcPitch;
							byte* dstRow = dstBase + y * width * 4;
							for (int x = 0; x < width; x++)
							{
								byte a = srcRow[x];
								dstRow[x * 4 + 0] = 255;
								dstRow[x * 4 + 1] = 255;
								dstRow[x * 4 + 2] = 255;
								dstRow[x * 4 + 3] = a;
							}
						}
						return rawData;
					}

					if (isLum)
					{
						bool hasAlpha = pixelFormat == PixelFormat.LUMINANCE_ALPHA;
						for (int y = 0; y < height; y++)
						{
							byte* srcRow = srcBase + y * srcPitch;
							byte* dstRow = dstBase + y * width * 4;
							for (int x = 0; x < width; x++)
							{
								byte l, a = 255;
								if (!hasAlpha)
								{
									if (pixSize == 1)
									{
										uint px = srcRow[x];
										l = ScaleChannel(px, rMask != 0 ? rMask : 0xFFU, rMask != 0 ? rShift : 0, rMask != 0 ? rBits : 8);
									}
									else if (pixSize == 2)
									{
										uint px = (uint)(srcRow[x * 2] | (srcRow[x * 2 + 1] << 8));
										l = ScaleChannel(px, rMask != 0 ? rMask : 0xFFFFU, rMask != 0 ? rShift : 0, rMask != 0 ? rBits : 16);
									}
									else
									{
										uint px = 0;
										for (int b = 0; b < pixSize && b < 4; b++) px |= (uint)srcRow[x * pixSize + b] << (b * 8);
										l = ScaleChannel(px, rMask, rShift, rBits);
									}
								}
								else
								{
									// A8L8 etc: 2 bytes
									uint px;
									if (pixSize == 2)
									{
										px = (uint)(srcRow[x * 2] | (srcRow[x * 2 + 1] << 8));
									}
									else
									{
										px = 0;
										for (int b = 0; b < pixSize && b < 4; b++) px |= (uint)srcRow[x * pixSize + b] << (b * 8);
									}
									l = ScaleChannel(px, rMask, rShift, rBits);
									a = ScaleChannel(px, aMask, aShift, aBits);
								}
								dstRow[x * 4 + 0] = l;
								dstRow[x * 4 + 1] = l;
								dstRow[x * 4 + 2] = l;
								dstRow[x * 4 + 3] = a;
							}
						}
						return rawData;
					}

					if (isRG)
					{
						for (int y = 0; y < height; y++)
						{
							byte* srcRow = srcBase + y * srcPitch;
							byte* dstRow = dstBase + y * width * 4;
							for (int x = 0; x < width; x++)
							{
								uint px = (uint)(srcRow[x * 2] | (srcRow[x * 2 + 1] << 8));
								dstRow[x * 4 + 0] = ScaleChannel(px, rMask, rShift, rBits);
								dstRow[x * 4 + 1] = ScaleChannel(px, gMask, gShift, gBits);
								dstRow[x * 4 + 2] = 0;
								dstRow[x * 4 + 3] = 255;
							}
						}
						return rawData;
					}

					// Generic RGB / RGBA (masks already describe channel positions)
					bool hasAlphaMask = aMask != 0;
					for (int y = 0; y < height; y++)
					{
						byte* srcRow = srcBase + y * srcPitch;
						byte* dstRow = dstBase + y * width * 4;
						for (int x = 0; x < width; x++)
						{
							uint px;
							byte* p = srcRow + x * pixSize;
							switch (pixSize)
							{
								case 1:
									px = p[0];
									break;
								case 2:
									px = (uint)(p[0] | (p[1] << 8));
									break;
								case 3:
									px = (uint)(p[0] | (p[1] << 8) | (p[2] << 16));
									break;
								default:
									px = (uint)(p[0] | (p[1] << 8) | (p[2] << 16) | (p[3] << 24));
									break;
							}
							dstRow[x * 4 + 0] = rBits == 0 ? (byte)0 : ScaleChannel(px, rMask, rShift, rBits);
							dstRow[x * 4 + 1] = gBits == 0 ? (byte)0 : ScaleChannel(px, gMask, gShift, gBits);
							dstRow[x * 4 + 2] = bBits == 0 ? (byte)0 : ScaleChannel(px, bMask, bShift, bBits);
							if (pixelFormat == PixelFormat.RGB)
							{
								dstRow[x * 4 + 3] = 255;
							}
							else
							{
								dstRow[x * 4 + 3] = !hasAlphaMask ? (byte)255 : ScaleChannel(px, aMask, aShift, aBits);
							}
						}
					}
				}
			}
			return rawData;
		}

		private static byte Interpolate8(int a, int b, int index)
		{
			int weight = index - 1;
			return (byte)(((7 - weight) * a + weight * b + 3) / 7);
		}

		private static byte Interpolate6(int a, int b, int index)
		{
			int weight = index - 1;
			return (byte)(((5 - weight) * a + weight * b + 2) / 5);
		}

		private static void BuildAlphaPalette(byte[] data, int offset, byte[] palette)
		{
			int first = data[offset];
			int second = data[offset + 1];
			palette[0] = (byte)first;
			palette[1] = (byte)second;
			if (first > second)
			{
				for (int i = 2; i < 8; i++) palette[i] = Interpolate8(first, second, i);
			}
			else
			{
				for (int i = 2; i < 6; i++) palette[i] = Interpolate6(first, second, i);
				palette[6] = 0;
				palette[7] = 255;
			}
		}

		private byte[] Decompress3Dc(DdsHeader header, byte[] data, bool reconstructZ)
		{
			int width = header.width;
			int height = header.height;
			int bps = width * 4;
			byte[] rawData = new byte[width * height * 4];
			byte[] rPalette = new byte[8];
			byte[] gPalette = new byte[8];
			int blockOffset = 0;
			for (int by = 0; by < height; by += 4)
			{
				for (int bx = 0; bx < width; bx += 4)
				{
					if (blockOffset + 16 > data.Length) throw new InvalidDataException("DDS BC5 block truncated.");
					BuildAlphaPalette(data, blockOffset, rPalette);
					BuildAlphaPalette(data, blockOffset + 8, gPalette);
					ulong rIndices = ReadUInt64(data, blockOffset) >> 16;
					ulong gIndices = ReadUInt64(data, blockOffset + 8) >> 16;
					for (int t = 0; t < 16; t++)
					{
						int py = by + t / 4;
						int px = bx + t % 4;
						int rIndex = (int)(rIndices & 7);
						int gIndex = (int)(gIndices & 7);
						rIndices >>= 3;
						gIndices >>= 3;
						if (px >= width || py >= height) continue;
						byte r = rPalette[rIndex];
						byte g = gPalette[gIndex];
						int offset = py * bps + px * 4;
						rawData[offset] = r;
						rawData[offset + 1] = g;
						if (reconstructZ)
						{
							int tb = 127 * 128 - (r - 127) * (r - 128) - (g - 127) * (g - 128);
							rawData[offset + 2] = tb > 0 ? (byte)(Math.Sqrt(tb) + 128) : (byte)0x7F;
						}
						else
						{
							rawData[offset + 2] = 0;
						}
						rawData[offset + 3] = 255;
					}
					blockOffset += 16;
				}
			}
			return rawData;
		}

		private byte[] DecompressAti1n(DdsHeader header, byte[] data)
		{
			int width = header.width;
			int height = header.height;
			int bps = width * 4;
			byte[] rawData = new byte[width * height * 4];
			byte[] palette = new byte[8];
			int blockOffset = 0;
			for (int by = 0; by < height; by += 4)
			{
				for (int bx = 0; bx < width; bx += 4)
				{
					if (blockOffset + 8 > data.Length) throw new InvalidDataException("DDS BC4 block truncated.");
					BuildAlphaPalette(data, blockOffset, palette);
					ulong indices = ReadUInt64(data, blockOffset) >> 16;
					for (int t = 0; t < 16; t++)
					{
						int py = by + t / 4;
						int px = bx + t % 4;
						byte value = palette[(int)(indices & 7)];
						indices >>= 3;
						if (px >= width || py >= height) continue;
						int offset = py * bps + px * 4;
						rawData[offset] = value;
						rawData[offset + 1] = value;
						rawData[offset + 2] = value;
						rawData[offset + 3] = 255;
					}
					blockOffset += 8;
				}
			}
			return rawData;
		}

		private byte[] DecompressRXGB(DdsHeader header, byte[] data)
		{
			// RXGB swaps red and alpha; a shader may reconstruct normal Z from X/Y.
			byte[] rawData = DecompressDXT5(header, data);
			for (int i = 0; i < rawData.Length; i += 4)
			{
				byte r = rawData[i];
				rawData[i] = rawData[i + 3];
				rawData[i + 3] = r;
			}
			return rawData;
		}

		private unsafe byte[] DecompressFloat(DdsHeader header, byte[] data, PixelFormat pixelFormat, int srcPitch)
		{
			int width = header.width;
			int height = header.height;
			int pixels = width * height;
			byte[] rawData = new byte[pixels * 4];

			int bytesPerPixel;
			switch (pixelFormat)
			{
				case PixelFormat.R32F:
				case PixelFormat.R32_FLOAT:
					bytesPerPixel = 4;
					break;
				case PixelFormat.G32R32F:
					bytesPerPixel = 8;
					break;
				case PixelFormat.A32B32G32R32F:
					bytesPerPixel = 16;
					break;
				case PixelFormat.R16F:
					bytesPerPixel = 2;
					break;
				case PixelFormat.G16R16F:
					bytesPerPixel = 4;
					break;
				case PixelFormat.A16B16G16R16F:
				case PixelFormat.A16B16G16R16:
					bytesPerPixel = 8;
					break;
				default:
					throw new NotImplementedException("Decompression of PixelFormat " + pixelFormat + " has not been implemented in this plugin.");
			}

			int requiredAlignment = bytesPerPixel >= 4 ? 4 : 2;
			RequireNativeAlignment(data, requiredAlignment);
			int rowBytes = width * bytesPerPixel;
			// Legacy FourCC paths pass srcPitch == 0 (tightly packed); DX10 honors file pitch.
			if (srcPitch > 0 && srcPitch < rowBytes)
			{
				throw new InvalidDataException("DDS float pitch invalid.");
			}
			if (srcPitch > 0 && (srcPitch & (requiredAlignment - 1)) != 0)
			{
				throw new InvalidDataException("DDS float pitch is not aligned.");
			}
			int stride = srcPitch > 0 ? srcPitch : rowBytes;
			if ((long)stride * (height - 1) + rowBytes > data.Length)
			{
				throw new InvalidDataException("DDS float data truncated.");
			}

			fixed (byte* srcBase = data)
			{
				fixed (byte* dstBase = rawData)
				{
					switch (pixelFormat)
					{
						case PixelFormat.R32F:  // Red float, green = blue = red (grayscale)
						case PixelFormat.R32_FLOAT:
							{
								for (int y = 0; y < height; y++)
								{
									float* srcRow = (float*)(srcBase + y * stride);
									byte* dstRow = dstBase + y * width * 4;
									for (int x = 0; x < width; x++)
									{
										byte v = FloatToByte(srcRow[x]);
										dstRow[x * 4 + 0] = v;
										dstRow[x * 4 + 1] = v;
										dstRow[x * 4 + 2] = v;
										dstRow[x * 4 + 3] = 255;
									}
								}
							}
							break;
						case PixelFormat.A32B32G32R32F:  // RGBA float
							{
								for (int y = 0; y < height; y++)
								{
									float* srcRow = (float*)(srcBase + y * stride);
									byte* dstRow = dstBase + y * width * 4;
									for (int x = 0; x < width; x++)
									{
										dstRow[x * 4 + 0] = FloatToByte(srcRow[x * 4 + 0]);
										dstRow[x * 4 + 1] = FloatToByte(srcRow[x * 4 + 1]);
										dstRow[x * 4 + 2] = FloatToByte(srcRow[x * 4 + 2]);
										dstRow[x * 4 + 3] = FloatToByte(srcRow[x * 4 + 3]);
									}
								}
							}
							break;
						case PixelFormat.G32R32F:  // Red + green float
							{
								for (int y = 0; y < height; y++)
								{
									float* srcRow = (float*)(srcBase + y * stride);
									byte* dstRow = dstBase + y * width * 4;
									for (int x = 0; x < width; x++)
									{
										dstRow[x * 4 + 0] = FloatToByte(srcRow[x * 2 + 0]);
										dstRow[x * 4 + 1] = FloatToByte(srcRow[x * 2 + 1]);
										dstRow[x * 4 + 2] = 0;
										dstRow[x * 4 + 3] = 255;
									}
								}
							}
							break;
						case PixelFormat.R16F:  // Red half -> grayscale
							{
								for (int y = 0; y < height; y++)
								{
									ushort* srcRow = (ushort*)(srcBase + y * stride);
									byte* dstRow = dstBase + y * width * 4;
									for (int x = 0; x < width; x++)
									{
										byte v = FloatToByte(HalfToFloatF(srcRow[x]));
										dstRow[x * 4 + 0] = v;
										dstRow[x * 4 + 1] = v;
										dstRow[x * 4 + 2] = v;
										dstRow[x * 4 + 3] = 255;
									}
								}
							}
							break;
						case PixelFormat.A16B16G16R16F:  // RGBA half
							{
								for (int y = 0; y < height; y++)
								{
									ushort* srcRow = (ushort*)(srcBase + y * stride);
									byte* dstRow = dstBase + y * width * 4;
									for (int x = 0; x < width; x++)
									{
										dstRow[x * 4 + 0] = FloatToByte(HalfToFloatF(srcRow[x * 4 + 0]));
										dstRow[x * 4 + 1] = FloatToByte(HalfToFloatF(srcRow[x * 4 + 1]));
										dstRow[x * 4 + 2] = FloatToByte(HalfToFloatF(srcRow[x * 4 + 2]));
										dstRow[x * 4 + 3] = FloatToByte(HalfToFloatF(srcRow[x * 4 + 3]));
									}
								}
							}
							break;
						case PixelFormat.G16R16F:  // Red + green half
							{
								for (int y = 0; y < height; y++)
								{
									ushort* srcRow = (ushort*)(srcBase + y * stride);
									byte* dstRow = dstBase + y * width * 4;
									for (int x = 0; x < width; x++)
									{
										dstRow[x * 4 + 0] = FloatToByte(HalfToFloatF(srcRow[x * 2 + 0]));
										dstRow[x * 4 + 1] = FloatToByte(HalfToFloatF(srcRow[x * 2 + 1]));
										dstRow[x * 4 + 2] = 0;
										dstRow[x * 4 + 3] = 255;
									}
								}
							}
							break;
						case PixelFormat.A16B16G16R16:  // 4x16-bit UNORM integer
							{
								for (int y = 0; y < height; y++)
								{
									ushort* srcRow = (ushort*)(srcBase + y * stride);
									byte* dstRow = dstBase + y * width * 4;
									for (int x = 0; x < width; x++)
									{
										dstRow[x * 4 + 0] = (byte)(srcRow[x * 4 + 0] >> 8);
										dstRow[x * 4 + 1] = (byte)(srcRow[x * 4 + 1] >> 8);
										dstRow[x * 4 + 2] = (byte)(srcRow[x * 4 + 2] >> 8);
										dstRow[x * 4 + 3] = (byte)(srcRow[x * 4 + 3] >> 8);
									}
								}
							}
							break;
					}
				}
			}

			return rawData;
		}

		private const uint DDSD_PITCH = 0x00000008;

		private const uint DDPF_ALPHAPIXELS = 0x00000001;
		private const uint DDPF_ALPHA = 0x00000002;
		private const uint DDPF_FOURCC = 0x00000004;
		private const uint DDPF_RGB = 0x00000040;
		private const uint DDPF_YUV = 0x00000200;
		private const uint DDPF_LUMINANCE = 0x00020000;
		private const uint DDPF_PALETTEINDEXED8 = 0x00000020;
	}
}
