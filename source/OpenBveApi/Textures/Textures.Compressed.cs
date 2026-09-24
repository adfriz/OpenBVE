using System;

namespace OpenBveApi.Textures {
	/// <summary>Describes compressed texture support exposed by a host.</summary>
	[Flags]
	public enum TextureCapabilities {
		/// <summary>No compressed texture support is available.</summary>
		None = 0,
		/// <summary>BC1/DXT1 compressed textures are supported.</summary>
		BC1 = 1,
		/// <summary>BC2/DXT3 compressed textures are supported.</summary>
		BC2 = 2,
		/// <summary>BC3/DXT5 compressed textures are supported.</summary>
		BC3 = 4,
		/// <summary>BC4/RGTC1 compressed textures are supported.</summary>
		BC4 = 8,
		/// <summary>BC5/RGTC2 compressed textures are supported.</summary>
		BC5 = 16,
		/// <summary>BC7/BPTC compressed textures are supported.</summary>
		BC7 = 32
	}

	/// <summary>Describes a renderer-independent compressed texture format.</summary>
	public enum CompressedTextureFormat {
		/// <summary>No compressed format.</summary>
		None = 0,
		/// <summary>BC1 with an RGB internal format.</summary>
		Bc1Rgb = 1,
		/// <summary>BC1 with an RGBA internal format.</summary>
		Bc1Rgba = 2,
		/// <summary>BC2 with an RGBA internal format.</summary>
		Bc2Rgba = 3,
		/// <summary>BC3 with an RGBA internal format.</summary>
		Bc3Rgba = 4,
		/// <summary>BC4 with a red internal format.</summary>
		Bc4R = 5,
		/// <summary>BC5 with a red/green internal format.</summary>
		Bc5Rg = 6,
		/// <summary>BC7 with a UNORM internal format.</summary>
		Bc7Unorm = 7,
		/// <summary>BC7 with an sRGB internal format.</summary>
		Bc7Srgb = 8
	}

	/// <summary>Describes one level of a compressed texture.</summary>
	public sealed class CompressedTextureMip {
		/// <summary>Gets the level width.</summary>
		public int Width { get; private set; }
		/// <summary>Gets the level height.</summary>
		public int Height { get; private set; }
		/// <summary>Gets the byte offset in the containing compressed buffer.</summary>
		public int Offset { get; private set; }
		/// <summary>Gets the byte length of this level.</summary>
		public int Length { get; private set; }

		/// <summary>Creates a compressed mip description.</summary>
		public CompressedTextureMip(int width, int height, int offset, int length) {
			if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
			if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
			if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
			if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
			Width = width;
			Height = height;
			Offset = offset;
			Length = length;
		}
	}

	/// <summary>Contains the complete compressed payload for a texture resource.</summary>
	public sealed class CompressedTextureData {
		/// <summary>Gets the base width.</summary>
		public int Width { get; private set; }
		/// <summary>Gets the base height.</summary>
		public int Height { get; private set; }
		/// <summary>Gets the compressed format.</summary>
		public CompressedTextureFormat Format { get; private set; }
		/// <summary>Gets whether the resource is an sRGB resource.</summary>
		public bool IsSrgb { get; private set; }
		/// <summary>Gets the number of surfaces; initially only one 2D surface is supported.</summary>
		public int SurfaceCount { get; private set; }
		/// <summary>Gets the number of bytes in one compressed block.</summary>
		public int BlockBytes { get; private set; }
		/// <summary>Gets the contiguous compressed payload.</summary>
		public byte[] Data { get; private set; }
		/// <summary>Gets the mip levels, ordered from level zero.</summary>
		public CompressedTextureMip[] MipLevels { get; private set; }

		/// <summary>Gets the host capability required for a compressed format.</summary>
		public static TextureCapabilities GetRequiredCapabilities(CompressedTextureFormat format) {
			switch (format) {
				case CompressedTextureFormat.Bc1Rgb:
				case CompressedTextureFormat.Bc1Rgba: return TextureCapabilities.BC1;
				case CompressedTextureFormat.Bc2Rgba: return TextureCapabilities.BC2;
				case CompressedTextureFormat.Bc3Rgba: return TextureCapabilities.BC3;
				case CompressedTextureFormat.Bc4R: return TextureCapabilities.BC4;
				case CompressedTextureFormat.Bc5Rg: return TextureCapabilities.BC5;
				case CompressedTextureFormat.Bc7Unorm:
				case CompressedTextureFormat.Bc7Srgb: return TextureCapabilities.BC7;
				default: return TextureCapabilities.None;
			}
		}

		/// <summary>Gets the number of bytes in one block for a compressed format.</summary>
		public static int GetBlockBytes(CompressedTextureFormat format) {
			switch (format) {
				case CompressedTextureFormat.Bc1Rgb:
				case CompressedTextureFormat.Bc1Rgba:
				case CompressedTextureFormat.Bc4R: return 8;
				case CompressedTextureFormat.Bc2Rgba:
				case CompressedTextureFormat.Bc3Rgba:
				case CompressedTextureFormat.Bc5Rg:
				case CompressedTextureFormat.Bc7Unorm:
				case CompressedTextureFormat.Bc7Srgb: return 16;
				default: throw new ArgumentOutOfRangeException(nameof(format));
			}
		}

