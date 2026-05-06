using System;
using System.Collections.Generic;
using OpenBveApi.Colors;
using OpenBveApi.Hosts;
using OpenBveApi.Math;
using OpenBveApi.World;


namespace OpenBveApi.Objects
{
	/// <summary>Represents a static (e.g. non-animated) object within the world</summary>
	/// <inheritdoc />
	public class StaticObject : UnifiedObject
	{
		/// <summary>Whether the object is optimized</summary>
		private bool isOptimized;
		/// <summary>The mesh of the object</summary>
		public Mesh Mesh;
		/// <summary>The starting track position, for static objects only.</summary>
		public float StartingTrackDistance;
		/// <summary>The ending track position, for static objects only.</summary>
		public float EndingTrackDistance;
		/// <summary>Whether the object is dynamic, i.e. not static.</summary>
		public bool Dynamic;
		/// <summary> Stores the author for this object.</summary>
		public string Author;
		/// <summary> Stores the copyright information for this object.</summary>
		public string Copyright;

		private readonly HostInterface currentHost;


		/// <summary>Creates a new empty object</summary>
		public StaticObject(HostInterface host)
		{
			currentHost = host;
			Mesh = new Mesh();
		}

		/// <summary>Creates a clone of this object.</summary>
		public StaticObject Clone(Textures.Texture daytimeTexture, Textures.Texture nighttimeTexture) 
		{
			StaticObject cloneResult = new StaticObject(currentHost)
			{
				StartingTrackDistance = StartingTrackDistance,
				EndingTrackDistance = EndingTrackDistance,
				Dynamic = Dynamic,
				Mesh = { Vertices = new VertexTemplate[Mesh.Vertices.Length] },
				isOptimized = isOptimized
			};

			var srcVertices = Mesh.Vertices;
			var dstVertices = cloneResult.Mesh.Vertices;
			for (int j = 0; j < srcVertices.Length; j++)
			{
				dstVertices[j] = srcVertices[j].Clone();
			}

			var srcFaces = Mesh.Faces;
			cloneResult.Mesh.Faces = new MeshFace[srcFaces.Length];
			var dstFaces = cloneResult.Mesh.Faces;
			
			for (int j = 0; j < srcFaces.Length; j++)
			{
				dstFaces[j].Flags = srcFaces[j].Flags;
				dstFaces[j].Material = srcFaces[j].Material;
				
				var srcFaceVerts = srcFaces[j].Vertices;
				dstFaces[j].Vertices = new MeshFaceVertex[srcFaceVerts.Length];
				for (int k = 0; k < srcFaceVerts.Length; k++)
				{
					dstFaces[j].Vertices[k] = srcFaceVerts[k];
				}
			}

			var srcMaterials = Mesh.Materials;
			cloneResult.Mesh.Materials = new MeshMaterial[srcMaterials.Length];
			var dstMaterials = cloneResult.Mesh.Materials;
			
			for (int j = 0; j < srcMaterials.Length; j++)
			{
				dstMaterials[j] = srcMaterials[j];
				if (daytimeTexture != null) dstMaterials[j].DaytimeTexture = daytimeTexture;
				if (nighttimeTexture != null) dstMaterials[j].NighttimeTexture = nighttimeTexture;
			}

			return cloneResult;
		}

		/// <summary>Creates a clone of this object.</summary>
		public override UnifiedObject Clone()
		{
			return Clone(null, null);
		}

		/// <summary>Creates a mirrored clone of this object</summary>
		public override UnifiedObject Mirror()
		{
			StaticObject mirrorResult = (StaticObject)Clone();
			var vertices = mirrorResult.Mesh.Vertices;
			for (int i = 0; i < vertices.Length; i++)
			{
				vertices[i].Coordinates.X = -vertices[i].Coordinates.X;
			}
			
			var faces = mirrorResult.Mesh.Faces;
			for (int i = 0; i < faces.Length; i++)
			{
				var faceVerts = faces[i].Vertices;
				for (int k = 0; k < faceVerts.Length; k++)
				{
					faceVerts[k].Normal.X = -faceVerts[k].Normal.X;
				}
				faces[i].Flip();
			}
			mirrorResult.isOptimized = isOptimized;
			return mirrorResult;
		}

