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
 * The OpenBVE half of GltfParser.
 *
 * GltfParser.cs on its own knows nothing about OpenBVE- it just hands back
 * structs and lists. This file translates that into StaticObjects and friends,
 * so if you ever pinch the parser for another project, this is the one file
 * you leave behind.
 */

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using OpenBveApi.Colors;
using OpenBveApi.Hosts;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using Object.Gltf;

namespace Plugin
{
	/// <summary>Pipes the parser's warnings / errors into the in-game log</summary>
	internal sealed class HostGltfLogger : IGltfParserLogger
	{
		private readonly HostInterface currentHost;

		internal HostGltfLogger(HostInterface host)
		{
			currentHost = host;
		}

		public void Warning(string message)
		{
			currentHost.AddMessage(MessageType.Warning, false, message);
		}

		public void Error(string message)
		{
			currentHost.AddMessage(MessageType.Error, false, message);
		}
	}

	/// <summary>Takes a parsed GltfModel and builds the StaticObject the game actually draws</summary>
	internal static class GltfObject
	{
		/// <summary>Load a .gltf / .glb file the same way the other parsers do</summary>
		/// <param name="fileName">Full path to the file</param>
		internal static StaticObject ReadObject(string fileName)
		{
			GltfModel model = GltfParser.ParseFile(fileName, new HostGltfLogger(Plugin.CurrentHost));
			StaticObject parsedObject = new StaticObject(Plugin.CurrentHost);
			Convert(model, ref parsedObject);
			parsedObject.Mesh.FinalizeMesh();
			return parsedObject;
		}

		// One MeshBuilder per mesh, since each mesh is single-material by construction
		private static void Convert(GltfModel model, ref StaticObject parsedObject)
		{
			for (int i = 0; i < model.Meshes.Count; i++)
			{
				GltfMesh mesh = model.Meshes[i];
				if (mesh.TriangleCount == 0)
				{
					continue;
				}
				MeshBuilder meshBuilder = new MeshBuilder(Plugin.CurrentHost);
				if (mesh.MaterialIndex >= 0)
				{
					meshBuilder.Materials[0] = ConvertMaterial(model.Materials[mesh.MaterialIndex]);
				}
				for (int c = 0; c < mesh.Corners.Count; c += 3)
				{
					// Corners already came out of the parser in world space, so no fixups here
					int first = meshBuilder.Vertices.Count;
					MeshFaceVertex[] corners = new MeshFaceVertex[3];
					// Mirror on X to match the viewer display (positions + normals), swapping
					// the last two corners so winding stays consistent after the reflection.
					int[] mirrorOrder = { 0, 2, 1 };
					for (int k = 0; k < 3; k++)
					{
						GltfCorner corner = mesh.Corners[c + mirrorOrder[k]];
						meshBuilder.Vertices.Add(new Vertex(
							new Vector3(-corner.X, corner.Y, corner.Z),
							new Vector2(corner.U, corner.V)));
						corners[k] = new MeshFaceVertex(first + k, new Vector3(-corner.NX, corner.NY, corner.NZ));
					}
					MeshFace face = new MeshFace(corners, 0);
					face.Flags |= FaceFlags.Triangles;
					if (mesh.DoubleSided)
					{
						face.Flags |= FaceFlags.Face2Mask;
					}
					meshBuilder.Faces.Add(face);
				}
				meshBuilder.Apply(ref parsedObject);
			}
		}

		// Parsed material -> MeshBuilder material. File-agnostic PBR contract: factors,
		// emissive and texture paths ride the shared Material, any importer can fill them.
		private static Material ConvertMaterial(GltfMaterial material)
		{
			Material result = new Material
			{
				Color = new Color32(material.R, material.G, material.B, material.A),
				DaytimeTexture = material.DaytimeTexture,
				MetallicFactor = material.MetallicFactor,
				RoughnessFactor = material.RoughnessFactor,
				EmissiveColor = new Color32(ToByte(material.EmissiveR * 255.0f), ToByte(material.EmissiveG * 255.0f), ToByte(material.EmissiveB * 255.0f), (byte)255),
				NormalScale = material.NormalScale,
				OcclusionStrength = material.OcclusionStrength
			};
			if (result.DaytimeTexture == null && material.ImageBytes != null)
			{
				// Embedded texture- the renderer only eats file paths, so spill it to a
				// content-hashed file in temp. Same image always lands on the same path.
				try
				{
					result.DaytimeTexture = CacheImage(material.ImageBytes, material.ImageMime);
				}
				catch
				{
					// Temp file trouble- drop the texture rather than the whole object
				}
			}
			if (material.TransmissionFactor > 0.0f)
			{
				// Thin-glass approx: OpenBVE has no transmission framebuffer, so the
				// factor lowers opacity instead. The lowered alpha routes the face
				// through the transparent pass (interior shows through) while the PBR
				// env reflection on top keeps it reading as glass, not a hole.
				float glass = material.TransmissionFactor;
				if (glass < 0.0f) glass = 0.0f;
				if (glass > 1.0f) glass = 1.0f;
				byte alpha = ToByte((1.0f - glass) * 255.0f);
				if (alpha < 8) alpha = 8;
				result.Color = new Color32(result.Color.R, result.Color.G, result.Color.B, alpha);
			}
			result.NormalTexture = material.NormalTexture ?? CacheImageSoft(material.NormalBytes, material.NormalMime);
			result.OcclusionTexture = material.OcclusionTexture ?? CacheImageSoft(material.OcclusionBytes, material.OcclusionMime);
			result.EmissiveTexture = material.EmissiveTexture ?? CacheImageSoft(material.EmissiveBytes, material.EmissiveMime);
			result.MetallicRoughnessTexture = material.MetallicRoughnessTexture ?? CacheImageSoft(material.MetallicRoughnessBytes, material.MetallicRoughnessMime);
			// PBR shading path when the file carries PBR data (factors off default or maps present)
			if (material.MetallicFactor != 1.0f || material.RoughnessFactor != 1.0f || material.HasAdvancedData ||
				result.NormalTexture != null || result.OcclusionTexture != null || result.EmissiveTexture != null ||
				result.MetallicRoughnessTexture != null)
			{
				result.Flags |= MaterialFlags.PBR;
			}
			return result;
		}

		private static byte ToByte(float value)
		{
			if (value < 0) return 0;
			if (value > 255) return 255;
			return (byte)(value + 0.5f);
		}

		private static string CacheImageSoft(byte[] bytes, string mime)
		{
			if (bytes == null) return null;
			try
			{
				return CacheImage(bytes, mime);
			}
			catch
			{
				return null;
			}
		}

		private static string CacheImage(byte[] bytes, string mime)
		{
			string extension = ".bin";
			if (mime == "image/png")
			{
				extension = ".png";
			}
			else if (mime == "image/jpeg")
			{
				extension = ".jpg";
			}
			StringBuilder name = new StringBuilder(32);
			using (MD5 md5 = MD5.Create())
			{
				foreach (byte b in md5.ComputeHash(bytes))
				{
					name.Append(b.ToString("x2"));
				}
			}
			string directory = Path.Combine(Path.GetTempPath(), "OpenBVE-Gltf");
			Directory.CreateDirectory(directory);
			string path = Path.Combine(directory, name + extension);
			if (!File.Exists(path))
			{
				File.WriteAllBytes(path, bytes);
			}
			return path;
		}
	}
}
