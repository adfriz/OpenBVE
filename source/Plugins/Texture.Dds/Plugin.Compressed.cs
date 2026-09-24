using System;
using System.IO;
using OpenBveApi.Textures;

namespace Texture.Dds {
	public partial class DDSImage {
		private bool CanUseCompressedTexture(DdsHeader header, PixelFormat pixelFormat) {
			if ((header.depth != 1) || (header.Dx10ResourceDimension != 0 && header.Dx10ResourceDimension != 3)) return false;
			if ((header.ddscaps.caps2 & (DdsHeader.DdsCaps2Cubemap | DdsHeader.DdsCaps2Volume)) != 0) return false;
			if (header.IsDx10 && (header.Dx10ArraySize != 1 || (header.Dx10MiscFlag & DdsHeader.DdsResourceMiscTextureCube) != 0)) return false;
			CompressedTextureFormat format = GetCompressedFormat(header, pixelFormat);
			TextureCapabilities required = GetRequiredCapabilities(format);
			return format != CompressedTextureFormat.None && required != TextureCapabilities.None && (capabilities & required) == required;
		}

		private static CompressedTextureFormat GetCompressedFormat(DdsHeader header, PixelFormat pixelFormat) {
			// DXT2/DXT4, RXGB, and ATI2/3DC require CPU semantic fixes and stay off the direct path.
			if (pixelFormat == PixelFormat.DXT1 && header.IsDx10 && (header.Dx10MiscFlags2 & DdsHeader.DdsMiscFlags2AlphaModeMask) == DdsHeader.DdsAlphaModeOpaque) {
				return CompressedTextureFormat.Bc1Rgb;
			}
			switch (pixelFormat) {
				case PixelFormat.DXT1: return CompressedTextureFormat.Bc1Rgba;
				case PixelFormat.DXT3: return CompressedTextureFormat.Bc2Rgba;
				case PixelFormat.DXT5: return CompressedTextureFormat.Bc3Rgba;
				case PixelFormat.ATI1N:
				case PixelFormat.R8_BC4: return CompressedTextureFormat.Bc4R;
				case PixelFormat.RG8_BC5: return CompressedTextureFormat.Bc5Rg;
				case PixelFormat.BC7: return CompressedTextureFormat.Bc7Unorm;
				default: return CompressedTextureFormat.None;
			}
		}

		private static TextureCapabilities GetRequiredCapabilities(CompressedTextureFormat format) {
			return CompressedTextureData.GetRequiredCapabilities(format);
		}

		private static int GetCompressedMipCount(DdsHeader header) {
			uint declared = header.mipmapCount == 0 ? 1U : header.mipmapCount;
			int maximum = 1;
			int width = header.width;
			int height = header.height;
			while (width > 1 || height > 1) {
				width = Math.Max(1, width >> 1);
				height = Math.Max(1, height >> 1);
				maximum++;
			}
			if (declared > (uint)maximum) throw new InvalidDataException("DDS mip level count is invalid.");
			return (int)declared;
		}

		private CompressedTextureData ReadCompressedMipChain(BinaryReader reader, DdsHeader header, PixelFormat pixelFormat) {
			CompressedTextureFormat format = GetCompressedFormat(header, pixelFormat);
			int blockBytes = CompressedTextureData.GetBlockBytes(format);
			long topLevelBytes = (long)((header.width + 3) / 4) * ((header.height + 3) / 4) * blockBytes;
			if ((header.flags & DdsHeader.DdsSdLinearSize) != 0 && header.sizeOrPitch != 0 && header.sizeOrPitch != topLevelBytes) {
				throw new InvalidDataException("DDS compressed top-level size is invalid.");
			}
			int mipCount = GetCompressedMipCount(header);
			CompressedTextureMip[] mips = new CompressedTextureMip[mipCount];
			long total = 0;
			for (int level = 0; level < mipCount; level++) {
				int width = Math.Max(1, header.width >> level);
				int height = Math.Max(1, header.height >> level);
				long levelBytes = (long)((width + 3) / 4) * ((height + 3) / 4) * blockBytes;
				if (levelBytes <= 0 || levelBytes > int.MaxValue || total > MaxInputBytes - levelBytes) {
					throw new InvalidDataException("DDS compressed mip payload is too large.");
				}
				mips[level] = new CompressedTextureMip(width, height, (int)total, (int)levelBytes);
				total += levelBytes;
			}
			if (total <= 0 || total > MaxInputBytes || total > int.MaxValue) throw new InvalidDataException("DDS compressed mip payload is invalid.");
			Stream stream = reader.BaseStream;
			if (stream.CanSeek && stream.Length - stream.Position < total) throw new InvalidDataException("DDS file truncated.");
			byte[] data = new byte[(int)total];
			for (int level = 0; level < mipCount; level++) {
				ReadCompressedLevel(reader, data, mips[level].Offset, mips[level].Length);
			}
			return new CompressedTextureData(header.width, header.height, format, false, 1, mips, data);
		}

		private static void ReadCompressedLevel(BinaryReader reader, byte[] data, int offset, int count) {
			int readOffset = 0;
			while (readOffset < count) {
				int read = reader.Read(data, offset + readOffset, count - readOffset);
				if (read == 0) throw new InvalidDataException("DDS file truncated.");
				readOffset += read;
			}
		}

		private TextureTransparencyType GetCompressedTransparency(DdsHeader header, PixelFormat pixelFormat) {
			if (header.IsDx10 && (header.Dx10MiscFlags2 & DdsHeader.DdsMiscFlags2AlphaModeMask) == DdsHeader.DdsAlphaModeOpaque) {
				return TextureTransparencyType.Opaque;
			}
			switch (pixelFormat) {
				case PixelFormat.DXT1:
				case PixelFormat.DXT3:
				case PixelFormat.DXT5:
				case PixelFormat.BC7:
					return TextureTransparencyType.Alpha;
				default:
					return TextureTransparencyType.Opaque;
			}
		}

		private sealed class DdsCompressedTextureDecoder : ICompressedTextureDecoder {
			private readonly DDSImage owner;
			private readonly DdsHeader header;
			private readonly PixelFormat pixelFormat;
			private readonly int srcPitch;

			public DdsCompressedTextureDecoder(DDSImage owner, DdsHeader header, PixelFormat pixelFormat, int srcPitch) {
				this.owner = owner;
				this.header = header;
				this.pixelFormat = pixelFormat;
				this.srcPitch = srcPitch;
			}

			public byte[] DecodeToRgba(CompressedTextureData data) {
				if (data == null || data.MipLevels == null || data.MipLevels.Length == 0) throw new InvalidDataException("Compressed DDS payload is missing.");
				CompressedTextureMip top = data.MipLevels[0];
				byte[] topData = new byte[top.Length];
				Buffer.BlockCopy(data.Data, top.Offset, topData, 0, top.Length);
				byte[] rgba = owner.DecompressData(header, topData, pixelFormat, srcPitch);
				if (header.IsDx10 && (header.Dx10MiscFlags2 & DdsHeader.DdsMiscFlags2AlphaModeMask) == DdsHeader.DdsAlphaModeOpaque)
				{
					for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
				}
				return rgba;
			}
		}
	}
}
