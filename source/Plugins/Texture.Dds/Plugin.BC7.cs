/*
 * Copyright (c) 2026, The OpenBVE Project
 * Simplified BSD License (BSD-2-Clause)
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *
 * 1. Redistributions of source code must retain the above copyright notice, this
 *    list of conditions and the following disclaimer.
 * 2. Redistributions in binary form must reproduce the above copyright notice,
 *    this list of conditions and the following disclaimer in the documentation
 *    and/or other materials provided with the distribution.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
 * ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

/*
 * BC7 (BPTC LDR) decoder for the openBVE DDS plugin.
 * Supports DXGI BC7_TYPELESS/UNORM (97/98); rejects sRGB (99) and BC6H.
 *
 * References:
 *   https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format
 *   https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format-mode-reference
 *   https://learn.microsoft.com/en-us/windows/win32/direct3d11/texture-block-compression-in-direct3d-11
 *   https://learn.microsoft.com/en-us/windows/win32/api/dxgiformat/ne-dxgiformat-dxgi_format
 *   https://github.com/KhronosGroup/DataFormat/blob/main/bptc.txt
 *   https://registry.khronos.org/DataFormat/specs/1.3/dataformat.1.3.html (section 20.1 BC7)
 *   https://registry.khronos.org/OpenGL/extensions/ARB/ARB_texture_compression_bptc.txt
 *   https://github.com/microsoft/DirectXTex/blob/main/DirectXTex/BC6HBC7.cpp
 *
 * Blocks are 128-bit little-endian values: mode, partition/rotation/selector,
 * endpoints/P-bits, then indices. Texels use x + 4*y; endpoints are channel-major.
 * Anchor texels store one index bit less than other texels.
 *
 * The bit-reader/partition packing follows iOrange/bcdec v0.985
 * (Copyright (c) 2022 Sergii Kudlai, Unlicense): https://github.com/iOrange/bcdec
 */

using System;
using System.Diagnostics;
using System.IO;

namespace Texture.Dds
{
	internal static class Bc7Decoder
	{
		private sealed class DecodeWorkspace
		{
			public readonly int[] r = new int[6];
			public readonly int[] g = new int[6];
			public readonly int[] b = new int[6];
			public readonly int[] a = new int[6];
			public readonly byte[] er = new byte[6];
			public readonly byte[] eg = new byte[6];
			public readonly byte[] eb = new byte[6];
			public readonly byte[] ea = new byte[6];
			public readonly byte[] subset = new byte[16];
			public readonly byte[] partitionInfo = new byte[16];
			public readonly byte[] primaryIndices = new byte[16];
			public readonly byte[] secondaryIndices = new byte[16];
		}

		private struct BitReader
		{
			private ulong low;
			private ulong high;

			public int Position;

			public BitReader(ulong low, ulong high)
			{
				this.low = low;
				this.high = high;
				this.Position = 0;
			}

			public int Read(int count)
			{
				if (count == 0) return 0;
				uint mask = (uint)((1UL << count) - 1UL);
				uint bits = (uint)(low & mask);
				low >>= count;
				low |= (high & mask) << (64 - count);
				high >>= count;
				Position += count;
				return (int)bits;
			}
		}

		// Interpolation weights for 2/3/4-bit indices:
		// https://registry.khronos.org/OpenGL/extensions/ARB/ARB_texture_compression_bptc.txt
		private static readonly byte[] Weight2 = { 0, 21, 43, 64 };
		private static readonly byte[] Weight3 = { 0, 9, 18, 27, 37, 46, 55, 64 };
		private static readonly byte[] Weight4 = { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 };

		private static byte[] WeightTable(int indexBits)
		{
			switch (indexBits)
			{
				case 2: return Weight2;
				case 3: return Weight3;
				default: return Weight4; // 4
			}
		}

		// Index start offsets from the BC7 mode table; Debug.Assert checks the cursor.
		// https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format-mode-reference
		private static readonly int[] IndexStart = { 83, 82, 99, 98, 50, 66, 65, 98 };

