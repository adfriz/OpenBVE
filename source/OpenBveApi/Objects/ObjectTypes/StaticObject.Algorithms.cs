using System;
using System.Collections.Generic;
using System.Linq;
using OpenBveApi.Colors;
using OpenBveApi.Math;
using OpenBveApi.World;

namespace OpenBveApi.Objects
{
	public partial class StaticObject
	{
		/// <summary>Creates a mirrored clone of this object</summary>
		public override UnifiedObject Mirror()
		{
			StaticObject mirrorResult = (StaticObject)Clone();
			for (int i = 0; i < mirrorResult.Mesh.Vertices.Length; i++)
			{
				mirrorResult.Mesh.Vertices[i].Coordinates.X = -mirrorResult.Mesh.Vertices[i].Coordinates.X;
			}
			for (int i = 0; i < mirrorResult.Mesh.Faces.Length; i++)
			{
				for (int k = 0; k < mirrorResult.Mesh.Faces[i].Vertices.Length; k++)
				{
					mirrorResult.Mesh.Faces[i].Vertices[k].Normal.X = -mirrorResult.Mesh.Faces[i].Vertices[k].Normal.X;
				}
				mirrorResult.Mesh.Faces[i].Flip();
			}
			mirrorResult.isOptimized = isOptimized;
			return mirrorResult;
		}

		/// <inheritdoc/>
		public override UnifiedObject Transform(double nearDistance, double farDistance)
		{
			StaticObject transformResult = (StaticObject)this.Clone();
			int n = 0;
			double x2 = 0.0, x3 = 0.0, x6 = 0.0, x7 = 0.0;
			for (int i = 0; i < transformResult.Mesh.Vertices.Length; i++)
			{
				if (n == 2)
				{
					x2 = transformResult.Mesh.Vertices[i].Coordinates.X;
				}
				else if (n == 3)
				{
					x3 = transformResult.Mesh.Vertices[i].Coordinates.X;
				}
				else if (n == 6)
				{
					x6 = transformResult.Mesh.Vertices[i].Coordinates.X;
				}
				else if (n == 7)
				{
					x7 = transformResult.Mesh.Vertices[i].Coordinates.X;
				}
				n++;
				if (n == 8)
				{
					break;
				}
			}
			if (n >= 4)
			{
				int m = 0;
				for (int i = 0; i < transformResult.Mesh.Vertices.Length; i++)
				{
					if (m == 0)
					{
						transformResult.Mesh.Vertices[i].Coordinates.X = nearDistance - x3;
					}
					else if (m == 1)
					{
						transformResult.Mesh.Vertices[i].Coordinates.X = farDistance - x2;
						if (n < 8)
						{
							break;
						}
					}
					else if (m == 4)
					{
						transformResult.Mesh.Vertices[i].Coordinates.X = nearDistance - x7;
					}
					else if (m == 5)
					{
						transformResult.Mesh.Vertices[i].Coordinates.X = farDistance - x6;
						break;
					}
					m++;
					if (m == 8)
					{
						break;
					}
				}
			}
			return transformResult;
		}

		/// <inheritdoc/>
		public override UnifiedObject TransformLeft(double nearDistance, double farDistance)
		{
			bool vertical = true;
			double zPos = Mesh.Vertices[0].Coordinates.Z;
			double minX = double.MaxValue, maxX = double.MinValue;
			for (int i = 0; i < Mesh.Vertices.Length; i++)
			{
				minX = System.Math.Min(Mesh.Vertices[i].Coordinates.X, minX);
				maxX = System.Math.Max(Mesh.Vertices[i].Coordinates.X, maxX);
				if (System.Math.Abs(Mesh.Vertices[i].Coordinates.Z - zPos) > 0.1)
				{
					vertical = false;
				}
			}

			StaticObject transformResult = (StaticObject)Clone();

			if (vertical || System.Math.Abs(nearDistance - farDistance) > 0.1)
			{
				double width = maxX - minX;
				transformResult.ApplyScale(width / (nearDistance + width), 1, 1);
				return transformResult;
			}

			for (int i = 0; i < Mesh.Vertices.Length; i += 4)
			{
				List<VertexTemplate> tempList = Mesh.Vertices.Skip(i).Take(4).ToList();
				int bottomLeft = tempList.IndexOf(tempList.OrderByDescending(c => c.Coordinates.Z).ThenBy(c => c.Coordinates.X).First());
				int bottomRight = tempList.IndexOf(tempList.OrderByDescending(c => c.Coordinates.Z).ThenByDescending(c => c.Coordinates.X).First());
				int topRight = tempList.IndexOf(tempList.OrderBy(c => c.Coordinates.Z).ThenByDescending(c => c.Coordinates.X).First());
				int topLeft = tempList.IndexOf(tempList.OrderBy(c => c.Coordinates.Z).ThenBy(c => c.Coordinates.X).First());

				transformResult.Mesh.Vertices[i + bottomRight].Coordinates.X = farDistance - transformResult.Mesh.Vertices[i + bottomLeft].Coordinates.X;
				transformResult.Mesh.Vertices[i + topRight].Coordinates.X = nearDistance - transformResult.Mesh.Vertices[i + topLeft].Coordinates.X;
			}

			return transformResult;
		}

