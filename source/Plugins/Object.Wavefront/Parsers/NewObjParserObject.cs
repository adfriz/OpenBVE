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
 * The OpenBVE half of NewObjParser.
 *
 * NewObjParser.cs on its own knows nothing about OpenBVE- it just hands back
 * structs and lists. This file translates that into StaticObjects and friends,
 * so if you ever pinch the parser for another project, this is the one file
 * you leave behind.
 */

using System;
using System.Text;
using OpenBveApi.Colors;
using OpenBveApi.Hosts;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using Object.Wavefront;

namespace Plugin
{
	/// <summary>Pipes the parser's warnings / errors into the in-game log</summary>
	internal sealed class HostObjLogger : IObjParserLogger
	{
		private readonly HostInterface currentHost;

		internal HostObjLogger(HostInterface host)
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

	/// <summary>Takes a parsed ObjModel and builds the StaticObject the game actually draws</summary>
	internal static class NewObjParserObject
	{
		/// <summary>Load an OBJ file the same way the other parsers do</summary>
		/// <param name="fileName">Full path to the .obj</param>
		/// <param name="encoding">Whatever encoding the file was saved in</param>
		internal static StaticObject ReadObject(string fileName, Encoding encoding)
		{
			ObjModel model = NewObjParser.ParseFile(fileName, encoding, new HostObjLogger(Plugin.CurrentHost));
			StaticObject parsedObject = new StaticObject(Plugin.CurrentHost);
			Convert(model, ref parsedObject);
			parsedObject.Mesh.FinalizeMesh();
			return parsedObject;
		}

		// One MeshBuilder per mesh, since each mesh is single-material by construction
		private static void Convert(ObjModel model, ref StaticObject parsedObject)
		{
			for (int i = 0; i < model.Meshes.Count; i++)
			{
				ObjMesh mesh = model.Meshes[i];
				if (mesh.TriangleCount == 0)
				{
					continue;
				}
				MeshBuilder meshBuilder = new MeshBuilder(Plugin.CurrentHost);
				if (mesh.MaterialIndex >= 0)
				{
					meshBuilder.Materials[0] = ConvertMaterial(model.Materials[mesh.MaterialIndex]);
				}
				EmitMesh(model, mesh, meshBuilder);
				meshBuilder.Apply(ref parsedObject);
			}
		}

		// Unpack one mesh's triangles into faces, applying the exporter fixups as we go
		private static void EmitMesh(ObjModel model, ObjMesh mesh, MeshBuilder meshBuilder)
		{
			for (int i = 0; i < mesh.Corners.Count; i += 3)
			{
				ObjCorner c0 = mesh.Corners[i];
				ObjCorner c1 = mesh.Corners[i + 1];
				ObjCorner c2 = mesh.Corners[i + 2];
				Vector3 p0 = Position(model, c0.Position);
				Vector3 p1 = Position(model, c1.Position);
				Vector3 p2 = Position(model, c2.Position);
				Vector2 uv0 = TextureCoord(model, c0.TexCoord);
				Vector2 uv1 = TextureCoord(model, c1.TexCoord);
				Vector2 uv2 = TextureCoord(model, c2.TexCoord);
				Vector3 n0 = Normal(model, c0.Normal);
				Vector3 n1 = Normal(model, c1.Normal);
				Vector3 n2 = Normal(model, c2.Normal);
				if (model.LeftHanded)
				{
					// Mirrored exporter (SketchUp etc.): mirror X back and swap two corners so the face points the right way
					p0.X = -p0.X;
					p1.X = -p1.X;
					p2.X = -p2.X;
					n0.X = -n0.X;
					n1.X = -n1.X;
					n2.X = -n2.X;
					Vector3 tempPosition = p1;
					p1 = p2;
					p2 = tempPosition;
					Vector2 tempTexture = uv1;
					uv1 = uv2;
					uv2 = tempTexture;
					Vector3 tempNormal = n1;
					n1 = n2;
					n2 = tempNormal;
				}
				if (n0.X == 0.0 & n0.Y == 0.0 & n0.Z == 0.0)
				{
					// No normals in the file for this corner- cook up a flat face normal so lighting has something to work with
					Vector3 calculated = CalculateNormal(p0, p1, p2);
					n0 = calculated;
					n1 = calculated;
					n2 = calculated;
				}
				int first = meshBuilder.Vertices.Count;
				meshBuilder.Vertices.Add(new Vertex(p0, uv0));
				meshBuilder.Vertices.Add(new Vertex(p1, uv1));
				meshBuilder.Vertices.Add(new Vertex(p2, uv2));
				MeshFace face = new MeshFace(3);
				face.Vertices[0] = new MeshFaceVertex(first, n0);
				face.Vertices[1] = new MeshFaceVertex(first + 1, n1);
				face.Vertices[2] = new MeshFaceVertex(first + 2, n2);
				face.Flags |= FaceFlags.Triangles;
				if (!c0.Flat && !c1.Flat && !c2.Flat)
				{
					/*
					 * Face2Mask marks a face as double-sided, which switches backface
					 * culling off for it. OBJ has no dedicated "render both sides" flag-
					 * the only signal is 's off' / 's 0', meaning the exporter asked for
					 * flat shading. Those are recorded on the corners as Flat, and we
					 * leave Face2Mask off for them so the renderer culls their backfaces.
					 *
					 * Everything else keeps the previous always-double-sided behaviour,
					 * since most OBJ files carry no smoothing data at all and would
					 * otherwise vanish when viewed from behind.
					 */
					face.Flags |= FaceFlags.Face2Mask;
				}
				meshBuilder.Faces.Add(face);
			}
		}