		/// <inheritdoc/>
		public override UnifiedObject Transform(double nearDistance, double farDistance)
		{
			StaticObject transformResult = (StaticObject)this.Clone();
			var vertices = transformResult.Mesh.Vertices;
			
			int n = 0;
			double x2 = 0.0, x3 = 0.0, x6 = 0.0, x7 = 0.0;
			for (int i = 0; i < vertices.Length; i++)
			{
				if (n == 2) x2 = vertices[i].Coordinates.X;
				else if (n == 3) x3 = vertices[i].Coordinates.X;
				else if (n == 6) x6 = vertices[i].Coordinates.X;
				else if (n == 7) x7 = vertices[i].Coordinates.X;
				
				n++;
				if (n == 8) break;
			}
			
			if (n >= 4)
			{
				int m = 0;
				for (int i = 0; i < vertices.Length; i++)
				{
					if (m == 0) vertices[i].Coordinates.X = nearDistance - x3;
					else if (m == 1)
					{
						vertices[i].Coordinates.X = farDistance - x2;
						if (n < 8) break;
					}
					else if (m == 4) vertices[i].Coordinates.X = nearDistance - x7;
					else if (m == 5)
					{
						vertices[i].Coordinates.X = farDistance - x6;
						break;
					}
					m++;
					if (m == 8) break;
				}
			}
			return transformResult;
		}

		// Helper Zero-Allocation untuk Transform
		private int GetCornerIndex(VertexTemplate[] vertices, int offset, bool descZ, bool descX)
		{
			int bestIndex = 0;
			for (int j = 1; j < 4; j++)
			{
				var current = vertices[offset + j].Coordinates;
				var best = vertices[offset + bestIndex].Coordinates;
				
				int zComp = current.Z.CompareTo(best.Z);
				if (descZ) zComp = -zComp;
				
				if (zComp > 0) bestIndex = j;
				else if (zComp == 0)
				{
					int xComp = current.X.CompareTo(best.X);
					if (descX) xComp = -xComp;
					if (xComp > 0) bestIndex = j;
				}
			}
			return offset + bestIndex;
		}