		/// <inheritdoc/>
		public override UnifiedObject TransformRight(double nearDistance, double farDistance)
		{
			bool vertical = true;
			double zPos = Mesh.Vertices[0].Coordinates.Z;
			double minX = double.MaxValue, maxX = double.MinValue;
			for (int i = 0; i < Mesh.Vertices.Length; i++)
			{
				minX = System.Math.Min(Mesh.Vertices[i].Coordinates.X, minX);
				maxX = System.Math.Max(Mesh.Vertices[i].Coordinates.X, maxX);
				if (System.Math.Abs(Mesh.Vertices[i].Coordinates.Z - zPos) > 0.1)
				{
					vertical = false;
				}
			}

			StaticObject transformResult = (StaticObject)Clone();

			if (vertical || System.Math.Abs(nearDistance - farDistance) > 0.1)
			{
				double width = maxX - minX;
				transformResult.ApplyScale(width / (nearDistance + width), 1, 1);
				return transformResult;
			}

			for (int i = 0; i < Mesh.Vertices.Length; i += 4)
			{
				List<VertexTemplate> tempList = Mesh.Vertices.Skip(i).Take(4).ToList();
				int bottomLeft = tempList.IndexOf(tempList.OrderByDescending(c => c.Coordinates.Z).ThenBy(c => c.Coordinates.X).First());
				int bottomRight = tempList.IndexOf(tempList.OrderByDescending(c => c.Coordinates.Z).ThenByDescending(c => c.Coordinates.X).First());
				int topRight = tempList.IndexOf(tempList.OrderBy(c => c.Coordinates.Z).ThenByDescending(c => c.Coordinates.X).First());
				int topLeft = tempList.IndexOf(tempList.OrderBy(c => c.Coordinates.Z).ThenBy(c => c.Coordinates.X).First());

				transformResult.Mesh.Vertices[i + bottomLeft].Coordinates.X = farDistance - transformResult.Mesh.Vertices[i + bottomRight].Coordinates.X;
				transformResult.Mesh.Vertices[i + topLeft].Coordinates.X = nearDistance - transformResult.Mesh.Vertices[i + topRight].Coordinates.X;
			}

			return transformResult;
		}