		/// <summary>Gets the number of supplied mip levels.</summary>
		public int LevelCount => MipLevels.Length;

		/// <summary>Gets whether the supplied levels form a complete chain down to 1x1.</summary>
		public bool HasCompleteMipChain {
			get {
				int expectedWidth = Width;
				int expectedHeight = Height;
				for (int i = 0; i < MipLevels.Length; i++) {
					CompressedTextureMip mip = MipLevels[i];
					if (mip == null || mip.Width != expectedWidth || mip.Height != expectedHeight) return false;
					if (expectedWidth == 1 && expectedHeight == 1) return i == MipLevels.Length - 1;
					expectedWidth = System.Math.Max(1, expectedWidth >> 1);
					expectedHeight = System.Math.Max(1, expectedHeight >> 1);
				}
				return false;
			}
		}

		/// <summary>Creates a compressed texture payload.</summary>
		public CompressedTextureData(int width, int height, CompressedTextureFormat format, bool isSrgb,
			int surfaceCount, CompressedTextureMip[] mipLevels, byte[] data) {
			if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
			if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
			if (format == CompressedTextureFormat.None) throw new ArgumentException("A compressed format is required.", nameof(format));
			if (surfaceCount != 1) throw new ArgumentOutOfRangeException(nameof(surfaceCount), "Only one compressed surface is currently supported.");
			if (mipLevels == null || mipLevels.Length == 0) throw new ArgumentException("At least one mip level is required.", nameof(mipLevels));
			if (data == null) throw new ArgumentNullException(nameof(data));
			if (mipLevels[0] == null || mipLevels[0].Width != width || mipLevels[0].Height != height) throw new ArgumentException("The first mip must match the base dimensions.", nameof(mipLevels));
			int previousEnd = 0;
			int previousWidth = int.MaxValue;
			int previousHeight = int.MaxValue;
			for (int i = 0; i < mipLevels.Length; i++) {
				CompressedTextureMip mip = mipLevels[i];
				if (mip == null || mip.Length <= 0 || mip.Offset < 0 || mip.Offset > data.Length - mip.Length || mip.Offset != previousEnd || mip.Width > previousWidth || mip.Height > previousHeight) {
					throw new ArgumentException("A compressed mip level is outside the payload.", nameof(mipLevels));
				}
				long expected = (long)((mip.Width + 3) / 4) * ((mip.Height + 3) / 4) * GetBlockBytes(format);
				if (expected != mip.Length) throw new ArgumentException("A compressed mip level has an invalid length.", nameof(mipLevels));
				previousEnd = mip.Offset + mip.Length;
				previousWidth = mip.Width;
				previousHeight = mip.Height;
			}
			if (previousEnd != data.Length) throw new ArgumentException("The compressed payload contains unused bytes.", nameof(data));
			Width = width;
			Height = height;
			Format = format;
			IsSrgb = isSrgb;
			SurfaceCount = surfaceCount;
			BlockBytes = GetBlockBytes(format);
			MipLevels = new CompressedTextureMip[mipLevels.Length];
			Array.Copy(mipLevels, MipLevels, mipLevels.Length);
			Data = data;
		}
	}

	/// <summary>Explicitly decodes a compressed texture to the existing RGBA representation.</summary>
	public interface ICompressedTextureDecoder {
		/// <summary>Decodes the base mip to row-based RGBA bytes.</summary>
		byte[] DecodeToRgba(CompressedTextureData data);
	}

	/// <summary>Origin for an in-memory compressed texture payload.</summary>
	public sealed class CompressedOrigin : TextureOrigin {
		/// <summary>Gets the compressed payload.</summary>
		public CompressedTextureData Data { get; private set; }
		/// <summary>Gets the explicit CPU decoder.</summary>
		public ICompressedTextureDecoder Decoder { get; private set; }
		/// <summary>Gets the supplied transparency classification.</summary>
		public TextureTransparencyType Transparency { get; private set; }

		/// <summary>Creates a compressed texture origin.</summary>
		public CompressedOrigin(CompressedTextureData data, ICompressedTextureDecoder decoder, TextureTransparencyType transparency) {
			Data = data ?? throw new ArgumentNullException(nameof(data));
			Decoder = decoder;
			Transparency = transparency;
		}

		/// <summary>Gets a texture containing the compressed payload.</summary>
		public override bool GetTexture(out Texture texture) {
			texture = new Texture(Data, Decoder, Transparency);
			return true;
		}

		/// <summary>Checks compressed-origin equality.</summary>
		public static bool operator ==(CompressedOrigin a, CompressedOrigin b) {
			if (ReferenceEquals(a, b)) return true;
			if (a is null || b is null) return false;
			return ReferenceEquals(a.Data, b.Data) && ReferenceEquals(a.Decoder, b.Decoder);
		}

		/// <summary>Checks compressed-origin inequality.</summary>
		public static bool operator !=(CompressedOrigin a, CompressedOrigin b) { return !(a == b); }

		/// <summary>Checks compressed-origin equality.</summary>
		public override bool Equals(object obj) { return this == obj as CompressedOrigin; }

		/// <summary>Gets the compressed-origin hash code.</summary>
		public override int GetHashCode() { return Data == null ? 0 : Data.GetHashCode(); }
	}
}
