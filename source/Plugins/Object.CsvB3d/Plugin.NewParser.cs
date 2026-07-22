//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2026, Christopher Lees, The OpenBVE Project
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

using Formats.OpenBve;
using OpenBveApi;
using OpenBveApi.Colors;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Textures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Object.CsvB3d
{
	internal partial class NewParser
	{
		internal static StaticObject ReadObject(string fileName, Encoding textEncoding)
		{
			CSVB3DFile<CSVB3DSection, CSVB3DKey> objectFile = new CSVB3DFile<CSVB3DSection, CSVB3DKey>(fileName, Plugin.currentHost);
			string basePath = Path.GetDirectoryName(fileName);

			StaticObject staticObject = new StaticObject(Plugin.currentHost);
			MeshBuilder currentMeshBuilder = new MeshBuilder(Plugin.currentHost);
			List<Vector3> currentNormals = new List<Vector3>();
			Color32? lastTransparentColor = null;
			currentMeshBuilder.Materials[0].Color = Color32.White;

			while (objectFile.RemainingSubBlocks > 0)
			{
				CSVB3DBlock<CSVB3DSection, CSVB3DKey> subBlock = objectFile.ReadNextBlock();

				if (subBlock.Key == CSVB3DSection.CreateMeshBuilder)
				{
					/*
					 * NOTE: Only flush the MeshBuilder when encountering a new CreateMeshBuilder
					 * This allows Translate, Rotate etc. commands to be placed after the Texture section
					 * See also https://github.com/leezer3/OpenBVE/issues/448
					 */
					currentMeshBuilder.Apply(ref staticObject, Plugin.enabledHacks.BveTsHacks);
					currentMeshBuilder = new MeshBuilder(Plugin.currentHost);
				}

				while (subBlock.RemainingDataValues > 0)
				{
					CSVB3DKey key = subBlock.NextValue;
					switch (key)
					{
						case CSVB3DKey.Vertex:
							currentMeshBuilder.Vertices.Add(subBlock.GetNextVertex(out Vector3 currentNormal));
							currentNormals.Add(currentNormal);
							break;
						case CSVB3DKey.Face:
						case CSVB3DKey.Face2:
							if (subBlock.TryGetNextVertexIndexArray(out int[] faceVertices, Plugin.enabledHacks.BveTsHacks))
							{
								CheckForFaceHacks(fileName, currentMeshBuilder, staticObject, key == CSVB3DKey.Face2, ref faceVertices);
								MeshFace f = new MeshFace(faceVertices.Length);
								for (int j = 0; j < faceVertices.Length; j++)
								{
									f.Vertices[j].Index = faceVertices[j];
									if (faceVertices[j] < currentNormals.Count)
									{
										f.Vertices[j].Normal = currentNormals[faceVertices[j]];
									}
								}

								if (currentMeshBuilder.isCylinder && Plugin.enabledHacks.BveTsHacks && Plugin.enabledHacks.CylinderHack)
								{
									int l = f.Vertices.Length;
									(f.Vertices[l - 2], f.Vertices[l - 1]) = (f.Vertices[l - 1], f.Vertices[l - 2]);
								}

								if (key == CSVB3DKey.Face2)
								{
									f.Flags |= FaceFlags.Face2Mask;
								}

								currentMeshBuilder.Faces.Add(f);
							}
							break;
						case CSVB3DKey.Color:
						case CSVB3DKey.ColorAll:
						case CSVB3DKey.EmissiveColor:
						case CSVB3DKey.EmissiveColorAll:
							if (subBlock.GetNextColor32(out Color32 c))
							{
								if (key == CSVB3DKey.Color || key == CSVB3DKey.ColorAll)
								{
									CheckForColorHacks(fileName, currentMeshBuilder, staticObject, ref c);
								}

								currentMeshBuilder.ApplyColor(c, key == CSVB3DKey.EmissiveColor || key == CSVB3DKey.EmissiveColorAll);
								if (key == CSVB3DKey.ColorAll || key == CSVB3DKey.EmissiveColorAll)
								{
									staticObject.ApplyColor(c, key == CSVB3DKey.EmissiveColorAll);
								}
							}
							break;
						case CSVB3DKey.Translate:
						case CSVB3DKey.TranslateAll:
							if (subBlock.GetNextVector3(out Vector3 translationVector))
							{
								currentMeshBuilder.ApplyTranslation(translationVector);
								if (key == CSVB3DKey.TranslateAll)
								{
									staticObject.ApplyTranslation(translationVector);
								}
							}
							break;
						case CSVB3DKey.Cube:
							if (subBlock.GetNextVector3(out Vector3 cubeSize))
							{
								CreateCube(ref currentMeshBuilder, cubeSize);
							}
							break;
						case CSVB3DKey.Cylinder:
							if (subBlock.TryGetNextDoubleArray(out double[] cylinderProps, 4))
							{
								int numFaces = (int)cylinderProps[0];
								if (numFaces < 2)
								{
									Plugin.currentHost.AddMessage(MessageType.Error, false, "NumberOfFaces is expected to be at least 2 in " + subBlock.CurrentCommand + " at line " + subBlock.CurrentLine + " in file " + fileName);
									if (Plugin.enabledHacks.BveTsHacks)
									{
										// A cylinder with zero (or an empty) face count crashes BVE2 / BVE4
										// With one face, it's just not shown
										break;
									}

									numFaces = 8;
								}

								double upperRadius = cylinderProps.Length >= 2 ? cylinderProps[1] : 1.0;
								double lowerRadius = cylinderProps.Length >= 3 ? cylinderProps[2] : 1.0;
								double height = cylinderProps.Length >= 3 ? cylinderProps[3] : 1.0;
								CreateCylinder(ref currentMeshBuilder, numFaces, upperRadius, lowerRadius, height);
							}
							break;
						case CSVB3DKey.Rotate:
						case CSVB3DKey.RotateAll:
							if (subBlock.TryGetNextDoubleArray(out double[] rotateProps, 4) && rotateProps.Length == 4)
							{
								Vector3 rotationVector = new Vector3(rotateProps[0], rotateProps[1], rotateProps[2]);
								double t = rotationVector.NormSquared();
								if (t == 0.0)
								{
									rotationVector = Vector3.Right;
									t = 1.0;
								}

								if (Math.Abs(rotationVector.X) > 1 || Math.Abs(rotationVector.Y) > 1 || Math.Abs(rotationVector.Z) > 1)
								{
									Plugin.currentHost.AddMessage(MessageType.Warning, false, "Potentially incorrect rotational direction vector in " + subBlock.CurrentCommand + "- Angle should be the *last* argument at line " + subBlock.CurrentLine + " in file " + fileName);
								}

								if (rotateProps[3] != 0.0)
								{
									t = 1.0 / Math.Sqrt(t);
									rotationVector *= t;
									rotateProps[3] = rotateProps[3].ToRadians();
									currentMeshBuilder.ApplyRotation(rotationVector, rotateProps[3]);
									if (key == CSVB3DKey.RotateAll)
									{
										staticObject.ApplyRotation(rotationVector, rotateProps[3]);
									}
								}
							}
							break;
						case CSVB3DKey.Scale:
						case CSVB3DKey.ScaleAll:
							if (subBlock.GetNextVector3(out Vector3 scaleVector))
							{
								if (scaleVector.X == 0)
								{
									scaleVector.X = 1.0;
									Plugin.currentHost.AddMessage(MessageType.Error, false, "X is required to be different to zero in " + subBlock.CurrentCommand + " at line " + subBlock.CurrentLine + " in file " + fileName);
								}

								if (scaleVector.Y == 0)
								{
									scaleVector.Y = 1.0;
									Plugin.currentHost.AddMessage(MessageType.Error, false, "Y is required to be different to zero in " + subBlock.CurrentCommand + " at line " + subBlock.CurrentLine + " in file " + fileName);
								}

								if (scaleVector.Z == 0)
								{
									scaleVector.Z = 1.0;
									Plugin.currentHost.AddMessage(MessageType.Error, false, "Z is required to be different to zero in " + subBlock.CurrentCommand + " at line " + subBlock.CurrentLine + " in file " + fileName);
								}

								currentMeshBuilder.ApplyScale(scaleVector);
								if (key == CSVB3DKey.ScaleAll)
								{
									staticObject.ApplyScale(scaleVector);
								}
							}
							break;
						case CSVB3DKey.Shear:
						case CSVB3DKey.ShearAll:
							if (subBlock.GetNextShear(out Vector3 shearDirection, out Vector3 shearStress, out double shearRatio))
							{
								currentMeshBuilder.ApplyShear(shearDirection, shearStress, shearRatio);
								if (key == CSVB3DKey.ShearAll)
								{
									staticObject.ApplyShear(shearDirection, shearStress, shearRatio);
								}
							}
							break;
						case CSVB3DKey.Mirror:
						case CSVB3DKey.MirrorAll:
							if (subBlock.GetNextMirror(out Vector3 mirrorVertices, out Vector3 mirrorNormals))
							{
								currentMeshBuilder.ApplyMirror(mirrorVertices.X != 0, mirrorVertices.Y != 0, mirrorVertices.Z != 0, mirrorNormals.X != 0, mirrorNormals.Y != 0, mirrorNormals.Z != 0);
								if (key == CSVB3DKey.MirrorAll)
								{
									staticObject.ApplyMirror(mirrorVertices.X != 0, mirrorVertices.Y != 0, mirrorVertices.Z != 0, mirrorNormals.X != 0, mirrorNormals.Y != 0, mirrorNormals.Z != 0);
								}
							}

							break;
						case CSVB3DKey.Load:
							if (subBlock.GetNextTexturePath(basePath, out string tDay, out string tNight))
							{
								OpenBveApi.Objects.Helpers.TextureMapType mapType = OpenBveApi.Objects.Helpers.TextureMapType.None;
								bool isMapType = !string.IsNullOrEmpty(tDay) &&
									OpenBveApi.Objects.Helpers.TextureMapConvention.TryGetMapType(tDay, out mapType);

								if (isMapType && mapType != OpenBveApi.Objects.Helpers.TextureMapType.Emissive)
								{
									switch (mapType)
									{
									case OpenBveApi.Objects.Helpers.TextureMapType.Normal:
										currentMeshBuilder.Materials[0].NormalMap = tDay;
										break;
									case OpenBveApi.Objects.Helpers.TextureMapType.NormalDirectX:
										currentMeshBuilder.Materials[0].NormalMap = tDay;
										currentMeshBuilder.Materials[0].NormalMapIsDirectX = true;
										break;
									case OpenBveApi.Objects.Helpers.TextureMapType.AmbientOcclusion:
										currentMeshBuilder.Materials[0].AmbientOcclusionMap = tDay;
										break;
									case OpenBveApi.Objects.Helpers.TextureMapType.ORM:
										currentMeshBuilder.Materials[0].AmbientOcclusionMap = tDay;
										currentMeshBuilder.Materials[0].AmbientOcclusionMapIsORM = true;
										break;
									case OpenBveApi.Objects.Helpers.TextureMapType.Roughness:
									case OpenBveApi.Objects.Helpers.TextureMapType.Metallic:
									case OpenBveApi.Objects.Helpers.TextureMapType.MetallicRoughness:
										currentMeshBuilder.Materials[0].MetallicRoughnessMap = tDay;
										currentMeshBuilder.Materials[0].Flags |= MaterialFlags.HasMetallicRoughness;
										break;
									}
								}
								else
								{
									currentMeshBuilder.Materials[0].DaytimeTexture = tDay;
									currentMeshBuilder.Materials[0].NighttimeTexture = tNight;

									if (isMapType)
									{
										if ((currentMeshBuilder.Materials[0].Flags & MaterialFlags.Emissive) != 0)
										{
											Plugin.currentHost.AddMessage(MessageType.Warning, false, "Emissive texture '" + tDay + "' ignored; legacy EmissiveColor takes precedence in the same meshbuilder section at line " + subBlock.CurrentLine + " in file " + fileName);
										}
									}
								}
							}
							else
							{
								if (CheckForTextureHacks(fileName, ref tDay))
								{
									currentMeshBuilder.Materials[0].DaytimeTexture = tDay;
								}
							}

							if (!string.IsNullOrEmpty(currentMeshBuilder.Materials[0].DaytimeTexture) && currentMeshBuilder.Materials[0].AmbientOcclusionMap == null)
							{
								string ao = TryFindSiblingMap(currentMeshBuilder.Materials[0].DaytimeTexture, new[] { "_orm", "_arm", "_ao", "_ambientocclusion" });
								if (ao != null)
								{
									currentMeshBuilder.Materials[0].AmbientOcclusionMap = ao;
									currentMeshBuilder.Materials[0].AmbientOcclusionMapIsORM = OpenBveApi.Objects.Helpers.TextureMapConvention.TryGetMapType(ao, out OpenBveApi.Objects.Helpers.TextureMapType aoType) && aoType == OpenBveApi.Objects.Helpers.TextureMapType.ORM;
								}
							}
							break;
						case CSVB3DKey.LoadLightMap:
							if (subBlock.GetNextPath(basePath, out string lightMap))
							{
								currentMeshBuilder.Materials[0].LightMap = lightMap;
							}
							break;
						case CSVB3DKey.Coordinates:
							if (subBlock.GetNextTextureCoordinates(out int idx, out Vector2 textureCoordinates, out bool hasLightMap, out Vector2 lightMapCoordinates))
							{
								if (idx >= currentMeshBuilder.Vertices.Count)
								{
									Plugin.currentHost.AddMessage(MessageType.Error, false, "Invalid vertex index in command " + subBlock.CurrentCommand + " at line " + subBlock.CurrentLine + " in file " + fileName);
									break;
								}

								if (hasLightMap)
								{
									currentMeshBuilder.Vertices[idx] = new LightMappedVertex(currentMeshBuilder.Vertices[idx].Coordinates, textureCoordinates, lightMapCoordinates);
								}
								else
								{
									currentMeshBuilder.Vertices[idx].TextureCoordinates = textureCoordinates;
								}
							}
							break;
						case CSVB3DKey.Transparent:
							if (!subBlock.GetNextColor32(out c))
							{
								if (lastTransparentColor != null && Plugin.enabledHacks.BveTsHacks)
								{
									// BVE2 / BVE4 use the *last* transparent color if none is set
									c = (Color24)lastTransparentColor;
								}
								else
								{
									break;
								}
							}

							currentMeshBuilder.Materials[0].TransparentColor = (Color24)c;
							currentMeshBuilder.Materials[0].Flags |= MaterialFlags.TransparentColor;
							lastTransparentColor = c;
							break;
						case CSVB3DKey.BlendMode:
							if (subBlock.GetBlendMode(out MeshMaterialBlendMode blendMode, out double glowHalfDistance, out GlowAttenuationMode glowMode))
							{
								currentMeshBuilder.Materials[0].BlendMode = blendMode;
								currentMeshBuilder.Materials[0].GlowAttenuationData = Glow.GetAttenuationData(glowHalfDistance, glowMode);
							}
							break;
						case CSVB3DKey.WrapMode:
							if (subBlock.GetNextEnumValue(out OpenGlTextureWrapMode wrapMode))
							{
								currentMeshBuilder.Materials[0].WrapMode = wrapMode;
							}
							break;
						case CSVB3DKey.Text:
							if (subBlock.GetNextRawValue(out string text))
							{
								currentMeshBuilder.Materials[0].Text = text;
							}
							break;
						case CSVB3DKey.TextColor:
							if (subBlock.GetNextColor32(out c))
							{
								currentMeshBuilder.Materials[0].TextColor = c;
							}
							break;
						case CSVB3DKey.TextPadding:
							if (subBlock.GetNextVector2(out Vector2 textPadding))
							{
								currentMeshBuilder.Materials[0].TextPadding = textPadding;
							}
							break;
						case CSVB3DKey.DisableShadowCasting:
							if (subBlock.GetNextBool(out bool disableShadowCasting) && disableShadowCasting)
							{
								currentMeshBuilder.Materials[0].Flags |= MaterialFlags.NoShadow;
							}
							break;
						default:
							subBlock.SkipNextValue();
							break;
					}
				}


			}
			TryAutoDiscoverTextures(fileName, basePath, ref currentMeshBuilder);
			currentMeshBuilder.Apply(ref staticObject, Plugin.enabledHacks.BveTsHacks);
			staticObject.Mesh.CreateNormals();
			return staticObject;
		}

		private static void TryAutoDiscoverTextures(string b3dFilePath, string basePath, ref MeshBuilder meshBuilder)
		{
			if (meshBuilder.Materials[0].DaytimeTexture != null)
			{
				return;
			}

			string directory = System.IO.Path.GetDirectoryName(b3dFilePath);
			string baseName = System.IO.Path.GetFileNameWithoutExtension(b3dFilePath);

			if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(baseName))
			{
				return;
			}

			string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".dds" };
			string[] resolutionSuffixes = { "_1k", "_2k", "_4k", "_8k", "_16k", "_512", "_1024", "_2048", "_4096" };

			foreach (string file in System.IO.Directory.GetFiles(directory))
			{
				string ext = System.IO.Path.GetExtension(file);
				if (!imageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
				{
					continue;
				}

				string nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(file);
				if (string.IsNullOrEmpty(nameWithoutExt) || !nameWithoutExt.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				string suffix = nameWithoutExt.Substring(baseName.Length);
				if (string.IsNullOrEmpty(suffix))
				{
					if (meshBuilder.Materials[0].DaytimeTexture == null)
					{
						meshBuilder.Materials[0].DaytimeTexture = file;
					}
					continue;
				}

				foreach (string res in resolutionSuffixes)
				{
					if (suffix.EndsWith(res, StringComparison.OrdinalIgnoreCase))
					{
						suffix = suffix.Substring(0, suffix.Length - res.Length);
						break;
					}
				}

				// Build a virtual name with the resolution suffix stripped for map-type matching
				string strippedName = baseName + suffix;
				string strippedFile = System.IO.Path.Combine(directory, strippedName + ext);

				if (OpenBveApi.Objects.Helpers.TextureMapConvention.TryGetMapType(strippedFile, out OpenBveApi.Objects.Helpers.TextureMapType mapType))
				{
					switch (mapType)
					{
					case OpenBveApi.Objects.Helpers.TextureMapType.Normal:
						if (meshBuilder.Materials[0].NormalMap == null)
						{
							meshBuilder.Materials[0].NormalMap = file;
						}
						break;
					case OpenBveApi.Objects.Helpers.TextureMapType.NormalDirectX:
						if (meshBuilder.Materials[0].NormalMap == null)
						{
							meshBuilder.Materials[0].NormalMap = file;
							meshBuilder.Materials[0].NormalMapIsDirectX = true;
						}
						break;
					case OpenBveApi.Objects.Helpers.TextureMapType.ORM:
						if (meshBuilder.Materials[0].AmbientOcclusionMap == null)
						{
							meshBuilder.Materials[0].AmbientOcclusionMap = file;
							meshBuilder.Materials[0].AmbientOcclusionMapIsORM = true;
						}
						break;
					case OpenBveApi.Objects.Helpers.TextureMapType.AmbientOcclusion:
						if (meshBuilder.Materials[0].AmbientOcclusionMap == null)
						{
							meshBuilder.Materials[0].AmbientOcclusionMap = file;
						}
						break;
					case OpenBveApi.Objects.Helpers.TextureMapType.Roughness:
					case OpenBveApi.Objects.Helpers.TextureMapType.Metallic:
					case OpenBveApi.Objects.Helpers.TextureMapType.MetallicRoughness:
						if (meshBuilder.Materials[0].MetallicRoughnessMap == null)
						{
							meshBuilder.Materials[0].MetallicRoughnessMap = file;
							meshBuilder.Materials[0].Flags |= MaterialFlags.HasMetallicRoughness;
						}
						break;
					}
				}
				else
				{
					if (meshBuilder.Materials[0].DaytimeTexture == null)
					{
						meshBuilder.Materials[0].DaytimeTexture = file;
					}
				}
			}
		}

		/// <summary>
		/// Searches for a sibling map texture by inserting a recognised suffix before the file extension.
		/// Returns the first existing candidate path, or null if none are found.
		/// </summary>
		private static string TryFindSiblingMap(string baseTexture, string[] suffixes)
		{
			if (string.IsNullOrEmpty(baseTexture))
			{
				return null;
			}

			string directory = System.IO.Path.GetDirectoryName(baseTexture);
			string name = System.IO.Path.GetFileNameWithoutExtension(baseTexture);
			string extension = System.IO.Path.GetExtension(baseTexture);
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}

			foreach (string suffix in suffixes)
			{
				string candidate = string.IsNullOrEmpty(directory)
					? name + suffix + extension
					: System.IO.Path.Combine(directory, name + suffix + extension);
				if (System.IO.File.Exists(candidate))
				{
					return candidate;
				}
			}

			return null;
		}
	}
}