		/// <summary>Joins two static objects</summary>
		public void JoinObjects(StaticObject additionalObject, Matrix4D[] animationMatrices = null)
		{
			if (additionalObject == null)
			{
				return;
			}

			int mf = Mesh.Faces.Length;
			int mm = Mesh.Materials.Length;
			int mv = Mesh.Vertices.Length;
			Array.Resize(ref Mesh.Faces, mf + additionalObject.Mesh.Faces.Length);
			Array.Resize(ref Mesh.Materials, mm + additionalObject.Mesh.Materials.Length);
			Array.Resize(ref Mesh.Vertices, mv + additionalObject.Mesh.Vertices.Length);
			for (int i = 0; i < additionalObject.Mesh.Faces.Length; i++)
			{
				Mesh.Faces[mf + i] = additionalObject.Mesh.Faces[i];
				for (int j = 0; j < Mesh.Faces[mf + i].Vertices.Length; j++)
				{
					Mesh.Faces[mf + i].Vertices[j].Index += mv;
				}

				Mesh.Faces[mf + i].Material += (ushort) mm;
			}

			for (int i = 0; i < additionalObject.Mesh.Materials.Length; i++)
			{
				Mesh.Materials[mm + i] = additionalObject.Mesh.Materials[i];
			}

			for (int i = 0; i < additionalObject.Mesh.Vertices.Length; i++)
			{
				if (additionalObject.Mesh.Vertices[i] is AnimatedVertex av)
				{
					Vector3 transformedCoordinates = new Vector3(av.Coordinates);
					for (int j = 0; j < av.MatrixChain.Length; j++)
					{
						if (animationMatrices != null && av.MatrixChain[j] >= 0 && av.MatrixChain[j] < 255)
						{
							transformedCoordinates.Transform(animationMatrices[av.MatrixChain[j]], false);
						}
					}
					Mesh.Vertices[mv + i] = new Vertex(transformedCoordinates, av.TextureCoordinates);
				}
				else
				{
					Mesh.Vertices[mv + i] = additionalObject.Mesh.Vertices[i].Clone();
				}
			}
		}

		/// <summary>Applies scale</summary>
		public void ApplyScale(Vector3 scale)
		{
			ApplyScale(scale.X, scale.Y, scale.Z);
		}

		/// <summary>Applies scale</summary>
		public void ApplyScale(double x, double y, double z)
		{
			float rx = (float) (1.0 / x);
			float ry = (float) (1.0 / y);
			float rz = (float) (1.0 / z);
			float rx2 = rx * rx;
			float ry2 = ry * ry;
			float rz2 = rz * rz;
			bool reverse = x * y * z < 0.0;
			for (int j = 0; j < Mesh.Vertices.Length; j++)
			{
				Mesh.Vertices[j].Coordinates.X *= x;
				Mesh.Vertices[j].Coordinates.Y *= y;
				Mesh.Vertices[j].Coordinates.Z *= z;
			}

			for (int j = 0; j < Mesh.Faces.Length; j++)
			{
				for (int k = 0; k < Mesh.Faces[j].Vertices.Length; k++)
				{
					double nx2 = Mesh.Faces[j].Vertices[k].Normal.X * Mesh.Faces[j].Vertices[k].Normal.X;
					double ny2 = Mesh.Faces[j].Vertices[k].Normal.Y * Mesh.Faces[j].Vertices[k].Normal.Y;
					double nz2 = Mesh.Faces[j].Vertices[k].Normal.Z * Mesh.Faces[j].Vertices[k].Normal.Z;
					double u = nx2 * rx2 + ny2 * ry2 + nz2 * rz2;
					if (u != 0.0)
					{
						u = (float) System.Math.Sqrt((nx2 + ny2 + nz2) / u);
						Mesh.Faces[j].Vertices[k].Normal.X *= rx * u;
						Mesh.Faces[j].Vertices[k].Normal.Y *= ry * u;
						Mesh.Faces[j].Vertices[k].Normal.Z *= rz * u;
					}
				}
			}

			if (reverse)
			{
				for (int j = 0; j < Mesh.Faces.Length; j++)
				{
					Mesh.Faces[j].Flip();
				}
			}
		}

		/// <summary>Applies rotation</summary>
		public void ApplyRotation(Vector3 rotationVector, double angle)
		{
			for (int j = 0; j < Mesh.Vertices.Length; j++)
			{
				Mesh.Vertices[j].Coordinates.Rotate(rotationVector, angle);
			}

			for (int j = 0; j < Mesh.Faces.Length; j++)
			{
				for (int k = 0; k < Mesh.Faces[j].Vertices.Length; k++)
				{
					Mesh.Faces[j].Vertices[k].Normal.Rotate(rotationVector, angle);
				}
			}
		}

		/// <summary>Applies translation</summary>
		public override void ApplyTranslation(double x, double y, double z, bool absoluteTranslation = false)
		{
			for (int i = 0; i < Mesh.Vertices.Length; i++)
			{
				Mesh.Vertices[i].Coordinates.X += x;
				Mesh.Vertices[i].Coordinates.Y += y;
				Mesh.Vertices[i].Coordinates.Z += z;
			}
		}