		// Two-subset partition table; each character is a texel's subset.
		// https://github.com/KhronosGroup/DataFormat/blob/main/bptc.txt
		private static readonly string[] Partition2 = new string[]
		{
			"0011001100110011", // 0
			"0001000100010001", // 1
			"0111011101110111", // 2
			"0001001100110111", // 3
			"0000000100010011", // 4
			"0011011101111111", // 5
			"0001001101111111", // 6
			"0000000100110111", // 7
			"0000000000010011", // 8
			"0011011111111111", // 9
			"0000000101111111", // 10
			"0000000000010111", // 11
			"0001011111111111", // 12
			"0000000011111111", // 13
			"0000111111111111", // 14
			"0000000000001111", // 15
			"0000100011101111", // 16
			"0111000100000000", // 17
			"0000000010001110", // 18
			"0111001100010000", // 19
			"0011000100000000", // 20
			"0000100011001110", // 21
			"0000000010001100", // 22
			"0111001100110001", // 23
			"0011000100010000", // 24
			"0000100010001100", // 25
			"0110011001100110", // 26
			"0011011001101100", // 27
			"0001011111101000", // 28
			"0000111111110000", // 29
			"0111000110001110", // 30
			"0011100110011100", // 31
			"0101010101010101", // 32
			"0000111100001111", // 33
			"0101101001011010", // 34
			"0011001111001100", // 35
			"0011110000111100", // 36
			"0101010110101010", // 37
			"0110100101101001", // 38
			"0101101010100101", // 39
			"0111001111001110", // 40
			"0001001111001000", // 41
			"0011001001001100", // 42
			"0011101111011100", // 43
			"0110100110010110", // 44
			"0011110011000011", // 45
			"0110011010011001", // 46
			"0000011001100000", // 47
			"0100111001000000", // 48
			"0010011100100000", // 49
			"0000001001110010", // 50
			"0000010011100100", // 51
			"0110110010010011", // 52
			"0011011011001001", // 53
			"0110001110011100", // 54
			"0011100111000110", // 55
			"0110110011001001", // 56
			"0110001100111001", // 57
			"0111111010000001", // 58
			"0001100011100111", // 59
			"0000111100110011", // 60
			"0011001111110000", // 61
			"0010001011101110", // 62
			"0100010001110111", // 63
		};

		// Three-subset partition table; each character is a texel's subset.
		// https://github.com/KhronosGroup/DataFormat/blob/main/bptc.txt
		private static readonly string[] Partition3 = new string[]
		{
			"0011001102212222", // 0
			"0001001122112221", // 1
			"0000200122112211", // 2
			"0222002200110111", // 3
			"0000000011221122", // 4
			"0011001100220022", // 5
			"0022002211111111", // 6
			"0011001122112211", // 7
			"0000000011112222", // 8
			"0000111111112222", // 9
			"0000111122222222", // 10
			"0012001200120012", // 11
			"0112011201120112", // 12
			"0122012201220122", // 13
			"0011011211221222", // 14
			"0011200122002220", // 15
			"0001001101121122", // 16
			"0111001120012200", // 17
			"0000112211221122", // 18
			"0022002200221111", // 19
			"0111011102220222", // 20
			"0001000122212221", // 21
			"0000001101220122", // 22
			"0000110022102210", // 23
			"0122012200110000", // 24
			"0012001211222222", // 25
			"0110122112210110", // 26
			"0000011012211221", // 27
			"0022110211020022", // 28
			"0110011020022222", // 29
			"0011012201220011", // 30
			"0000200022112221", // 31
			"0000000211221222", // 32
			"0222002200120011", // 33
			"0011001200220222", // 34
			"0120012001200120", // 35
			"0000111122220000", // 36
			"0120120120120120", // 37
			"0120201212010120", // 38
			"0011220011220011", // 39
			"0011112222000011", // 40
			"0101010122222222", // 41
			"0000000021212121", // 42
			"0022112200221122", // 43
			"0022001100220011", // 44
			"0220122102201221", // 45
			"0101222222220101", // 46
			"0000212121212121", // 47
			"0101010101012222", // 48
			"0222011102220111", // 49
			"0002111200021112", // 50
			"0000211221122112", // 51
			"0222011101110222", // 52
			"0002111211120002", // 53
			"0110011001102222", // 54
			"0000000021122112", // 55
			"0110011022222222", // 56
			"0022001100110022", // 57
			"0022112211220022", // 58
			"0000000000002112", // 59
			"0002000100020001", // 60
			"0222122202221222", // 61
			"0101222222222222", // 62
			"0111201122012220", // 63
		};

		// Anchor texels (one fewer index bit); subset 0 is texel 0.
		// https://registry.khronos.org/OpenGL/extensions/ARB/ARB_texture_compression_bptc.txt
		private static readonly byte[] Anchor2 = new byte[]
		{
			15,15,15,15,15,15,15,15,15,15,15,15,15,15,15,15,
			15,2,8,2,2,8,8,15,2,8,2,2,8,8,2,2,
			15,15,6,8,2,8,15,15,2,8,2,2,2,15,15,6,
			6,2,6,8,15,15,2,2,15,15,15,15,15,2,2,15
		};

