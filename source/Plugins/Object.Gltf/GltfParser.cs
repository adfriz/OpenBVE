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
 * GltfParser
 * ----------
 * A small glTF 2.0 reader for static scenery: same deal as NewObjParser, one
 * file with nothing exotic in it. The one exception is JSON- writing a JSON
 * parser by hand is neither simple nor fast, so that bit is Newtonsoft's.
 *
 * What it handles, deliberately kept to the boring subset:
 *  - .gltf (JSON with external or base64 buffers) and .glb (the binary box)
 *  - Node trees with matrix or translation/rotation/scale, flattened to world space
 *  - TRIANGLES primitives with POSITION, optional NORMAL and TEXCOORD_0, indexed or not
 *  - Materials: base color factor + texture, alphaMode, doubleSided
 *
 * What it skips (with a warning, not a crash): skins, morph targets, cameras,
 * lights, animations, metallic/roughness maps, emissive and occlusion textures,
 * sparse accessors, and anything that isn't triangles.
 *
 * Coordinate notes: glTF is already right-handed Y-up like OpenBVE, so positions
 * pass straight through. Texture V passes through untouched too: glTF files
 * store V=0 at the top of the image (Blender flips it on export), which is
 * exactly OpenBVE's convention as well.
 *
 * The bit that turns all that into a StaticObject lives in GltfObject.cs, kept
 * separate so this file stays reusable elsewhere.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Object.Gltf
{
	/// <summary>Where we send the "this file is a bit odd" messages while parsing</summary>
	public interface IGltfParserLogger
	{
		/// <summary>Something's off but we can carry on</summary>
		void Warning(string message);
		/// <summary>Something's off and the bit it belongs to is unusable</summary>
		void Error(string message);
	}

	/// <summary>One corner of a triangle, already in world space</summary>
	public struct GltfCorner
	{
		/// <summary>World-space position</summary>
		public double X, Y, Z;
		/// <summary>World-space normal</summary>
		public double NX, NY, NZ;
		/// <summary>Texture co-ordinates, exactly as the file stores them</summary>
		public double U, V;
	}

	/// <summary>A bunch of triangles that all use the same material</summary>
	public sealed class GltfMesh
	{
		/// <summary>Index into the model's material list, or -1 for plain untextured</summary>
		public int MaterialIndex = -1;
		/// <summary>Whether faces are visible from both sides</summary>
		public bool DoubleSided;
		/// <summary>The triangle corners, three at a time</summary>
		public readonly List<GltfCorner> Corners = new List<GltfCorner>();

		/// <summary>How many triangles are in here</summary>
		public int TriangleCount
		{
			get { return Corners.Count / 3; }
		}
	}

	/// <summary>One material out of the file</summary>
	public sealed class GltfMaterial
	{
		/// <summary>Base color red, 0-255</summary>
		public byte R = 255;
		/// <summary>Base color green, 0-255</summary>
		public byte G = 255;
		/// <summary>Base color blue, 0-255</summary>
		public byte B = 255;
		/// <summary>Opacity, 0-255</summary>
		public byte A = 255;
		/// <summary>Absolute path to an external texture, or null</summary>
		public string DaytimeTexture;
		/// <summary>An embedded texture's raw bytes, or null</summary>
		public byte[] ImageBytes;
		/// <summary>The embedded texture's mime type (image/png, image/jpeg)</summary>
		public string ImageMime;
		/// <summary>Whether faces are visible from both sides</summary>
		public bool DoubleSided;
	}

	/// <summary>Everything we got out of one glTF file</summary>
	public sealed class GltfModel
	{
		/// <summary>Every material in the file</summary>
		public readonly List<GltfMaterial> Materials = new List<GltfMaterial>();
		/// <summary>The geometry, already flattened and split by material</summary>
		public readonly List<GltfMesh> Meshes = new List<GltfMesh>();
	}

	/// <summary>Reads glTF 2.0 files without any dependencies beyond Newtonsoft. See the note at the top of this file.</summary>
	public static class GltfParser
	{
		// glTF component types we understand
		private const int ComponentByte = 5120;
		private const int ComponentUByte = 5121;
		private const int ComponentShort = 5122;
		private const int ComponentUShort = 5123;
		private const int ComponentUInt = 5125;
		private const int ComponentFloat = 5126;

		// Primitive modes (we only draw triangles)
		private const int ModeTriangles = 4;

		/// <summary>Read a .gltf or .glb file off disk</summary>
		/// <param name="filePath">Where it lives</param>
		/// <param name="logger">Where complaints go, or null to stay quiet</param>
		public static GltfModel ParseFile(string filePath, IGltfParserLogger logger = null)
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			string directory = string.Empty;
			try
			{
				directory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? string.Empty;
			}
			catch
			{
				// Weird path- external buffers and textures will just fail softly below
			}
			if (bytes.Length >= 4 && bytes[0] == 'g' && bytes[1] == 'l' && bytes[2] == 'T' && bytes[3] == 'F')
			{
				return ParseGlb(bytes, directory, logger);
			}
			return ParseText(Encoding.UTF8.GetString(bytes), directory, logger);
		}

		/// <summary>Parse .gltf JSON you already have in memory (handy for tests)</summary>
		/// <param name="json">The file contents</param>
		/// <param name="baseDirectory">Where buffers and textures are relative to</param>
		/// <param name="logger">Where complaints go, or null to stay quiet</param>
		public static GltfModel ParseText(string json, string baseDirectory, IGltfParserLogger logger = null)
		{
			Parser parser = new Parser(baseDirectory ?? string.Empty, logger);
			return parser.Run(JObject.Parse(json), null);
		}

		/// <summary>Parse a .glb blob you already have in memory (handy for tests)</summary>
		public static GltfModel ParseGlb(byte[] bytes, string baseDirectory, IGltfParserLogger logger = null)
		{
			Parser parser = new Parser(baseDirectory ?? string.Empty, logger);
			return parser.RunGlb(bytes);
		}

		// All the scratch state for parsing one file. Lives here (not in statics) so
		// two files on two threads can't trip over each other.
		private sealed class Parser
		{
			private readonly string baseDirectory;
			private readonly IGltfParserLogger logger;
			private readonly GltfModel model = new GltfModel();
			private readonly List<byte[]> buffers = new List<byte[]>();
			private int defaultMaterial = -1;

			private JObject root;
			private JArray bufferViews;
			private JArray accessors;
			private JArray meshes;
			private JArray nodes;
			private JArray materials;
			private JArray textures;
			private JArray images;

			internal Parser(string baseDirectory, IGltfParserLogger logger)
			{
				this.baseDirectory = baseDirectory;
				this.logger = logger;
			}

			private void Warn(string message)
			{
				if (logger != null) logger.Warning(message);
			}

			// --- Small JSON helpers -------------------------------------------------

			private static JArray ArrayOf(JObject obj, string name)
			{
				JToken token = obj[name];
				return token != null && token.Type == JTokenType.Array ? (JArray)token : null;
			}

			private static JObject ObjectOf(JToken token, string name)
			{
				if (token == null || token.Type != JTokenType.Object)
				{
					return null;
				}
				JToken child = ((JObject)token)[name];
				return child != null && child.Type == JTokenType.Object ? (JObject)child : null;
			}

			private static int IntOf(JToken token, string name, int fallback)
			{
				if (token == null || token.Type != JTokenType.Object)
				{
					return fallback;
				}
				JToken child = ((JObject)token)[name];
				if (child == null || child.Type != JTokenType.Integer)
				{
					return fallback;
				}
				return (int)(long)child;
			}

			private static bool BoolOf(JToken token, string name, bool fallback)
			{
				if (token == null || token.Type != JTokenType.Object)
				{
					return fallback;
				}
				JToken child = ((JObject)token)[name];
				if (child == null || child.Type != JTokenType.Boolean)
				{
					return fallback;
				}
				return (bool)child;
			}

			private static string StringOf(JToken token, string name)
			{
				if (token == null || token.Type != JTokenType.Object)
				{
					return null;
				}
				JToken child = ((JObject)token)[name];
				return child != null && child.Type == JTokenType.String ? (string)child : null;
			}

			private static double NumberAt(JToken array, int index, double fallback)
			{
				if (array == null || array.Type != JTokenType.Array || index < 0 || index >= ((JArray)array).Count)
				{
					return fallback;
				}
				JToken item = ((JArray)array)[index];
				if (item.Type != JTokenType.Integer && item.Type != JTokenType.Float)
				{
					return fallback;
				}
				return (double)item;
			}

			// --- Entry points ---------------------------------------------------------

			internal GltfModel Run(JObject root, byte[] glbBin)
			{
				this.root = root;
				bufferViews = ArrayOf(root, "bufferViews");
				accessors = ArrayOf(root, "accessors");
				meshes = ArrayOf(root, "meshes");
				nodes = ArrayOf(root, "nodes");
				materials = ArrayOf(root, "materials");
				textures = ArrayOf(root, "textures");
				images = ArrayOf(root, "images");

				LoadBuffers(glbBin);
				LoadMaterials();

				// Default scene, or the first one if the file doesn't say
				JArray scenes = ArrayOf(root, "scenes");
				JArray sceneNodes = null;
				int scene = IntOf(root, "scene", 0);
				if (scenes != null && scene >= 0 && scene < scenes.Count)
				{
					JToken nodesToken = scenes[scene]["nodes"];
					if (nodesToken != null && nodesToken.Type == JTokenType.Array)
					{
						sceneNodes = (JArray)nodesToken;
					}
				}
				if (sceneNodes == null)
				{
					// No scenes at all- just walk every node as if it were a root
					if (nodes != null)
					{
						for (int i = 0; i < nodes.Count; i++)
						{
							WalkNode(i, Identity());
						}
					}
					return model;
				}
				foreach (JToken nodeToken in sceneNodes)
				{
					if (nodeToken.Type == JTokenType.Integer)
					{
						WalkNode((int)(long)nodeToken, Identity());
					}
				}
				return model;
			}

			internal GltfModel RunGlb(byte[] bytes)
			{
				// 12-byte header, then chunks of (length, type, data)
				if (bytes.Length < 12)
				{
					Warn("File is too short to be a .glb container.");
					return model;
				}
				int jsonLength = BitConverter.ToInt32(bytes, 12);
				if (jsonLength < 0 || 20 + jsonLength > bytes.Length)
				{
					Warn("The .glb JSON chunk runs past the end of the file.");
					return model;
				}
				string json = Encoding.UTF8.GetString(bytes, 20, jsonLength);
				byte[] bin = null;
				int pos = 20 + jsonLength;
				if (pos + 8 <= bytes.Length)
				{
					int binLength = BitConverter.ToInt32(bytes, pos);
					if (binLength >= 0 && pos + 8 + binLength <= bytes.Length)
					{
						bin = new byte[binLength];
						Buffer.BlockCopy(bytes, pos + 8, bin, 0, binLength);
					}
				}
				JObject parsed;
				try
				{
					parsed = JObject.Parse(json);
				}
				catch (Exception ex)
				{
					Warn("Could not parse the .glb JSON chunk: " + ex.Message);
					return model;
				}
				return Run(parsed, bin);
			}

			// --- Buffers ----------------------------------------------------------------

			private void LoadBuffers(byte[] glbBin)
			{
				JArray defs = ArrayOf(root, "buffers");
				if (defs == null)
				{
					if (glbBin != null)
					{
						buffers.Add(glbBin);
					}
					return;
				}
				for (int i = 0; i < defs.Count; i++)
				{
					// In a .glb the first buffer is the BIN chunk, whatever its uri says
					if (i == 0 && glbBin != null)
					{
						buffers.Add(glbBin);
						continue;
					}
					string uri = StringOf(defs[i], "uri");
					if (uri != null && uri.StartsWith("data:", StringComparison.Ordinal))
					{
						int comma = uri.IndexOf(',');
						try
						{
							buffers.Add(comma >= 0 ? Convert.FromBase64String(uri.Substring(comma + 1)) : new byte[0]);
						}
						catch
						{
							Warn("Buffer " + i + " has a broken base64 data URI.");
							buffers.Add(new byte[0]);
						}
						continue;
					}
					if (string.IsNullOrEmpty(uri))
					{
						// A .glb buffer without a uri is the BIN chunk
						buffers.Add(glbBin ?? new byte[0]);
						continue;
					}
					string path;
					try
					{
						path = Path.Combine(baseDirectory, uri);
					}
					catch
					{
						Warn("Buffer " + i + " has an unusable path.");
						buffers.Add(new byte[0]);
						continue;
					}
					if (!File.Exists(path))
					{
						Warn("Buffer file " + uri + " was not found.");
						buffers.Add(new byte[0]);
						continue;
					}
					try
					{
						buffers.Add(File.ReadAllBytes(path));
					}
					catch (Exception ex)
					{
						Warn("Could not read buffer file " + uri + ": " + ex.Message);
						buffers.Add(new byte[0]);
					}
				}
			}

			// Resolves an accessor to a slice of a buffer: returns false when the
			// indices are nonsense, so the caller can skip the primitive.
			private static int ComponentSize(int componentType)
			{
				switch (componentType)
				{
					case ComponentByte:
					case ComponentUByte:
						return 1;
					case ComponentShort:
					case ComponentUShort:
						return 2;
					case ComponentUInt:
					case ComponentFloat:
						return 4;
					default:
						return 0;
				}
			}

			private bool AccessorBytes(int accessorIndex, int components, out byte[] data, out int start, out int stride, out int count)
			{
				data = null;
				start = stride = count = 0;
				if (accessors == null || accessorIndex < 0 || accessorIndex >= accessors.Count)
				{
					return false;
				}
				JObject accessor = accessors[accessorIndex] as JObject;
				int viewIndex = IntOf(accessor, "bufferView", -1);
				count = IntOf(accessor, "count", 0);
				if (accessor == null || viewIndex < 0 || bufferViews == null || viewIndex >= bufferViews.Count || count <= 0)
				{
					return false;
				}
				if (accessor["sparse"] != null)
				{
					Warn("Sparse accessors are not supported, skipping part of the model.");
					return false;
				}
				JObject view = bufferViews[viewIndex] as JObject;
				int bufferIndex = IntOf(view, "buffer", 0);
				int viewOffset = IntOf(view, "byteOffset", 0);
				int accessorOffset = IntOf(accessor, "byteOffset", 0);
				if (view == null || bufferIndex < 0 || bufferIndex >= buffers.Count)
				{
					return false;
				}
				int componentSize = ComponentSize(IntOf(accessor, "componentType", -1));
				if (componentSize == 0)
				{
					Warn("An accessor has an unknown component type, skipping part of the model.");
					return false;
				}
				stride = IntOf(view, "byteStride", components * componentSize);
				data = buffers[bufferIndex];
				start = viewOffset + accessorOffset;
				// Make sure the whole thing actually fits in the buffer before touching it
				long end = (long)start + (long)(count - 1) * stride + components * componentSize;
				if (start < 0 || end > data.Length)
				{
					Warn("An accessor runs past the end of its buffer, skipping part of the model.");
					return false;
				}
				return true;
			}

			private float[] ReadFloats(int accessorIndex, int components)
			{
				JObject accessor = accessors[accessorIndex] as JObject;
				if (IntOf(accessor, "componentType", -1) != ComponentFloat)
				{
					Warn("Only float attributes are supported, skipping part of the model.");
					return null;
				}
				byte[] data;
				int start, stride, count;
				if (!AccessorBytes(accessorIndex, components, out data, out start, out stride, out count))
				{
					return null;
				}
				float[] result = new float[count * components];
				for (int i = 0; i < count; i++)
				{
					int offset = start + i * stride;
					for (int c = 0; c < components; c++)
					{
						result[i * components + c] = BitConverter.ToSingle(data, offset + c * 4);
					}
				}
				return result;
			}

			private float[] ReadUvs(int accessorIndex)
			{
				JObject accessor = accessors[accessorIndex] as JObject;
				int componentType = IntOf(accessor, "componentType", ComponentFloat);
				if (componentType == ComponentFloat)
				{
					return ReadFloats(accessorIndex, 2);
				}
				// Normalized bytes / shorts are legal for uvs- stretch them back to 0..1
				byte[] data;
				int start, stride, count;
				if (!AccessorBytes(accessorIndex, 2, out data, out start, out stride, out count))
				{
					return null;
				}
				float[] result = new float[count * 2];
				for (int i = 0; i < count; i++)
				{
					int offset = start + i * stride;
					if (componentType == ComponentUByte)
					{
						result[i * 2] = data[offset] / 255.0f;
						result[i * 2 + 1] = data[offset + 1] / 255.0f;
					}
					else if (componentType == ComponentUShort)
					{
						result[i * 2] = BitConverter.ToUInt16(data, offset) / 65535.0f;
						result[i * 2 + 1] = BitConverter.ToUInt16(data, offset + 2) / 65535.0f;
					}
					else
					{
						Warn("Unsupported TEXCOORD_0 component type, ignoring texture co-ordinates.");
						return null;
					}
				}
				return result;
			}

			private int[] ReadIndices(int accessorIndex, int vertexCount)
			{
				JObject accessor = accessors[accessorIndex] as JObject;
				int componentType = IntOf(accessor, "componentType", -1);
				byte[] data;
				int start, stride, count;
				if (!AccessorBytes(accessorIndex, 1, out data, out start, out stride, out count))
				{
					return null;
				}
				int[] result = new int[count];
				for (int i = 0; i < count; i++)
				{
					int offset = start + i * stride;
					int index;
					if (componentType == ComponentUByte)
					{
						index = data[offset];
					}
					else if (componentType == ComponentUShort)
					{
						index = BitConverter.ToUInt16(data, offset);
					}
					else if (componentType == ComponentUInt)
					{
						long wide = BitConverter.ToUInt32(data, offset);
						index = wide > int.MaxValue ? -1 : (int)wide;
					}
					else
					{
						Warn("Unsupported index component type, skipping part of the model.");
						return null;
					}
					if (index < 0 || index >= vertexCount)
					{
						Warn("A face index points past the end of the vertices, skipping part of the model.");
						return null;
					}
					result[i] = index;
				}
				return result;
			}

			// --- Materials ------------------------------------------------------------------

			private static byte ToByte(double value)
			{
				if (value < 0) return 0;
				if (value > 255) return 255;
				return (byte)(value + 0.5);
			}

			private void LoadMaterials()
			{
				if (materials == null)
				{
					return;
				}
				for (int i = 0; i < materials.Count; i++)
				{
					GltfMaterial material = new GltfMaterial();
					material.DoubleSided = BoolOf(materials[i], "doubleSided", false);
					JObject pbr = ObjectOf(materials[i], "pbrMetallicRoughness");
					JToken factor = pbr != null ? pbr["baseColorFactor"] : null;
					material.R = ToByte(NumberAt(factor, 0, 1.0) * 255.0);
					material.G = ToByte(NumberAt(factor, 1, 1.0) * 255.0);
					material.B = ToByte(NumberAt(factor, 2, 1.0) * 255.0);
					material.A = ToByte(NumberAt(factor, 3, 1.0) * 255.0);
					JObject colorTexture = pbr != null ? ObjectOf(pbr, "baseColorTexture") : null;
					if (colorTexture != null)
					{
						ResolveTexture(IntOf(colorTexture, "index", -1), material);
					}
					model.Materials.Add(material);
				}
			}

			private void ResolveTexture(int textureIndex, GltfMaterial material)
			{
				if (textures == null || textureIndex < 0 || textureIndex >= textures.Count)
				{
					return;
				}
				int imageIndex = IntOf(textures[textureIndex], "source", -1);
				if (images == null || imageIndex < 0 || imageIndex >= images.Count)
				{
					return;
				}
				JObject image = images[imageIndex] as JObject;
				if (image == null)
				{
					return;
				}
				string uri = StringOf(image, "uri");
				if (uri != null)
				{
					if (uri.StartsWith("data:", StringComparison.Ordinal))
					{
						int comma = uri.IndexOf(',');
						int semicolon = uri.IndexOf(';');
						material.ImageMime = semicolon > 5 ? uri.Substring(5, semicolon - 5) : null;
						try
						{
							material.ImageBytes = comma >= 0 ? Convert.FromBase64String(uri.Substring(comma + 1)) : null;
						}
						catch
						{
							Warn("A texture has a broken base64 data URI.");
						}
						return;
					}
					string path;
					try
					{
						path = Path.Combine(baseDirectory, uri);
					}
					catch
					{
						Warn("A texture has an unusable path.");
						return;
					}
					if (File.Exists(path))
					{
						try
						{
							material.DaytimeTexture = Path.GetFullPath(path);
						}
						catch
						{
							material.DaytimeTexture = path;
						}
					}
					else
					{
						Warn("Texture file " + uri + " was not found.");
					}
					return;
				}
				int viewIndex = IntOf(image, "bufferView", -1);
				if (viewIndex < 0 || bufferViews == null || viewIndex >= bufferViews.Count)
				{
					return;
				}
				JObject view = bufferViews[viewIndex] as JObject;
				int bufferIndex = IntOf(view, "buffer", 0);
				int offset = IntOf(view, "byteOffset", 0);
				int length = IntOf(view, "byteLength", 0);
				if (view == null || bufferIndex < 0 || bufferIndex >= buffers.Count ||
					offset < 0 || length <= 0 || offset + length > buffers[bufferIndex].Length)
				{
					Warn("A texture's buffer view runs past the end of its buffer.");
					return;
				}
				material.ImageBytes = new byte[length];
				Buffer.BlockCopy(buffers[bufferIndex], offset, material.ImageBytes, 0, length);
				material.ImageMime = StringOf(image, "mimeType");
			}

			// --- Nodes and meshes ---------------------------------------------------------------

			// Column-major 4x4 identity, the way glTF stores matrices
			private static double[] Identity()
			{
				return new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
			}

			private static double[] Multiply(double[] a, double[] b)
			{
				double[] result = new double[16];
				for (int row = 0; row < 4; row++)
				{
					for (int col = 0; col < 4; col++)
					{
						result[row + col * 4] = a[row] * b[col * 4] + a[row + 4] * b[col * 4 + 1] +
							a[row + 8] * b[col * 4 + 2] + a[row + 12] * b[col * 4 + 3];
					}
				}
				return result;
			}

			// Builds a column-major matrix out of translation, xyzw quaternion and scale
			private static double[] Compose(JToken translation, JToken rotation, JToken scale)
			{
				double qx = NumberAt(rotation, 0, 0.0);
				double qy = NumberAt(rotation, 1, 0.0);
				double qz = NumberAt(rotation, 2, 0.0);
				double qw = NumberAt(rotation, 3, 1.0);
				double sx = NumberAt(scale, 0, 1.0);
				double sy = NumberAt(scale, 1, 1.0);
				double sz = NumberAt(scale, 2, 1.0);
				double xx = qx * qx;
				double xy = qx * qy;
				double xz = qx * qz;
				double xw = qx * qw;
				double yy = qy * qy;
				double yz = qy * qz;
				double yw = qy * qw;
				double zz = qz * qz;
				double zw = qz * qw;
				return new double[]
				{
					(1 - 2 * (yy + zz)) * sx, 2 * (xy + zw) * sx, 2 * (xz - yw) * sx, 0,
					2 * (xy - zw) * sy, (1 - 2 * (xx + zz)) * sy, 2 * (yz + xw) * sy, 0,
					2 * (xz + yw) * sz, 2 * (yz - xw) * sz, (1 - 2 * (xx + yy)) * sz, 0,
					NumberAt(translation, 0, 0.0), NumberAt(translation, 1, 0.0), NumberAt(translation, 2, 0.0), 1
				};
			}

			private void WalkNode(int nodeIndex, double[] parent)
			{
				if (nodes == null || nodeIndex < 0 || nodeIndex >= nodes.Count)
				{
					return;
				}
				JObject node = nodes[nodeIndex] as JObject;
				if (node == null)
				{
					return;
				}
				double[] local;
				JToken matrix = node["matrix"];
				if (matrix != null && matrix.Type == JTokenType.Array && ((JArray)matrix).Count == 16)
				{
					local = new double[16];
					for (int i = 0; i < 16; i++)
					{
						local[i] = NumberAt(matrix, i, i % 5 == 0 ? 1.0 : 0.0);
					}
				}
				else
				{
					local = Compose(node["translation"], node["rotation"], node["scale"]);
				}
				double[] world = Multiply(parent, local);
				int meshIndex = IntOf(node, "mesh", -1);
				if (meshes != null && meshIndex >= 0 && meshIndex < meshes.Count)
				{
					EmitMesh(meshes[meshIndex] as JObject, world);
				}
				JToken children = node["children"];
				if (children != null && children.Type == JTokenType.Array)
				{
					foreach (JToken child in (JArray)children)
					{
						if (child.Type == JTokenType.Integer)
						{
							WalkNode((int)(long)child, world);
						}
					}
				}
			}

			private void EmitMesh(JObject mesh, double[] world)
			{
				if (mesh == null)
				{
					return;
				}
				JToken primitives = mesh["primitives"];
				if (primitives == null || primitives.Type != JTokenType.Array)
				{
					return;
				}
				foreach (JToken primitiveToken in (JArray)primitives)
				{
					JObject primitive = primitiveToken as JObject;
					if (primitive == null)
					{
						continue;
					}
					try
					{
						EmitPrimitive(primitive, world);
					}
					catch (Exception ex)
					{
						Warn("Skipping part of the model: " + ex.Message);
					}
				}
			}

			private void EmitPrimitive(JObject primitive, double[] world)
			{
				if (IntOf(primitive, "mode", ModeTriangles) != ModeTriangles)
				{
					Warn("Only triangle primitives are supported, skipping part of the model.");
					return;
				}
				JObject attributes = ObjectOf(primitive, "attributes");
				if (attributes == null)
				{
					return;
				}
				JToken positionToken = attributes["POSITION"];
				if (positionToken == null || positionToken.Type != JTokenType.Integer)
				{
					Warn("A primitive without positions is not supported, skipping it.");
					return;
				}
				float[] positions = ReadFloats((int)(long)positionToken, 3);
				if (positions == null)
				{
					return;
				}
				int vertexCount = positions.Length / 3;
				float[] normals = null;
				JToken normalToken = attributes["NORMAL"];
				if (normalToken != null && normalToken.Type == JTokenType.Integer)
				{
					normals = ReadFloats((int)(long)normalToken, 3);
					if (normals != null && normals.Length != positions.Length)
					{
						Warn("A primitive has mismatched positions and normals, ignoring its normals.");
						normals = null;
					}
				}
				float[] uvs = null;
				JToken uvToken = attributes["TEXCOORD_0"];
				if (uvToken != null && uvToken.Type == JTokenType.Integer)
				{
					uvs = ReadUvs((int)(long)uvToken);
					if (uvs != null && uvs.Length != vertexCount * 2)
					{
						Warn("A primitive has mismatched positions and texture co-ordinates, ignoring them.");
						uvs = null;
					}
				}
				int[] indices = null;
				JToken indexToken = primitive["indices"];
				if (indexToken != null && indexToken.Type == JTokenType.Integer)
				{
					indices = ReadIndices((int)(long)indexToken, vertexCount);
					if (indices == null)
					{
						return;
					}
				}
				int corners = indices != null ? indices.Length : vertexCount;
				if (corners % 3 != 0)
				{
					Warn("A primitive has a triangle count that isn't whole, skipping it.");
					return;
				}
				GltfMesh mesh = new GltfMesh();
				int materialIndex = IntOf(primitive, "material", -1);
				if (materialIndex >= 0 && materialIndex < model.Materials.Count)
				{
					mesh.MaterialIndex = materialIndex;
				}
				else
				{
					// No material (or a broken reference)- share one plain white one
					if (defaultMaterial < 0)
					{
						defaultMaterial = model.Materials.Count;
						model.Materials.Add(new GltfMaterial());
					}
					mesh.MaterialIndex = defaultMaterial;
				}
				mesh.DoubleSided = model.Materials[mesh.MaterialIndex].DoubleSided;
				for (int i = 0; i < corners; i++)
				{
					int v = indices != null ? indices[i] : i;
					GltfCorner corner = new GltfCorner();
					TransformPoint(world, positions[v * 3], positions[v * 3 + 1], positions[v * 3 + 2],
						out corner.X, out corner.Y, out corner.Z);
					if (normals != null)
					{
						TransformNormal(world, normals[v * 3], normals[v * 3 + 1], normals[v * 3 + 2],
							out corner.NX, out corner.NY, out corner.NZ);
					}
					if (uvs != null)
					{
						// Same top-left origin as OpenBVE, so no flipping needed
						corner.U = uvs[v * 2];
						corner.V = uvs[v * 2 + 1];
					}
					mesh.Corners.Add(corner);
				}
				if (normals == null)
				{
					// No normals in the file- cook up flat face normals so lighting has something to work with
					for (int i = 0; i < mesh.Corners.Count; i += 3)
					{
						double nx, ny, nz;
						FaceNormal(mesh.Corners[i], mesh.Corners[i + 1], mesh.Corners[i + 2], out nx, out ny, out nz);
						GltfCorner a = mesh.Corners[i];
						GltfCorner b = mesh.Corners[i + 1];
						GltfCorner c = mesh.Corners[i + 2];
						a.NX = b.NX = c.NX = nx;
						a.NY = b.NY = c.NY = ny;
						a.NZ = b.NZ = c.NZ = nz;
						mesh.Corners[i] = a;
						mesh.Corners[i + 1] = b;
						mesh.Corners[i + 2] = c;
					}
				}
				if (mesh.TriangleCount != 0)
				{
					model.Meshes.Add(mesh);
				}
			}

			private static void TransformPoint(double[] m, double x, double y, double z, out double ox, out double oy, out double oz)
			{
				ox = m[0] * x + m[4] * y + m[8] * z + m[12];
				oy = m[1] * x + m[5] * y + m[9] * z + m[13];
				oz = m[2] * x + m[6] * y + m[10] * z + m[14];
			}

			private static void TransformNormal(double[] m, double x, double y, double z, out double ox, out double oy, out double oz)
			{
				// Upper 3x3 only, renormalized. Not exactly right under non-uniform
				// scale, but fine for the rigid transforms scenery uses.
				ox = m[0] * x + m[4] * y + m[8] * z;
				oy = m[1] * x + m[5] * y + m[9] * z;
				oz = m[2] * x + m[6] * y + m[10] * z;
				double length = Math.Sqrt(ox * ox + oy * oy + oz * oz);
				if (length > 0.0)
				{
					ox /= length;
					oy /= length;
					oz /= length;
				}
			}

			private static void FaceNormal(GltfCorner a, GltfCorner b, GltfCorner c, out double nx, out double ny, out double nz)
			{
				double ux = b.X - a.X;
				double uy = b.Y - a.Y;
				double uz = b.Z - a.Z;
				double vx = c.X - a.X;
				double vy = c.Y - a.Y;
				double vz = c.Z - a.Z;
				nx = uy * vz - uz * vy;
				ny = uz * vx - ux * vz;
				nz = ux * vy - uy * vx;
				double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
				if (length > 0.0)
				{
					nx /= length;
					ny /= length;
					nz /= length;
				}
			}
		}
	}
}