		/// <summary>Applies mirroring</summary>
		public void ApplyMirror(bool vX, bool vY, bool vZ, bool nX, bool nY, bool nZ)
		{
			for (int i = 0; i < Mesh.Vertices.Length; i++)
			{
				if (vX) Mesh.Vertices[i].Coordinates.X *= -1;
				if (vY) Mesh.Vertices[i].Coordinates.Y *= -1;
				if (vZ) Mesh.Vertices[i].Coordinates.Z *= -1;
			}

			for (int i = 0; i < Mesh.Faces.Length; i++)
			{
				for (int j = 0; j < Mesh.Faces[i].Vertices.Length; j++)
				{
					if (nX) Mesh.Faces[i].Vertices[j].Normal.X *= -1;
					if (nY) Mesh.Faces[i].Vertices[j].Normal.Y *= -1;
					if (nZ) Mesh.Faces[i].Vertices[j].Normal.Z *= -1;
				}
			}

			int numFlips = (vX ? 1 : 0) + (vY ? 1 : 0) + (vZ ? 1 : 0);
			if (numFlips % 2 != 0)
			{
				for (int i = 0; i < Mesh.Faces.Length; i++)
				{
					Array.Reverse(Mesh.Faces[i].Vertices);
				}
			}
		}

		/// <summary>Applies a color to all materials in the mesh</summary>
		public void ApplyColor(Color32 newColor, bool emissive)
		{
			for (int i = 0; i < Mesh.Materials.Length; i++)
			{
				if (emissive)
				{
					Mesh.Materials[i].EmissiveColor = newColor;
					Mesh.Materials[i].Flags |= MaterialFlags.Emissive;
				}
				else
				{
					Mesh.Materials[i].Color = newColor;
				}
			}
		}

		/// <summary>Performs shear mapping</summary>
		public void ApplyShear(Vector3 shearDirection, Vector3 shear, double ratio)
		{
			for (int j = 0; j < Mesh.Vertices.Length; j++)
			{
				double n = ratio * (shearDirection.X * Mesh.Vertices[j].Coordinates.X + shearDirection.Y * Mesh.Vertices[j].Coordinates.Y + shearDirection.Z * Mesh.Vertices[j].Coordinates.Z);
				Mesh.Vertices[j].Coordinates += shear * n;
			}

			for (int j = 0; j < Mesh.Faces.Length; j++)
			{
				for (int k = 0; k < Mesh.Faces[j].Vertices.Length; k++)
				{
					if (Mesh.Faces[j].Vertices[k].Normal.X != 0.0f | Mesh.Faces[j].Vertices[k].Normal.Y != 0.0f | Mesh.Faces[j].Vertices[k].Normal.Z != 0.0f)
					{
						double n = ratio * (shear.X * Mesh.Faces[j].Vertices[k].Normal.X + shear.Y * Mesh.Faces[j].Vertices[k].Normal.Y + shear.Z * Mesh.Faces[j].Vertices[k].Normal.Z);
						Mesh.Faces[j].Vertices[k].Normal -= shearDirection * n;
						Mesh.Faces[j].Vertices[k].Normal.Normalize();
					}
				}
			}
		}