		private static readonly byte[] Anchor3a = new byte[]
		{
			3,3,15,15,8,3,15,15,8,8,6,6,6,5,3,3,
			3,3,8,15,3,3,6,10,5,8,8,6,8,5,15,15,
			8,15,3,5,6,10,8,15,15,3,15,5,15,15,15,15,
			3,15,5,5,5,8,5,10,5,10,8,13,15,12,3,3
		};

		private static readonly byte[] Anchor3b = new byte[]
		{
			15,8,8,3,15,15,3,8,15,15,15,15,15,15,15,8,
			15,8,15,3,15,8,15,8,3,15,6,10,15,15,10,8,
			15,3,15,10,10,8,9,10,6,15,8,15,3,6,6,8,
			15,3,15,15,15,15,15,15,15,15,15,15,3,15,15,8
		};

		// Packed subset/anchor lookup: low two bits are the subset, bit 7 marks an anchor.
		private static readonly byte[] PartitionData = BuildPartitionData();

		private static byte[] BuildPartitionData()
		{
			byte[] data = new byte[2 * 64 * 16];
			for (int p = 0; p < 64; p++)
			{
				int offset = p * 16;
				string row = Partition2[p];
				for (int i = 0; i < 16; i++) data[offset + i] = (byte)(row[i] - '0');
				data[offset] |= 0x80;
				data[offset + Anchor2[p]] |= 0x80;
			}
			for (int p = 0; p < 64; p++)
			{
				int offset = (64 + p) * 16;
				string row = Partition3[p];
				for (int i = 0; i < 16; i++) data[offset + i] = (byte)(row[i] - '0');
				data[offset] |= 0x80;
				data[offset + Anchor3a[p]] |= 0x80;
				data[offset + Anchor3b[p]] |= 0x80;
			}
			return data;
		}

		/// <summary>Decodes the top mip level of BC7 block data to 32-bit RGBA.</summary>
		public static byte[] Decompress(DdsHeader header, byte[] data)
		{
			int width = header.width;
			int height = header.height;
			int blocksX = (width + 3) / 4;
			int blocksY = (height + 3) / 4;
			if ((long)blocksX * blocksY * 16L != data.Length)
			{
				throw new InvalidDataException("DDS BC7 data size mismatch.");
			}
			byte[] rawData = new byte[width * height * 4];
			int bps = width * 4;
			byte[] blockOut = new byte[64]; // one decoded 4x4 block, RGBA per texel
			// Reuse all small per-block scratch arrays for the whole image.
			DecodeWorkspace workspace = new DecodeWorkspace();
			for (int by = 0; by < blocksY; by++)
			{
				for (int bx = 0; bx < blocksX; bx++)
				{
					int srcOff = (by * blocksX + bx) * 16;
					DecodeBlock(data, srcOff, blockOut, workspace);
					// Copy out, clipping blocks that hang over non-multiple-of-4 edges.
					for (int y = 0; y < 4; y++)
					{
						int py = by * 4 + y;
						if (py >= height) continue;
						int rowOff = py * bps + bx * 16;
						for (int x = 0; x < 4; x++)
						{
							int px = bx * 4 + x;
							if (px >= width) continue;
							int src = (y * 4 + x) * 4;
							int dst = rowOff + x * 4;
							rawData[dst + 0] = blockOut[src + 0];
							rawData[dst + 1] = blockOut[src + 1];
							rawData[dst + 2] = blockOut[src + 2];
							rawData[dst + 3] = blockOut[src + 3];
						}
					}
				}
			}
			return rawData;
		}

		private static ulong ReadU64(byte[] data, int offset)
		{
			return ((ulong)data[offset])
				| ((ulong)data[offset + 1] << 8)
				| ((ulong)data[offset + 2] << 16)
				| ((ulong)data[offset + 3] << 24)
				| ((ulong)data[offset + 4] << 32)
				| ((ulong)data[offset + 5] << 40)
				| ((ulong)data[offset + 6] << 48)
				| ((ulong)data[offset + 7] << 56);
		}

		// Replicates a compressed endpoint to 8 bits.
		// https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format
		private static byte Unquantize(int raw, int prec)
		{
			raw <<= (8 - prec);
			raw |= raw >> prec;
			return (byte)raw;
		}

