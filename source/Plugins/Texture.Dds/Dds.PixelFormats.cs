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

// ReSharper disable UnusedMember.Global
namespace Texture.Dds
{
	/// <summary>Various pixel formats/compressors used by the DDS image.</summary>
	internal enum PixelFormat
	{
		/// <summary>32-bit image, with 8-bit red, green, blue and alpha.</summary>
		RGBA,
		/// <summary>24-bit image with 8-bit red, green, blue.</summary>
		RGB,
		/// <summary>16-bit DXT-1 compression, 1-bit alpha.</summary>
		DXT1,
		/// <summary>DXT-2 Compression</summary>
		DXT2,
		/// <summary>DXT-3 Compression</summary>
		DXT3,
		/// <summary>DXT-4 Compression</summary>
		DXT4,
		/// <summary>DXT-5 Compression</summary>
		DXT5,
		/// <summary>3DC Compression</summary>
		THREEDC,
		/// <summary>ATI1n Compression</summary>
		ATI1N,
		LUMINANCE,
		LUMINANCE_ALPHA,
		RXGB,
		A16B16G16R16,
		R16F,
		G16R16F,
		A16B16G16R16F,
		R32F,
		R32_FLOAT,
		G32R32F,
		A32B32G32R32F,
		/// <summary>8-bit single channel (DX10 R8).</summary>
		R8,
		/// <summary>8-bit alpha only.</summary>
		A8,
		/// <summary>16-bit two channels (DX10 R8G8).</summary>
		RG8,
		/// <summary>16-bit single channel (DX10 R16).</summary>
		R16,
		/// <summary>32-bit two-channel UNORM (DX10 R16G16).</summary>
		RG16,
		/// <summary>BC4 single channel via DX10.</summary>
		R8_BC4,
		/// <summary>BC5 two channels via DX10.</summary>
		RG8_BC5,
		/// <summary>BC7 RGBA (DX10).</summary>
		/// <remarks>16 bytes per 4x4 block; UNORM_SRGB is rejected.
		/// Spec and DXGI values:
		/// https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format
		/// https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format-mode-reference
		/// https://learn.microsoft.com/en-us/windows/win32/api/dxgiformat/ne-dxgiformat-dxgi_format</remarks>
		BC7,
		/// <summary>64-bit 4x16-bit UNORM.</summary>
		R16G16B16A16,
		/// <summary>Unknown pixel format.</summary>
		UNKNOWN
	}
}