		/// <inheritdoc />
		public override void OptimizeObject(bool preserveVerticies, int faceThreshold, bool vertexCulling)
		{
			if (isOptimized)
			{
				return;
			}
			isOptimized = true;
			int m = Mesh.Materials.Length;
			int f = Mesh.Faces.Length;
			
			if (m >= f / 500 && f >= faceThreshold && f < 20000 && currentHost.Platform != HostPlatform.AppleOSX)
			{
				return;
			}

			if (Mesh.Vertices.Length > 10000)
			{
				preserveVerticies = true;
			}

			for (int i = 0; i < f; i++)
			{
				FaceFlags type = Mesh.Faces[i].Flags & FaceFlags.FaceTypeMask;
				bool keep;
				switch (type)
				{
					case FaceFlags.Triangles:
						keep = Mesh.Faces[i].Vertices.Length >= 3;
						if (keep)
						{
							int n = (Mesh.Faces[i].Vertices.Length / 3) * 3;
							if (Mesh.Faces[i].Vertices.Length != n)
							{
								Array.Resize(ref Mesh.Faces[i].Vertices, n);
							}
						}
						break;
					case FaceFlags.Quads:
						keep = Mesh.Faces[i].Vertices.Length >= 4;
						if (keep)
						{
							int n = Mesh.Faces[i].Vertices.Length & ~3;
							if (Mesh.Faces[i].Vertices.Length != n)
							{
								Array.Resize(ref Mesh.Faces[i].Vertices, n);
							}
						}
						break;
					case FaceFlags.QuadStrip:
						keep = Mesh.Faces[i].Vertices.Length >= 4;
						if (keep)
						{
							int n = Mesh.Faces[i].Vertices.Length & ~1;
							if (Mesh.Faces[i].Vertices.Length != n)
							{
								Array.Resize(ref Mesh.Faces[i].Vertices, n);
							}
						}
						break;
					default:
						keep = Mesh.Faces[i].Vertices.Length >= 3;
						break;
				}

				if (!keep)
				{
					for (int j = i; j < f - 1; j++)
					{
						Mesh.Faces[j] = Mesh.Faces[j + 1];
					}

					f--;
					i--;
				}
			}

			bool[] materialUsed = new bool[m];
			for (int i = 0; i < f; i++)
			{
				materialUsed[Mesh.Faces[i].Material] = true;
			}

			for (int i = 0; i < m; i++)
			{
				if (!materialUsed[i])
				{
					for (int j = 0; j < f; j++)
					{
						if (Mesh.Faces[j].Material > i)
						{
							Mesh.Faces[j].Material--;
						}
					}

					for (int j = i; j < m - 1; j++)
					{
						Mesh.Materials[j] = Mesh.Materials[j + 1];
						materialUsed[j] = materialUsed[j + 1];
					}

					m--;
					i--;
				}
			}

			for (int i = 0; i < m - 1; i++)
			{
				for (int j = i + 1; j < m; j++)
				{
					if (Mesh.Materials[i] == Mesh.Materials[j])
					{
						for (int k = 0; k < f; k++)
						{
							if (Mesh.Faces[k].Material == j)
							{
								Mesh.Faces[k].Material = (ushort) i;
							}
							else if (Mesh.Faces[k].Material > j)
							{
								Mesh.Faces[k].Material--;
							}
						}

						for (int k = j; k < m - 1; k++)
						{
							Mesh.Materials[k] = Mesh.Materials[k + 1];
						}

						m--;
						j--;
					}
				}
			}

			if (!preserveVerticies && vertexCulling)
			{
				Dictionary<VertexTemplate, int> uniqueVertices = new Dictionary<VertexTemplate, int>();
				VertexTemplate[] newVertices = new VertexTemplate[Mesh.Vertices.Length];
				int count = 0;

				for (int i = 0; i < Mesh.Faces.Length; i++)
				{
					for (int j = 0; j < Mesh.Faces[i].Vertices.Length; j++)
					{
						int oldIndex = Mesh.Faces[i].Vertices[j].Index;
						VertexTemplate vertex = Mesh.Vertices[oldIndex];

						if (!uniqueVertices.TryGetValue(vertex, out int newIndex))
						{
							newIndex = count;
							uniqueVertices.Add(vertex, newIndex);
							newVertices[count] = vertex;
							count++;
						}

						Mesh.Faces[i].Vertices[j].Index = newIndex;
					}
				}

				Mesh.Vertices = new VertexTemplate[count];
				Array.Copy(newVertices, 0, Mesh.Vertices, 0, count);
			}

			for (int i = 0; i < f; ++i)
			{
				FaceFlags type = Mesh.Faces[i].Flags & FaceFlags.FaceTypeMask;
				if (type == FaceFlags.Quads || type == FaceFlags.Polygon)
				{
					int startingVertexCount = Mesh.Faces[i].Vertices.Length;
					int triCount = (startingVertexCount - 2);
					int vertexCount = triCount * 3;

					MeshFaceVertex[] originalPoly = (MeshFaceVertex[]) Mesh.Faces[i].Vertices.Clone();
					Array.Resize(ref Mesh.Faces[i].Vertices, vertexCount);
					MeshFaceVertex[] outVerts = Mesh.Faces[i].Vertices;

					for (int triIndex = 0, vertIndex = 0, oldVert = 2; triIndex < triCount; ++triIndex, ++oldVert)
					{
						outVerts[vertIndex] = originalPoly[0];
						vertIndex += 1;
						outVerts[vertIndex] = originalPoly[oldVert - 1];
						vertIndex += 1;
						outVerts[vertIndex] = originalPoly[oldVert];
						vertIndex += 1;
					}

					Mesh.Faces[i].Flags &=  ~FaceFlags.FaceTypeMask;
					Mesh.Faces[i].Flags |= FaceFlags.Triangles;
				}
			}

			for (int i = 0; i < f; i++)
			{
				FaceFlags type = Mesh.Faces[i].Flags & FaceFlags.FaceTypeMask;
				int faceCount = 0;
				FaceFlags faceBit = 0;
				if (type == FaceFlags.Triangles)
				{
					faceCount = 3;
					faceBit = FaceFlags.Triangles;
				}
				else if (type == FaceFlags.Quads)
				{
					faceCount = 4;
					faceBit = FaceFlags.Triangles;
				}

				if (faceCount == 3 || faceCount == 4)
				{
					if (Mesh.Faces[i].Vertices.Length > faceCount)
					{
						int n = (Mesh.Faces[i].Vertices.Length - faceCount) / faceCount;
						while (f + n > Mesh.Faces.Length)
						{
							Array.Resize(ref Mesh.Faces, Mesh.Faces.Length << 1);
						}

						for (int j = 0; j < n; j++)
						{
							Mesh.Faces[f + j].Vertices = new MeshFaceVertex[faceCount];
							for (int k = 0; k < faceCount; k++)
							{
								Mesh.Faces[f + j].Vertices[k] = Mesh.Faces[i].Vertices[faceCount + faceCount * j + k];
							}

							Mesh.Faces[f + j].Material = Mesh.Faces[i].Material;
							Mesh.Faces[f + j].Flags = Mesh.Faces[i].Flags;
							Mesh.Faces[i].Flags &= ~FaceFlags.FaceTypeMask;
							Mesh.Faces[i].Flags |= faceBit;
						}

						Array.Resize(ref Mesh.Faces[i].Vertices, faceCount);
						f += n;
					}
				}
			}

			{
				bool[] canMerge = new bool[f];
				for (int i = 0; i < f - 1; ++i)
				{
					int mergeVertices = 0;
					FaceFlags type = Mesh.Faces[i].Flags & FaceFlags.FaceTypeMask;
					FaceFlags face = Mesh.Faces[i].Flags & FaceFlags.Face2Mask;

					for (int j = i + 1; j < f; ++j)
					{
						FaceFlags type2 = Mesh.Faces[j].Flags & FaceFlags.FaceTypeMask;
						FaceFlags face2 = Mesh.Faces[j].Flags & FaceFlags.Face2Mask;

						bool mergeable = (type == FaceFlags.Triangles) &&
						                 (type == type2) &&
						                 (face == face2) &&
						                 (Mesh.Faces[i].Material == Mesh.Faces[j].Material);

						canMerge[j] = mergeable;
						mergeVertices += mergeable ? Mesh.Faces[j].Vertices.Length : 0;
					}

					if (mergeVertices == 0)
					{
						continue;
					}

					int lastVertexIt = Mesh.Faces[i].Vertices.Length;
					Array.Resize(ref Mesh.Faces[i].Vertices, lastVertexIt + mergeVertices);

					for (int j = i + 1; j < f; ++j)
					{
						if (canMerge[j])
						{
							Mesh.Faces[j].Vertices.CopyTo(Mesh.Faces[i].Vertices, lastVertexIt);
							lastVertexIt += Mesh.Faces[j].Vertices.Length;
						}
					}

					int jump = 0;
					for (int j = i + 1; j < f; ++j)
					{
						if (canMerge[j])
						{
							jump += 1;
						}
						else if (jump > 0)
						{
							Mesh.Faces[j - jump] = Mesh.Faces[j];
						}
					}

					f -= jump;
				}
			}

			if (m != Mesh.Materials.Length)
			{
				Array.Resize(ref Mesh.Materials, m);
			}

			if (f != Mesh.Faces.Length)
			{
				Array.Resize(ref Mesh.Faces, f);
			}
		}
	}
}