		// Blends endpoints with a rounded 6-bit weight.
		// https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format
		private static byte Interpolate(byte e0, byte e1, int weight)
		{
			return (byte)(((64 - weight) * e0 + weight * e1 + 32) >> 6);
		}

		// Decodes one 16-byte block into 16 RGBA texels (64 bytes, texel order x + 4*y).
		private static void DecodeBlock(byte[] data, int srcOff, byte[] dst64, DecodeWorkspace workspace)
		{
			ulong lo = ReadU64(data, srcOff);
			ulong hi = ReadU64(data, srcOff + 8);
			BitReader reader = new BitReader(lo, hi);

			// Step 1: mode = position of the first set bit in bits 0..7 (8 = reserved).
			int mode = 8;
			for (int m = 0; m < 8; m++)
			{
				if (reader.Read(1) != 0)
				{
					mode = m;
					break;
				}
			}
			if (mode == 8)
			{
				// Reserved mode 8 decodes as transparent black.
				// https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format-mode-reference
				for (int i = 0; i < 64; i += 4)
				{
					dst64[i + 0] = 0;
					dst64[i + 1] = 0;
					dst64[i + 2] = 0;
					dst64[i + 3] = 0;
				}
				return;
			}

			// Step 2: mode layout (subsets, partition, rotation, endpoints, indices).
			// https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format-mode-reference
			int ns;
			int pb;
			int rb = 0;
			int isb = 0;
			int cb;
			int ab;
			int epb;
			int spb;
			int ib;
			int ib2;
			switch (mode)
			{
				case 0: ns = 3; pb = 4; cb = 4; ab = 0; epb = 6; spb = 0; ib = 3; ib2 = 0; break;
				case 1: ns = 2; pb = 6; cb = 6; ab = 0; epb = 0; spb = 2; ib = 3; ib2 = 0; break;
				case 2: ns = 3; pb = 6; cb = 5; ab = 0; epb = 0; spb = 0; ib = 2; ib2 = 0; break;
				case 3: ns = 2; pb = 6; cb = 7; ab = 0; epb = 4; spb = 0; ib = 2; ib2 = 0; break;
				case 4: ns = 1; pb = 0; cb = 5; ab = 6; epb = 0; spb = 0; ib = 2; ib2 = 3; rb = 2; isb = 1; break;
				case 5: ns = 1; pb = 0; cb = 7; ab = 8; epb = 0; spb = 0; ib = 2; ib2 = 2; rb = 2; break;
				case 6: ns = 1; pb = 0; cb = 7; ab = 7; epb = 2; spb = 0; ib = 4; ib2 = 0; break;
				default: ns = 2; pb = 6; cb = 5; ab = 5; epb = 4; spb = 0; ib = 2; ib2 = 0; break; // mode 7
			}

			// Step 3: partition, rotation, and index-selector bits.
			int partition = pb > 0 ? reader.Read(pb) : 0;
			int rotation = rb > 0 ? reader.Read(rb) : 0;
			int idxMode = isb > 0 ? reader.Read(isb) : 0;

			// Step 4: raw endpoints, channel-major (R, G, B, A).
			// https://github.com/KhronosGroup/DataFormat/blob/main/bptc.txt
			int epCount = ns * 2;
			int[] r = workspace.r;
			int[] g = workspace.g;
			int[] b = workspace.b;
			int[] a = workspace.a;
			for (int c = 0; c < 3; c++)
			{
				for (int e = 0; e < epCount; e++)
				{
					int v = reader.Read(cb);
					if (c == 0) r[e] = v;
					else if (c == 1) g[e] = v;
					else b[e] = v;
				}
			}
			for (int e = 0; e < epCount; e++)
			{
				a[e] = ab > 0 ? reader.Read(ab) : 0;
			}

			// Step 5: P-bits become the endpoint LSBs.
			// https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format
			if (epb > 0)
			{
				for (int e = 0; e < epCount; e++)
				{
					int p = reader.Read(1);
					r[e] = (r[e] << 1) | p;
					g[e] = (g[e] << 1) | p;
					b[e] = (b[e] << 1) | p;
					if (ab > 0) a[e] = (a[e] << 1) | p;
				}
			}
			else if (spb > 0)
			{
				// Mode 1: one shared P-bit per subset (RGB only).
				for (int s = 0; s < ns; s++)
				{
					int p = reader.Read(1);
					r[s * 2 + 0] = (r[s * 2 + 0] << 1) | p;
					r[s * 2 + 1] = (r[s * 2 + 1] << 1) | p;
					g[s * 2 + 0] = (g[s * 2 + 0] << 1) | p;
					g[s * 2 + 1] = (g[s * 2 + 1] << 1) | p;
					b[s * 2 + 0] = (b[s * 2 + 0] << 1) | p;
					b[s * 2 + 1] = (b[s * 2 + 1] << 1) | p;
				}
			}

			// The cursor must reach the index data.
			Debug.Assert(reader.Position == IndexStart[mode], "BC7 bit cursor drifted before indices");

			// Step 6: expand endpoints; modes without alpha are opaque.
			int colorPrec = cb + (epb > 0 || spb > 0 ? 1 : 0);
			int alphaPrec = ab + (epb > 0 ? 1 : 0);
			byte[] er = workspace.er;
			byte[] eg = workspace.eg;
			byte[] eb = workspace.eb;
			byte[] ea = workspace.ea;
			for (int e = 0; e < epCount; e++)
			{
				er[e] = Unquantize(r[e], colorPrec);
				eg[e] = Unquantize(g[e], colorPrec);
				eb[e] = Unquantize(b[e], colorPrec);
				if (ab > 0) ea[e] = Unquantize(a[e], alphaPrec);
				else ea[e] = 255;
			}

			// Step 7: map texels to subsets and anchor texels.
			byte[] subset = workspace.subset;
			byte[] partitionInfo = workspace.partitionInfo;
			int tableOffset = -1;
			if (ns == 1)
			{
				for (int i = 0; i < 16; i++)
				{
					subset[i] = 0;
					partitionInfo[i] = i == 0 ? (byte)0x80 : (byte)0;
				}
			}
			else
			{
				int p = partition & 63;
				if (mode == 0) p &= 15;
				tableOffset = (ns == 2 ? 0 : 64) * 16 + p * 16;
				for (int i = 0; i < 16; i++)
				{
					byte info = PartitionData[tableOffset + i];
					partitionInfo[i] = info;
					subset[i] = (byte)(info & 3);
				}
			}

			// Step 8: read palette indices; modes 4/5 use two planes.
			// https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format
			byte[] colorIdx = workspace.primaryIndices;
			for (int t = 0; t < 16; t++)
			{
				int bits = (partitionInfo[t] & 0x80) != 0 ? ib - 1 : ib;
				colorIdx[t] = (byte)reader.Read(bits);
			}
			byte[] alphaIdx = colorIdx;
			int colorBits = ib;
			int alphaBits = ib;
			if (ib2 > 0)
			{
				byte[] secondary = workspace.secondaryIndices;
				for (int t = 0; t < 16; t++)
				{
					int bits = (partitionInfo[t] & 0x80) != 0 ? ib2 - 1 : ib2;
					secondary[t] = (byte)reader.Read(bits);
				}
				if (mode == 4 && idxMode == 1)
				{
					colorIdx = secondary;
					colorBits = ib2;
				}
				else
				{
					alphaIdx = secondary;
					alphaBits = ib2;
				}
			}
			byte[] cw = WeightTable(colorBits);
			byte[] aw = WeightTable(alphaBits);

			// Step 9: interpolate, undo mode 4/5 rotation, and write RGBA.
			// https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format
			for (int t = 0; t < 16; t++)
			{
				int s = subset[t];
				byte e0r = er[s * 2 + 0], e1r = er[s * 2 + 1];
				byte e0g = eg[s * 2 + 0], e1g = eg[s * 2 + 1];
				byte e0b = eb[s * 2 + 0], e1b = eb[s * 2 + 1];
				byte e0a = ea[s * 2 + 0], e1a = ea[s * 2 + 1];
				byte rr = Interpolate(e0r, e1r, cw[colorIdx[t]]);
				byte gg = Interpolate(e0g, e1g, cw[colorIdx[t]]);
				byte bb = Interpolate(e0b, e1b, cw[colorIdx[t]]);
				byte aa = ab > 0 ? Interpolate(e0a, e1a, aw[alphaIdx[t]]) : (byte)255;
				if (mode == 4 || mode == 5)
				{
					switch (rotation)
					{
						case 1: { byte tmp = aa; aa = rr; rr = tmp; break; }
						case 2: { byte tmp = aa; aa = gg; gg = tmp; break; }
						case 3: { byte tmp = aa; aa = bb; bb = tmp; break; }
					}
				}
				dst64[t * 4 + 0] = rr;
				dst64[t * 4 + 1] = gg;
				dst64[t * 4 + 2] = bb;
				dst64[t * 4 + 3] = aa;
			}
		}

	}
}