		// Index into Positions (-1 means "not given", so origin)
		private static Vector3 Position(ObjModel model, int index)
		{
			if (index < 0)
			{
				return new Vector3();
			}
			ObjPosition p = model.Positions[index];
			return new Vector3(p.X, p.Y, p.Z);
		}

		// Same for normals
		private static Vector3 Normal(ObjModel model, int index)
		{
			if (index < 0)
			{
				return new Vector3();
			}
			ObjNormal n = model.Normals[index];
			return new Vector3(n.X, n.Y, n.Z);
		}

		// Same for uvs, plus the per-exporter flipping
		private static Vector2 TextureCoord(ObjModel model, int index)
		{
			if (index < 0)
			{
				return new Vector2();
			}
			ObjTexCoord uv = model.TexCoords[index];
			double x = uv.U;
			double y = uv.V;
			if (model.FlipUvX)
			{
				x = 1.0 - x;
			}
			if (model.FlipUvY)
			{
				y = 1.0 - y;
			}
			return new Vector2(x, y);
		}

		// Plain cross-product face normal; gives up (zero vector) on degenerate triangles
		private static Vector3 CalculateNormal(Vector3 a, Vector3 b, Vector3 c)
		{
			double ux = b.X - a.X;
			double uy = b.Y - a.Y;
			double uz = b.Z - a.Z;
			double vx = c.X - a.X;
			double vy = c.Y - a.Y;
			double vz = c.Z - a.Z;
			double nx = uy * vz - uz * vy;
			double ny = uz * vx - ux * vz;
			double nz = ux * vy - uy * vx;
			double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
			if (length == 0.0)
			{
				return new Vector3();
			}
			return new Vector3(nx / length, ny / length, nz / length);
		}

		// Parsed material -> MeshBuilder material. Most of the MTL things we keep are just color + textures.
		private static Material ConvertMaterial(ObjMaterial material)
		{
			Material result = new Material
			{
				Color = new Color32(material.R, material.G, material.B, material.A),
				DaytimeTexture = material.DaytimeTexture,
				NighttimeTexture = material.NighttimeTexture
			};
			if (!string.IsNullOrEmpty(material.TransparencyTexture))
			{
				result.TransparencyTexture = material.TransparencyTexture;
				result.Flags |= MaterialFlags.TransparentColor;
			}
			return result;
		}
	}
}