		/// <inheritdoc/>
		public override UnifiedObject TransformLeft(double nearDistance, double farDistance)
		{
			bool vertical = true;
			var vertices = Mesh.Vertices;
			if (vertices.Length == 0) return this;

			double zPos = vertices[0].Coordinates.Z;
			double minX = double.MaxValue, maxX = double.MinValue;
			
			for (int i = 0; i < vertices.Length; i++)
			{
				var x = vertices[i].Coordinates.X;
				minX = x < minX ? x : minX;
				maxX = x > maxX ? x : maxX;
				
				if (vertical && System.Math.Abs(vertices[i].Coordinates.Z - zPos) > 0.1)
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

			var destVertices = transformResult.Mesh.Vertices;
			for (int i = 0; i < destVertices.Length; i += 4)
			{
				if (i + 3 >= destVertices.Length) break;

				int bottomLeft = GetCornerIndex(destVertices, i, true, false);
				int bottomRight = GetCornerIndex(destVertices, i, true, true);
				int topRight = GetCornerIndex(destVertices, i, false, true);
				int topLeft = GetCornerIndex(destVertices, i, false, false);

				destVertices[bottomRight].Coordinates.X = farDistance - destVertices[bottomLeft].Coordinates.X;
				destVertices[topRight].Coordinates.X = nearDistance - destVertices[topLeft].Coordinates.X;
			}

			return transformResult;
		}

		/// <inheritdoc/>
		public override UnifiedObject TransformRight(double nearDistance, double farDistance)
		{
			bool vertical = true;
			var vertices = Mesh.Vertices;
			if (vertices.Length == 0) return this;

			double zPos = vertices[0].Coordinates.Z;
			double minX = double.MaxValue, maxX = double.MinValue;
			
			for (int i = 0; i < vertices.Length; i++)
			{
				var x = vertices[i].Coordinates.X;
				minX = x < minX ? x : minX;
				maxX = x > maxX ? x : maxX;
				
				if (vertical && System.Math.Abs(vertices[i].Coordinates.Z - zPos) > 0.1)
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

			var destVertices = transformResult.Mesh.Vertices;
			for (int i = 0; i < destVertices.Length; i += 4)
			{
				if (i + 3 >= destVertices.Length) break;

				int bottomLeft = GetCornerIndex(destVertices, i, true, false);
				int bottomRight = GetCornerIndex(destVertices, i, true, true);
				int topRight = GetCornerIndex(destVertices, i, false, true);
				int topLeft = GetCornerIndex(destVertices, i, false, false);

				destVertices[bottomLeft].Coordinates.X = farDistance - destVertices[bottomRight].Coordinates.X;
				destVertices[topLeft].Coordinates.X = nearDistance - destVertices[topRight].Coordinates.X;
			}

			return transformResult;
		}

		/// <summary>Joins two static objects</summary>
		public void JoinObjects(StaticObject additionalObject, Matrix4D[] animationMatrices = null)
		{
			if (additionalObject == null) return;

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
				Mesh.Faces[mf + i].Material += (ushort)mm;
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
			float rx = (float)(1.0 / x);
			float ry = (float)(1.0 / y);
			float rz = (float)(1.0 / z);
			float rx2 = rx * rx;
			float ry2 = ry * ry;
			float rz2 = rz * rz;
			bool reverse = x * y * z < 0.0;
			
			var vertices = Mesh.Vertices;
			for (int j = 0; j < vertices.Length; j++)
			{
				vertices[j].Coordinates.X *= x;
				vertices[j].Coordinates.Y *= y;
				vertices[j].Coordinates.Z *= z;
			}

			var faces = Mesh.Faces;
			for (int j = 0; j < faces.Length; j++)
			{
				var faceVerts = faces[j].Vertices;
				for (int k = 0; k < faceVerts.Length; k++)
				{
					double nx = faceVerts[k].Normal.X;
					double ny = faceVerts[k].Normal.Y;
					double nz = faceVerts[k].Normal.Z;
					
					double u = (nx * nx * rx2) + (ny * ny * ry2) + (nz * nz * rz2);
					if (u != 0.0)
					{
						u = (float)System.Math.Sqrt(((nx * nx) + (ny * ny) + (nz * nz)) / u);
						faceVerts[k].Normal.X *= rx * u;
						faceVerts[k].Normal.Y *= ry * u;
						faceVerts[k].Normal.Z *= rz * u;
					}
				}
				if (reverse) faces[j].Flip();
			}
		}

		/// <summary>Applies rotation</summary>
		public void ApplyRotation(Vector3 rotationVector, double angle)
		{
			var vertices = Mesh.Vertices;
			for (int j = 0; j < vertices.Length; j++)
			{
				vertices[j].Coordinates.Rotate(rotationVector, angle);
			}

			var faces = Mesh.Faces;
			for (int j = 0; j < faces.Length; j++)
			{
				var faceVerts = faces[j].Vertices;
				for (int k = 0; k < faceVerts.Length; k++)
				{
					faceVerts[k].Normal.Rotate(rotationVector, angle);
				}
			}
		}
		
		/// <summary>Applies translation</summary>
		public override void ApplyTranslation(double x, double y, double z, bool absoluteTranslation = false)
		{
			var vertices = Mesh.Vertices;
			for (int i = 0; i < vertices.Length; i++)
			{
				vertices[i].Coordinates.X += x;
				vertices[i].Coordinates.Y += y;
				vertices[i].Coordinates.Z += z;
			}
		}

		/// <summary>Applies mirroring</summary>
		public void ApplyMirror(bool vX, bool vY, bool vZ, bool nX, bool nY, bool nZ)
		{
			var vertices = Mesh.Vertices;
			for (int i = 0; i < vertices.Length; i++)
			{
				if (vX) vertices[i].Coordinates.X *= -1;
				if (vY) vertices[i].Coordinates.Y *= -1;
				if (vZ) vertices[i].Coordinates.Z *= -1;
			}

			var faces = Mesh.Faces;
			for (int i = 0; i < faces.Length; i++)
			{
				var faceVerts = faces[i].Vertices;
				for (int j = 0; j < faceVerts.Length; j++)
				{
					if (nX) faceVerts[j].Normal.X *= -1;
					if (nY) faceVerts[j].Normal.Y *= -1;
					if (nZ) faceVerts[j].Normal.Z *= -1;
				}
			}

			int numFlips = (vX ? 1 : 0) + (vY ? 1 : 0) + (vZ ? 1 : 0);
			if (numFlips % 2 != 0)
			{
				for (int i = 0; i < faces.Length; i++)
				{
					Array.Reverse(faces[i].Vertices);
				}
			}
		}

		/// <summary>Applies a color to all materials in the mesh</summary>
		public void ApplyColor(Color32 newColor, bool emissive)
		{
			var materials = Mesh.Materials;
			for (int i = 0; i < materials.Length; i++)
			{
				if (emissive)
				{
					materials[i].EmissiveColor = newColor;
					materials[i].Flags |= MaterialFlags.Emissive;
				}
				else
				{
					materials[i].Color = newColor;
				}
			}
		}

		/// <summary>Performs shear mapping for all vertices within the StaticObject</summary>
		public void ApplyShear(Vector3 shearDirection, Vector3 shear, double ratio)
		{
			var vertices = Mesh.Vertices;
			for (int j = 0; j < vertices.Length; j++)
			{
				double n = ratio * (shearDirection.X * vertices[j].Coordinates.X + shearDirection.Y * vertices[j].Coordinates.Y + shearDirection.Z * vertices[j].Coordinates.Z);
				vertices[j].Coordinates += shear * n;
			}

			var faces = Mesh.Faces;
			for (int j = 0; j < faces.Length; j++)
			{
				var faceVerts = faces[j].Vertices;
				for (int k = 0; k < faceVerts.Length; k++)
				{
					if (faceVerts[k].Normal.X != 0.0f || faceVerts[k].Normal.Y != 0.0f || faceVerts[k].Normal.Z != 0.0f)
					{
						double n = ratio * (shear.X * faceVerts[k].Normal.X + shear.Y * faceVerts[k].Normal.Y + shear.Z * faceVerts[k].Normal.Z);
						faceVerts[k].Normal -= shearDirection * n;
						faceVerts[k].Normal.Normalize();
					}
				}
			}
		}

		/// <summary>Callback function to create the object within the world</summary>
		public override void CreateObject(Vector3 position, Transformation worldTransformation, Transformation localTransformation, ObjectCreationParameters Parameters)
		{
			currentHost.CreateStaticObject(this, position, Parameters, worldTransformation, localTransformation);
		}

		/// <inheritdoc />
		public override void OptimizeObject(bool preserveVerticies, int faceThreshold, bool vertexCulling)
		{
			if (isOptimized) return;
			isOptimized = true;
			
			int m = Mesh.Materials.Length;
			int f = Mesh.Faces.Length;
			
			if (m >= f / 500 && f >= faceThreshold && f < 20000 && currentHost.Platform != HostPlatform.AppleOSX)
			{
				return;
			}

			if (Mesh.Vertices.Length > 10000) preserveVerticies = true;

			// 1. Single-pass face elimination/reduction
			int writeIdx = 0;
			for (int i = 0; i < f; i++)
			{
				FaceFlags type = Mesh.Faces[i].Flags & FaceFlags.FaceTypeMask;
				bool keep = false;
				switch (type)
				{
					case FaceFlags.Triangles:
						keep = Mesh.Faces[i].Vertices.Length >= 3;
						if (keep)
						{
							int n = (Mesh.Faces[i].Vertices.Length / 3) * 3;
							if (Mesh.Faces[i].Vertices.Length != n) Array.Resize(ref Mesh.Faces[i].Vertices, n);
						}
						break;
					case FaceFlags.Quads:
						keep = Mesh.Faces[i].Vertices.Length >= 4;
						if (keep)
						{
							int n = Mesh.Faces[i].Vertices.Length & ~3;
							if (Mesh.Faces[i].Vertices.Length != n) Array.Resize(ref Mesh.Faces[i].Vertices, n);
						}
						break;
					case FaceFlags.QuadStrip:
						keep = Mesh.Faces[i].Vertices.Length >= 4;
						if (keep)
						{
							int n = Mesh.Faces[i].Vertices.Length & ~1;
							if (Mesh.Faces[i].Vertices.Length != n) Array.Resize(ref Mesh.Faces[i].Vertices, n);
						}
						break;
					default:
						keep = Mesh.Faces[i].Vertices.Length >= 3;
						break;
				}

				if (keep)
				{
					if (writeIdx != i) Mesh.Faces[writeIdx] = Mesh.Faces[i];
					writeIdx++;
				}
			}
			f = writeIdx;

			// 2. O(M) Fast Material Deduplication & Compaction
			int[] materialRemap = new int[m];
			for (int i = 0; i < m; i++)
			{
				materialRemap[i] = i;
				for (int j = 0; j < i; j++)
				{
					if (materialRemap[j] == j && Mesh.Materials[i] == Mesh.Materials[j])
					{
						materialRemap[i] = j;
						break;
					}
				}
			}

			bool[] materialUsed = new bool[m];
			for (int i = 0; i < f; i++)
			{
				Mesh.Faces[i].Material = (ushort)materialRemap[Mesh.Faces[i].Material];
				materialUsed[Mesh.Faces[i].Material] = true;
			}

			int[] finalRemap = new int[m];
			int mOut = 0;
			for (int i = 0; i < m; i++)
			{
				if (materialRemap[i] == i && materialUsed[i])
				{
					finalRemap[i] = mOut;
					if (mOut != i) Mesh.Materials[mOut] = Mesh.Materials[i];
					mOut++;
				}
			}

			for (int i = 0; i < m; i++)
			{
				if (materialRemap[i] != i) finalRemap[i] = finalRemap[materialRemap[i]];
			}

			for (int i = 0; i < f; i++)
			{
				Mesh.Faces[i].Material = (ushort)finalRemap[Mesh.Faces[i].Material];
			}
			m = mOut;

			// 3. Cull identical and unreferenced vertices (Dictionary based)
			if (!preserveVerticies && vertexCulling)
			{
				var uniqueVertices = new Dictionary<VertexTemplate, int>();
				var newVertices = new VertexTemplate[Mesh.Vertices.Length];
				int count = 0;

				for (int i = 0; i < f; i++)
				{
					var faceVerts = Mesh.Faces[i].Vertices;
					for (int j = 0; j < faceVerts.Length; j++)
					{
						int oldIndex = faceVerts[j].Index;
						VertexTemplate vertex = Mesh.Vertices[oldIndex];

						if (!uniqueVertices.TryGetValue(vertex, out int newIndex))
						{
							newIndex = count;
							uniqueVertices.Add(vertex, newIndex);
							newVertices[count] = vertex;
							count++;
						}
						faceVerts[j].Index = newIndex;
					}
				}

				Mesh.Vertices = new VertexTemplate[count];
				Array.Copy(newVertices, 0, Mesh.Vertices, 0, count);
			}

			// 4. Triangularize all polygons and quads into triangles
			for (int i = 0; i < f; ++i)
			{
				FaceFlags type = Mesh.Faces[i].Flags & FaceFlags.FaceTypeMask;
				if (type == FaceFlags.Quads || type == FaceFlags.Polygon)
				{
					int startingVertexCount = Mesh.Faces[i].Vertices.Length;
					int triCount = (startingVertexCount - 2);
					int vertexCount = triCount * 3;

					var originalPoly = (MeshFaceVertex[])Mesh.Faces[i].Vertices.Clone();
					Array.Resize(ref Mesh.Faces[i].Vertices, vertexCount);
					var outVerts = Mesh.Faces[i].Vertices;

					for (int triIndex = 0, vertIndex = 0, oldVert = 2; triIndex < triCount; ++triIndex, ++oldVert)
					{
						outVerts[vertIndex++] = originalPoly[0];
						outVerts[vertIndex++] = originalPoly[oldVert - 1];
						outVerts[vertIndex++] = originalPoly[oldVert];
					}

					Mesh.Faces[i].Flags &= ~FaceFlags.FaceTypeMask;
					Mesh.Faces[i].Flags |= FaceFlags.Triangles;
				}
			}

			// 5. Decomposite TRIANGLES and QUADS
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

				if ((faceCount == 3 || faceCount == 4) && Mesh.Faces[i].Vertices.Length > faceCount)
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

			// 6. Squish faces with the same material (O(F) Dictionary-based)
			{
				var mergeGroups = new Dictionary<long, List<int>>();

				for (int i = 0; i < f; ++i)
				{
					FaceFlags type = Mesh.Faces[i].Flags & FaceFlags.FaceTypeMask;
					if (type != FaceFlags.Triangles) continue;

					FaceFlags face = Mesh.Faces[i].Flags & FaceFlags.Face2Mask;
					long key = ((long)Mesh.Faces[i].Material << 32) | ((long)(int)type << 16) | (long)(int)face;

					if (!mergeGroups.TryGetValue(key, out List<int> group))
					{
						group = new List<int>();
						mergeGroups[key] = group;
					}
					group.Add(i);
				}

				foreach (var kvp in mergeGroups)
				{
					List<int> group = kvp.Value;
					if (group.Count <= 1) continue;

					int targetIdx = group[0];
					int totalVerts = 0;
					for (int g = 0; g < group.Count; g++)
					{
						totalVerts += Mesh.Faces[group[g]].Vertices.Length;
					}

					var merged = new MeshFaceVertex[totalVerts];
					int offset = 0;
					for (int g = 0; g < group.Count; g++)
					{
						var src = Mesh.Faces[group[g]].Vertices;
						Array.Copy(src, 0, merged, offset, src.Length);
						offset += src.Length;
					}

					Mesh.Faces[targetIdx].Vertices = merged;

					for (int g = 1; g < group.Count; g++)
					{
						Mesh.Faces[group[g]].Vertices = null; // Mark for removal
					}
				}

				writeIdx = 0;
				for (int i = 0; i < f; i++)
				{
					if (Mesh.Faces[i].Vertices != null)
					{
						if (writeIdx != i) Mesh.Faces[writeIdx] = Mesh.Faces[i];
						writeIdx++;
					}
				}
				f = writeIdx;
			}

			// 7. Finalize arrays
			if (m != Mesh.Materials.Length) Array.Resize(ref Mesh.Materials, m);
			if (f != Mesh.Faces.Length) Array.Resize(ref Mesh.Faces, f);
		}

	}
}