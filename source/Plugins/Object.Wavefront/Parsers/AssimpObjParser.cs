//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2020, S520, The OpenBVE Project
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

using System;
using System.Collections.Generic;
using OpenBveApi.Colors;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using AssimpNET.Obj;
using OpenBveApi;
using Material = AssimpNET.Obj.Material;
using Mesh = AssimpNET.Obj.Mesh;

namespace Plugin
{
	internal class AssimpObjParser
	{
		private static string currentFolder;
		private static int materialGroupIndex;

		internal static StaticObject ReadObject(string fileName)
		{
			currentFolder = Path.GetDirectoryName(fileName);
			try
			{
				ObjFileParser parser = new ObjFileParser(System.IO.File.ReadLines(fileName), null, System.IO.Path.GetFileNameWithoutExtension(fileName), fileName);
				Model model = parser.GetModel();

				StaticObject obj = new StaticObject(Plugin.CurrentHost);
				MeshBuilder builder = new MeshBuilder(Plugin.CurrentHost);
				Material lastMaterial = null;
				// caches (position + texture co-ordinates) -> builder vertex index to avoid
				// storing one heap Vertex object per face-vertex (many faces share the same vertex)
				Dictionary<Vertex, int> vertexCache = new Dictionary<Vertex, int>();
				int faceCountInGroup = 0;
				materialGroupIndex = 0;
				int totalMeshes = model.Meshes.Count;
				Plugin.CurrentHost.AddMessage(MessageType.Information, false, "AssimpObjParser: " + totalMeshes + " meshes, " + model.Vertices.Count + " vertices, " + model.MaterialMap.Count + " materials");

				// log material map
				foreach (var kvp in model.MaterialMap)
				{
					string tex = kvp.Value.Texture ?? "(none)";
					string texOp = kvp.Value.TextureOpacity ?? "(none)";
					Plugin.CurrentHost.AddMessage(MessageType.Information, false, "  Material \"" + kvp.Key + "\": Texture=" + tex + " Opacity=" + texOp + " d=" + kvp.Value.Alpha);
				}

				foreach (Mesh mesh in model.Meshes)
				{
					Plugin.CurrentHost.AddMessage(MessageType.Information, false, "AssimpObjParser: Mesh #" + mesh.MaterialIndex + " has " + mesh.Faces.Count + " faces, MaterialIndex=" + mesh.MaterialIndex);
					foreach (Face face in mesh.Faces)
					{
					if (face.Material != lastMaterial)
					{
						if (builder.Faces.Count > 0)
						{
							Plugin.CurrentHost.AddMessage(MessageType.Information, false, "  MaterialGroup #" + materialGroupIndex + ": " + builder.Faces.Count + " faces");
						}
						builder.Apply(ref obj);
						builder = new MeshBuilder(Plugin.CurrentHost);
						vertexCache.Clear();
						faceCountInGroup = 0;
						materialGroupIndex++;
						Material material = face.Material;
						if (material != null)
						{
							string matName = material.MaterialName ?? "(null)";
							string texPath = material.Texture ?? "(none)";
							string texOpPath = material.TextureOpacity ?? "(none)";
							Plugin.CurrentHost.AddMessage(MessageType.Information, false, "  MaterialGroup #" + materialGroupIndex + " start: \"" + matName + "\" Texture=" + texPath + " Opacity=" + texOpPath + " d=" + material.Alpha);
							builder.Materials[0].Color = new Color32(material.Diffuse);
								// apply alpha from MTL 'd' command (fixes missing transparent faces e.g. leaves)
								builder.Materials[0].Color.A = (byte)(material.Alpha * 255);
#pragma warning disable 0219
								//Current openBVE renderer does not support specular color
								// ReSharper disable once UnusedVariable
								Color24 mSpecular = new Color24(material.Specular);
#pragma warning restore 0219
								builder.Materials[0].EmissiveColor = new Color32(new Color24(material.Emissive));
								// only set emissive flag when non-black (avoids unlit rendering on normal materials)
								if (material.Emissive.R != 0 || material.Emissive.G != 0 || material.Emissive.B != 0)
								{
									builder.Materials[0].Flags |= MaterialFlags.Emissive;
								}
								if (material.TransparentUsed)
								{
									builder.Materials[0].TransparentColor = new Color24(material.Transparent);
									builder.Materials[0].Flags |= MaterialFlags.TransparentColor;
								}

							if (material.Texture != null)
							{
								builder.Materials[0].DaytimeTexture = Path.CombineFile(currentFolder, material.Texture);
								if (!System.IO.File.Exists(builder.Materials[0].DaytimeTexture))
								{
									Plugin.CurrentHost.AddMessage(MessageType.Error, true, "Texture " + builder.Materials[0].DaytimeTexture + " was not found in file " + fileName);
									builder.Materials[0].DaytimeTexture = null;
								}
							}

							// apply the opacity map (OBJ 'map_d') as the material's transparency texture.
							// This is what actually defines the leaf cutout for many exporters; without it
							// transparent faces such as foliage can fail to show their proper shape.
							if (material.TextureOpacity != null)
							{
								builder.Materials[0].TransparencyTexture = Path.CombineFile(currentFolder, material.TextureOpacity);
								if (!System.IO.File.Exists(builder.Materials[0].TransparencyTexture))
								{
									Plugin.CurrentHost.AddMessage(MessageType.Error, true, "Opacity texture " + builder.Materials[0].TransparencyTexture + " was not found in file " + fileName);
									builder.Materials[0].TransparencyTexture = null;
								}
							}
							}
						}
						
						if (face.Vertices.Count == 0)
						{
							throw new Exception("nVertices must be greater than zero");
						}
int[] faceIndices = new int[face.Vertices.Count];
						for (int i = 0; i < face.Vertices.Count; i++)
						{
							Vertex v = new Vertex(model.Vertices[(int)face.Vertices[i]] * model.ScaleFactor);
							
							if (model.TextureCoord.Count > 0 && face.TexturCoords.Count > 0 && i < face.TexturCoords.Count)
							{
								// use face's texcoord index, not loop index i (fixes wrong UV mapping)
								int texCoordIndex = (int)face.TexturCoords[i];
								if (texCoordIndex < model.TextureCoord.Count)
								{
									Vector2 textureCoordinate = new Vector2(model.TextureCoord[texCoordIndex].X, model.TextureCoord[texCoordIndex].Y);
									switch (model.Exporter)
									{
										case ModelExporter.SketchUp:
											textureCoordinate.X *= -1.0;
											textureCoordinate.Y *= -1.0;
											break;
										case ModelExporter.Blender:
										case ModelExporter.BlockBench:
											textureCoordinate.Y *= -1.0;
											break;
									}
									v.TextureCoordinates = textureCoordinate;
								}
							}
							
							int vertexIndex;
							if (!vertexCache.TryGetValue(v, out vertexIndex))
							{
								vertexIndex = builder.Vertices.Count;
								builder.Vertices.Add(v);
								vertexCache.Add(v, vertexIndex);
							}
						faceIndices[i] = vertexIndex;
					}

					faceCountInGroup++;

					MeshFace f = new MeshFace(face.Vertices.Count);
						
						for (int i = 0; i < face.Vertices.Count; i++)
						{
							f.Vertices[i].Index = faceIndices[i];
							if (face.Normals.Count > i)
							{
								f.Vertices[i].Normal = model.Normals[(int)face.Normals[i]];
							}
						}
						
						f.Material = 0;
						f.Flags |= FaceFlags.Face2Mask;
						if (face.Vertices.Count == 3)
						{
							f.Flags |= FaceFlags.Triangles;
						}
						builder.Faces.Add(f);
						
						if (model.Exporter >= ModelExporter.UnknownLeftHanded)
						{
							Array.Reverse(builder.Faces[builder.Faces.Count -1].Vertices, 0, builder.Faces[builder.Faces.Count -1].Vertices.Length);
						}
						lastMaterial = face.Material;
					}
				}
				// log final group
				if (builder.Faces.Count > 0)
				{
					Plugin.CurrentHost.AddMessage(MessageType.Information, false, "  MaterialGroup #" + materialGroupIndex + ": " + builder.Faces.Count + " faces");
				}
				Plugin.CurrentHost.AddMessage(MessageType.Information, false, "AssimpObjParser: total " + materialGroupIndex + " material groups built");

				// allow the intermediate parsed model to be collected before final mesh assembly
				model = null;
				parser = null;
				builder.Apply(ref obj);
				obj.Mesh.CreateNormals();
				return obj;
			}
			catch (Exception e)
			{
				Plugin.CurrentHost.AddMessage(MessageType.Error, false, e.Message + " in " + fileName);
				return null;
			}
		}
	}
}
