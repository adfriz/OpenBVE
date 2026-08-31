using System;
using System.Collections.Generic;
using OpenBveApi.Colors;
using OpenBveApi.Hosts;
using OpenBveApi.Math;
using OpenBveApi.World;

namespace OpenBveApi.Objects
{
	// <summary>Represents a static (e.g. non-animated) object within the world</summary>
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
		/// <param name="daytimeTexture">The replacement daytime texture</param>
		/// <param name="nighttimeTexture">The replacement nighttime texture</param>
		/// <returns></returns>
		public StaticObject Clone(Textures.Texture daytimeTexture, Textures.Texture nighttimeTexture) //Prefix is required or else MCS barfs
		{
			StaticObject cloneResult = new StaticObject(currentHost)
			{
				StartingTrackDistance = StartingTrackDistance,
				EndingTrackDistance = EndingTrackDistance,
				Dynamic = Dynamic,
				Mesh = {Vertices = new VertexTemplate[Mesh.Vertices.Length]},
				isOptimized = isOptimized
			};
			// vertices
			for (int j = 0; j < Mesh.Vertices.Length; j++)
			{
				cloneResult.Mesh.Vertices[j] = Mesh.Vertices[j].Clone();
			}

			// faces
			cloneResult.Mesh.Faces = new MeshFace[Mesh.Faces.Length];
			for (int j = 0; j < Mesh.Faces.Length; j++)
			{
				cloneResult.Mesh.Faces[j].Flags = Mesh.Faces[j].Flags;
				cloneResult.Mesh.Faces[j].Material = Mesh.Faces[j].Material;
				cloneResult.Mesh.Faces[j].Vertices = new MeshFaceVertex[Mesh.Faces[j].Vertices.Length];
				for (int k = 0; k < Mesh.Faces[j].Vertices.Length; k++)
				{
					cloneResult.Mesh.Faces[j].Vertices[k] = Mesh.Faces[j].Vertices[k];
				}
			}

			// materials
			cloneResult.Mesh.Materials = new MeshMaterial[Mesh.Materials.Length];
			for (int j = 0; j < Mesh.Materials.Length; j++)
			{
				cloneResult.Mesh.Materials[j] = Mesh.Materials[j];
				cloneResult.Mesh.Materials[j].DaytimeTexture = daytimeTexture ?? Mesh.Materials[j].DaytimeTexture;
				cloneResult.Mesh.Materials[j].NighttimeTexture = nighttimeTexture ?? Mesh.Materials[j].NighttimeTexture;
			}

			return cloneResult;
		}

		/// <summary>Creates a clone of this object.</summary>
		public override UnifiedObject Clone()
		{
			StaticObject cloneResult = new StaticObject(currentHost)
			{
				StartingTrackDistance = StartingTrackDistance,
				EndingTrackDistance = EndingTrackDistance,
				Dynamic = Dynamic,
				Mesh = {Vertices = new VertexTemplate[Mesh.Vertices.Length]},
				isOptimized = isOptimized
			};
			// vertices
			for (int j = 0; j < Mesh.Vertices.Length; j++)
			{
				cloneResult.Mesh.Vertices[j] = Mesh.Vertices[j].Clone();
			}

			// faces
			cloneResult.Mesh.Faces = new MeshFace[Mesh.Faces.Length];
			for (int j = 0; j < Mesh.Faces.Length; j++)
			{
				cloneResult.Mesh.Faces[j].Flags = Mesh.Faces[j].Flags;
				cloneResult.Mesh.Faces[j].Material = Mesh.Faces[j].Material;
				cloneResult.Mesh.Faces[j].Vertices = new MeshFaceVertex[Mesh.Faces[j].Vertices.Length];
				for (int k = 0; k < Mesh.Faces[j].Vertices.Length; k++)
				{
					cloneResult.Mesh.Faces[j].Vertices[k] = Mesh.Faces[j].Vertices[k];
				}
			}

			// materials
			cloneResult.Mesh.Materials = new MeshMaterial[Mesh.Materials.Length];
			for (int j = 0; j < Mesh.Materials.Length; j++)
			{
				cloneResult.Mesh.Materials[j] = Mesh.Materials[j];
			}

			return cloneResult;
		}

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
			/* ** ORIGINAL ALGORITHM**
			 *
			 * A brief description on how this works:
			 *
			 * Objects are implicitly assumed to be left or right handed.
			 * They must follow the following vertex windings, having a total of 4 or 8 vertices:
			 *
			 * LEFT-HANDED
			 * ============
			 *
			 * TopLeft, BottomLeft, BottomRight, TopRight
			 *
			 * RIGHT-HANDED
			 * ============
			 *
			 * BottomRight, TopRight, TopLeft, BottomLeft
			 *
			 * We then go through the vertex list, and our first two vertices in each are transformed.
			 * The *new* position is now the corresponding X of the other vertex MINUS the distance.
			 *
			 * NOTES:
			 * This algorithm is totally broken for anything other than objects containing 4 / 8 vertices
			 * If our vertex windings do not conform, it's also broken.
			 *
			 */
			/*
			 * BUGFIX: The original algorithm only transformed the first 4 or 8 vertices in the mesh, leaving
			 * all subsequent vertices at their original coordinates. This distorted or hid objects with larger
			 * vertex counts (such as the .x platform and roof center objects 'FormCL/CR' and 'RoofCL/CR').
			 * We now delegate to TransformLeft or TransformRight based on whether the secondary rail is on the
			 * left (nearDistance < 0.0) or right side of the primary rail.
			 */
			if (nearDistance < 0.0)
			{
				return TransformRight(nearDistance, farDistance);
			}
			else
			{
				return TransformLeft(nearDistance, farDistance);
			}
		}

		/// <inheritdoc/>
		public override UnifiedObject TransformLeft(double nearDistance, double farDistance)
		{
			// If we have less than 4 vertices, we cannot perform the interpolation based on the standard reference quad
			if (Mesh.Vertices.Length < 4)
			{
				return (StaticObject)Clone();
			}
			// Find reference vertices by manually checking the first 4 vertices
			// bottomLeft: highest Z, lowest X (Z Descending, X Ascending)
			// bottomRight: highest Z, highest X (Z Descending, X Descending)
			// topRight: lowest Z, highest X (Z Ascending, X Descending)
			// topLeft: lowest Z, lowest X (Z Ascending, X Ascending)
			int bottomLeftIdx = 0, bottomRightIdx = 0, topRightIdx = 0, topLeftIdx = 0;
			for (int i = 1; i < 4; i++)
			{
				Vector3 c = Mesh.Vertices[i].Coordinates;
				
				Vector3 cbl = Mesh.Vertices[bottomLeftIdx].Coordinates;
				if (c.Z > cbl.Z || (c.Z == cbl.Z && c.X < cbl.X)) bottomLeftIdx = i;

				Vector3 cbr = Mesh.Vertices[bottomRightIdx].Coordinates;
				if (c.Z > cbr.Z || (c.Z == cbr.Z && c.X > cbr.X)) bottomRightIdx = i;

				Vector3 ctr = Mesh.Vertices[topRightIdx].Coordinates;
				if (c.Z < ctr.Z || (c.Z == ctr.Z && c.X > ctr.X)) topRightIdx = i;

				Vector3 ctl = Mesh.Vertices[topLeftIdx].Coordinates;
				if (c.Z < ctl.Z || (c.Z == ctl.Z && c.X < ctl.X)) topLeftIdx = i;
			}

			if (bottomLeftIdx == bottomRightIdx || bottomLeftIdx == topRightIdx || bottomLeftIdx == topLeftIdx ||
			    bottomRightIdx == topRightIdx || bottomRightIdx == topLeftIdx || topRightIdx == topLeftIdx)
			{
				return (StaticObject)Clone();
			}

			StaticObject transformResult = (StaticObject)Clone();
			// Extract Z range of the first face's side vertices to map along the block length (usually 25m)
			// Vertex 3 is face at Z = 0, Vertex 2 is face at Z = blockLength
			double zMin = Mesh.Vertices[topLeftIdx].Coordinates.Z;
			double zMax = Mesh.Vertices[bottomLeftIdx].Coordinates.Z;
			double zRange = zMax - zMin;

			double xLeft0 = Mesh.Vertices[topLeftIdx].Coordinates.X;
			double xLeft1 = Mesh.Vertices[bottomLeftIdx].Coordinates.X;
			double xRight0 = Mesh.Vertices[topRightIdx].Coordinates.X;
			double xRight1 = Mesh.Vertices[bottomRightIdx].Coordinates.X;

			for (int i = 0; i < Mesh.Vertices.Length; i++)
			{
				double z = Mesh.Vertices[i].Coordinates.Z;
				// Compute interpolation factor 't' along the Z length of the object
				double t = zRange > 0.001 ? (z - zMin) / zRange : 0.0;
				// Interpolate reference coordinates at this vertex's Z position
				double faceX = xLeft0 + t * (xLeft1 - xLeft0);
				double originalBackX = xRight0 + t * (xRight1 - xRight0);
				// The target back position is nearDistance (adjusted for the face offset xLeft0) at Z = 0,
				// and farDistance (adjusted for the face offset xLeft1) at Z = blockLength
				double backX = (nearDistance - xLeft0) + t * ((farDistance - xLeft1) - (nearDistance - xLeft0));
				// Map the vertex's X coordinate from [faceX, originalBackX] to [faceX, backX]
				double x = Mesh.Vertices[i].Coordinates.X;
				double range = originalBackX - faceX;
				if (System.Math.Abs(range) > 0.001)
				{
					transformResult.Mesh.Vertices[i].Coordinates.X = faceX + ((x - faceX) / range) * (backX - faceX);
				}
			}
			return transformResult;
		}

		/// <inheritdoc/>
		public override UnifiedObject TransformRight(double nearDistance, double farDistance)
		{
			// If we have less than 4 vertices, we cannot perform the interpolation based on the standard reference quad
			if (Mesh.Vertices.Length < 4)
			{
				return (StaticObject)Clone();
			}
			// Find reference vertices by manually checking the first 4 vertices
			// bottomLeft: highest Z, lowest X (Z Descending, X Ascending)
			// bottomRight: highest Z, highest X (Z Descending, X Descending)
			// topRight: lowest Z, highest X (Z Ascending, X Descending)
			// topLeft: lowest Z, lowest X (Z Ascending, X Ascending)
			int bottomLeftIdx = 0, bottomRightIdx = 0, topRightIdx = 0, topLeftIdx = 0;
			for (int i = 1; i < 4; i++)
			{
				Vector3 c = Mesh.Vertices[i].Coordinates;
				
				Vector3 cbl = Mesh.Vertices[bottomLeftIdx].Coordinates;
				if (c.Z > cbl.Z || (c.Z == cbl.Z && c.X < cbl.X)) bottomLeftIdx = i;

				Vector3 cbr = Mesh.Vertices[bottomRightIdx].Coordinates;
				if (c.Z > cbr.Z || (c.Z == cbr.Z && c.X > cbr.X)) bottomRightIdx = i;

				Vector3 ctr = Mesh.Vertices[topRightIdx].Coordinates;
				if (c.Z < ctr.Z || (c.Z == ctr.Z && c.X > ctr.X)) topRightIdx = i;

				Vector3 ctl = Mesh.Vertices[topLeftIdx].Coordinates;
				if (c.Z < ctl.Z || (c.Z == ctl.Z && c.X < ctl.X)) topLeftIdx = i;
			}

			if (bottomLeftIdx == bottomRightIdx || bottomLeftIdx == topRightIdx || bottomLeftIdx == topLeftIdx ||
			    bottomRightIdx == topRightIdx || bottomRightIdx == topLeftIdx || topRightIdx == topLeftIdx)
			{
				return (StaticObject)Clone();
			}

			StaticObject transformResult = (StaticObject)Clone();
			// Extract Z range of the first face's side vertices to map along the block length (usually 25m)
			// Vertex 3 is face at Z = 0, Vertex 2 is face at Z = blockLength
			double zMin = Mesh.Vertices[topRightIdx].Coordinates.Z;
			double zMax = Mesh.Vertices[bottomRightIdx].Coordinates.Z;
			double zRange = zMax - zMin;

			double xLeft0 = Mesh.Vertices[topLeftIdx].Coordinates.X;
			double xLeft1 = Mesh.Vertices[bottomLeftIdx].Coordinates.X;
			double xRight0 = Mesh.Vertices[topRightIdx].Coordinates.X;
			double xRight1 = Mesh.Vertices[bottomRightIdx].Coordinates.X;

			for (int i = 0; i < Mesh.Vertices.Length; i++)
			{
				double z = Mesh.Vertices[i].Coordinates.Z;
				// Compute interpolation factor 't' along the Z length of the object
				double t = zRange > 0.001 ? (z - zMin) / zRange : 0.0;
				// Interpolate reference coordinates at this vertex's Z position
				double faceX = xRight0 + t * (xRight1 - xRight0);
				double originalBackX = xLeft0 + t * (xLeft1 - xLeft0);
				// The target back position is nearDistance (adjusted for the face offset xRight0) at Z = 0,
				// and farDistance (adjusted for the face offset xRight1) at Z = blockLength
				double backX = (nearDistance - xRight0) + t * ((farDistance - xRight1) - (nearDistance - xRight0));
				// Map the vertex's X coordinate from [faceX, originalBackX] to [faceX, backX]
				double x = Mesh.Vertices[i].Coordinates.X;
				double range = originalBackX - faceX;
				if (System.Math.Abs(range) > 0.001)
				{
					transformResult.Mesh.Vertices[i].Coordinates.X = faceX + ((x - faceX) / range) * (backX - faceX);
				}
			}
			return transformResult;
		}

		/// <summary>Joins two static objects</summary>
		/// <param name="additionalObject">The static object to join</param>
		/// <param name="animationMatrices">The animation matrices for the object</param>
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
							transformedCoordinates.Transform(animationMatrices[av.MatrixChain[j]], false); // use the static matrix, not the animated one
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
		/// <param name="rotationVector">The rotation vector</param>
		/// <param name="angle">The angle to rotate in degrees</param>
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
		public override void ApplyTranslation(Vector3 translationVector, bool absoluteTranslation = false)
		{
			for (int i = 0; i < Mesh.Vertices.Length; i++)
			{
				Mesh.Vertices[i].Coordinates += translationVector;
			}
		}

		/// <summary>Applies mirroring</summary>
		/// <param name="vX">Whether to mirror vertices in the X-axis</param>
		/// <param name="vY">Whether to mirror vertices in the Y-axis</param>
		/// <param name="vZ">Whether to mirror vertices in the Z-axis</param>
		/// <param name="nX">Whether to mirror normals in the X-axis</param>
		/// <param name="nY">Whether to mirror normals in the Y-axis</param>
		/// <param name="nZ">Whether to mirror normals in the Z-axis</param>
		public void ApplyMirror(bool vX, bool vY, bool vZ, bool nX, bool nY, bool nZ)
		{
			for (int i = 0; i < Mesh.Vertices.Length; i++)
			{
				if (vX)
				{
					Mesh.Vertices[i].Coordinates.X *= -1;
				}

				if (vY)
				{
					Mesh.Vertices[i].Coordinates.Y *= -1;
				}

				if (vZ)
				{
					Mesh.Vertices[i].Coordinates.Z *= -1;
				}
			}

			for (int i = 0; i < Mesh.Faces.Length; i++)
			{
				for (int j = 0; j < Mesh.Faces[i].Vertices.Length; j++)
				{
					if (nX)
					{
						Mesh.Faces[i].Vertices[j].Normal.X *= -1;
					}

					if (nY)
					{
						Mesh.Faces[i].Vertices[j].Normal.Y *= -1;
					}

					if (nZ)
					{
						Mesh.Faces[i].Vertices[j].Normal.Z *= -1;
					}
				}
			}

			int numFlips = 0;
			if (vX)
			{
				numFlips++;
			}

			if (vY)
			{
				numFlips++;
			}

			if (vZ)
			{
				numFlips++;
			}

			if (numFlips % 2 != 0)
			{
				for (int i = 0; i < Mesh.Faces.Length; i++)
				{
					Array.Reverse(Mesh.Faces[i].Vertices);
				}
			}
		}

		/// <summary>Applies a color to all materials in the mesh</summary>
		/// <param name="newColor">The color</param>
		/// <param name="emissive">Whether this is an emissive color</param>
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

		/// <summary>Performs shear mapping for all vertices within the StaticObject</summary>
		/// <param name="shearDirection">A vector describing the direction of the plane to be sheared</param>
		/// <param name="shear">A vector describing the shear direction</param>
		/// <param name="ratio">The amount of shear to apply.</param>
		/// <remarks>If Ratio is 0, no transformation is performed. If Direction and Shear are perpendicular, a Ratio of 1 corresponds to a slope of 45 degrees</remarks>
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

		/// <summary>Callback function to create the object within the world</summary>
		public override void CreateObject(Vector3 position, Transformation worldTransformation, Transformation localTransformation,
			ObjectCreationParameters Parameters)
		{
			currentHost.CreateStaticObject(this, position, Parameters, worldTransformation, localTransformation);
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
			
			if (currentHost.Platform != HostPlatform.AppleOSX)
			{
				/*
				 * HACK:
				 * A forwards compatible GL3 context (required on OS-X) only supports tris
				 * and thus an optimized object (decomposed into tris) in all circumstances
				 */
				// Force optimisation for both game and viewers (threshold bypass)
				// Previously viewers could skip when m >= f/500 && f >= faceThreshold && f <20000
			}

			if (Mesh.Vertices.Length > 10000)
			{
				// Don't attempt to de-duplicate where over 10k vertices
				preserveVerticies = true;
			}

			// eliminate invalid faces and reduce incomplete faces (2-pointer compaction)
			{
				MeshFace[] faces = Mesh.Faces;
				int write = 0;
				for (int i = 0; i < f; i++)
				{
					FaceFlags type = faces[i].Flags & FaceFlags.FaceTypeMask;
					bool keep;
					switch (type)
					{
						case FaceFlags.Triangles:
							keep = faces[i].Vertices.Length >= 3;
							if (keep)
							{
								int n = (faces[i].Vertices.Length / 3) * 3;
								if (faces[i].Vertices.Length != n)
								{
									Array.Resize(ref faces[i].Vertices, n);
								}
							}
							break;
						case FaceFlags.Quads:
							keep = faces[i].Vertices.Length >= 4;
							if (keep)
							{
								int n = faces[i].Vertices.Length & ~3;
								if (faces[i].Vertices.Length != n)
								{
									Array.Resize(ref faces[i].Vertices, n);
								}
							}
							break;
						case FaceFlags.QuadStrip:
							keep = faces[i].Vertices.Length >= 4;
							if (keep)
							{
								int n = faces[i].Vertices.Length & ~1;
								if (faces[i].Vertices.Length != n)
								{
									Array.Resize(ref faces[i].Vertices, n);
								}
							}
							break;
						default:
							keep = faces[i].Vertices.Length >= 3;
							break;
					}
					if (!keep) continue;
					if (write != i) faces[write] = faces[i];
					write++;
				}
				f = write;
			}

			// eliminate unused materials (compaction + single-pass remap)
			{
				MeshFace[] faces = Mesh.Faces;
				MeshMaterial[] materials = Mesh.Materials;
				bool[] materialUsed = new bool[m];
				for (int i = 0; i < f; i++)
				{
					materialUsed[faces[i].Material] = true;
				}
				int[] remap = new int[m];
				int newM = 0;
				for (int i = 0; i < m; i++)
				{
					if (materialUsed[i])
					{
						remap[i] = newM++;
					}
					else
					{
						remap[i] = -1;
					}
				}
				if (newM != m)
				{
					int w = 0;
					for (int i = 0; i < m; i++)
					{
						if (materialUsed[i])
						{
							if (w != i) materials[w] = materials[i];
							w++;
						}
					}
					for (int i = 0; i < f; i++)
					{
						faces[i].Material = (ushort)remap[faces[i].Material];
					}
					m = newM;
				}
			}

			// eliminate duplicate materials
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
								Mesh.Faces[k].Material = (ushort)i;
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

			// Cull identical and unreferenced vertices based on the hidden vertexCulling option.
			// Replaced old very slow OrderedDictionary implementation with generic Dictionary.
			if (!preserveVerticies && vertexCulling)
			{
				MeshFace[] faces = Mesh.Faces;
				VertexTemplate[] vertices = Mesh.Vertices;
				int vLen = vertices.Length;
				int capacity = vLen;
				int est = f * 3;
				if (est > capacity) capacity = est;
				Dictionary<VertexTemplate, int> uniqueVertices = new Dictionary<VertexTemplate, int>(capacity);
				VertexTemplate[] newVertices = new VertexTemplate[vLen];
				int count = 0;

				// Iterate through all referenced vertices in the faces.
				// This automatically ignores and culls unreferenced 'garbage' vertices in the original Mesh.Vertices array.
				for (int i = 0; i < f; i++)
				{
					MeshFaceVertex[] fv = faces[i].Vertices;
					for (int j = 0; j < fv.Length; j++)
					{
						int oldIndex = fv[j].Index;
						VertexTemplate vertex = vertices[oldIndex];

						// If the exact same vertex structure hasn't been cached yet, cache it and add it to our new array.
						if (!uniqueVertices.TryGetValue(vertex, out int newIndex))
						{
							newIndex = count;
							uniqueVertices.Add(vertex, newIndex);
							newVertices[count] = vertex;
							count++;
						}

						// Update the face to point to the new, deduplicated vertex index.
						fv[j].Index = newIndex;
					}
				}

				// Copy the unique vertices back into the mesh (only count, not full length)
				if (count == vLen)
				{
					Mesh.Vertices = newVertices;
				}
				else
				{
					Mesh.Vertices = new VertexTemplate[count];
					Array.Copy(newVertices, 0, Mesh.Vertices, 0, count);
				}
			}

			// structure optimization
			// Triangularize all polygons and quads into triangles (alloc once, no Clone/Resize)
			{
				MeshFace[] faces = Mesh.Faces;
				for (int i = 0; i < f; ++i)
				{
					FaceFlags type = faces[i].Flags & FaceFlags.FaceTypeMask;
					if (type == FaceFlags.Quads || type == FaceFlags.Polygon)
					{
						MeshFaceVertex[] src = faces[i].Vertices;
						int startingVertexCount = src.Length;
						int triCount = startingVertexCount - 2;
						int vertexCount = triCount * 3;
						MeshFaceVertex[] dst = new MeshFaceVertex[vertexCount];
						for (int triIndex = 0, vertIndex = 0, oldVert = 2; triIndex < triCount; ++triIndex, ++oldVert)
						{
							dst[vertIndex++] = src[0];
							dst[vertIndex++] = src[oldVert - 1];
							dst[vertIndex++] = src[oldVert];
						}
						faces[i].Vertices = dst;
						faces[i].Flags &= ~FaceFlags.FaceTypeMask;
						faces[i].Flags |= FaceFlags.Triangles;
					}
				}
			}

			// decomposite TRIANGLES and QUADS (precompute capacity, single resize)
			{
				MeshFace[] faces = Mesh.Faces;
				int extra = 0;
				for (int i = 0; i < f; i++)
				{
					FaceFlags type = faces[i].Flags & FaceFlags.FaceTypeMask;
					int faceCount = 0;
					if (type == FaceFlags.Triangles) faceCount = 3;
					else if (type == FaceFlags.Quads) faceCount = 4;
					if ((faceCount == 3 || faceCount == 4) && faces[i].Vertices.Length > faceCount)
					{
						extra += (faces[i].Vertices.Length - faceCount) / faceCount;
					}
				}
				if (extra > 0)
				{
					if (f + extra > faces.Length)
					{
						Array.Resize(ref Mesh.Faces, f + extra);
						faces = Mesh.Faces;
					}
				}
				for (int i = 0; i < f; i++)
				{
					FaceFlags type = faces[i].Flags & FaceFlags.FaceTypeMask;
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
					if ((faceCount == 3 || faceCount == 4) && faces[i].Vertices.Length > faceCount)
					{
						int n = (faces[i].Vertices.Length - faceCount) / faceCount;
						MeshFaceVertex[] srcVerts = faces[i].Vertices;
						FaceFlags origFlags = faces[i].Flags;
						for (int j = 0; j < n; j++)
						{
							faces[f + j].Vertices = new MeshFaceVertex[faceCount];
							Array.Copy(srcVerts, faceCount + faceCount * j, faces[f + j].Vertices, 0, faceCount);
							faces[f + j].Material = faces[i].Material;
							faces[f + j].Flags = (j == 0) ? origFlags : (FaceFlags)((origFlags & ~FaceFlags.FaceTypeMask) | faceBit);
						}
						faces[i].Flags = (FaceFlags)((origFlags & ~FaceFlags.FaceTypeMask) | faceBit);
						Array.Resize(ref faces[i].Vertices, faceCount);
						f += n;
					}
				}
			}

			// Squish faces that have the same material (Dictionary grouping O(f))
			{
				MeshFace[] faces = Mesh.Faces;
				if (f > 1)
				{
					Dictionary<(ushort mat, FaceFlags flags), List<int>> groups = new Dictionary<(ushort, FaceFlags), List<int>>(f);
					for (int i = 0; i < f; i++)
					{
						FaceFlags type = faces[i].Flags & FaceFlags.FaceTypeMask;
						if (type != FaceFlags.Triangles) continue;
						var key = (faces[i].Material, faces[i].Flags);
						if (!groups.TryGetValue(key, out var list))
						{
							list = new List<int>();
							groups[key] = list;
						}
						list.Add(i);
					}
					bool[] toRemove = new bool[f];
					foreach (var kvp in groups)
					{
						List<int> list = kvp.Value;
						if (list.Count <= 1) continue;
						int firstIdx = list[0];
						int totalVerts = 0;
						for (int k = 0; k < list.Count; k++) totalVerts += faces[list[k]].Vertices.Length;
						MeshFaceVertex[] merged = new MeshFaceVertex[totalVerts];
						int pos = 0;
						for (int k = 0; k < list.Count; k++)
						{
							MeshFaceVertex[] src = faces[list[k]].Vertices;
							Array.Copy(src, 0, merged, pos, src.Length);
							pos += src.Length;
						}
						faces[firstIdx].Vertices = merged;
						for (int k = 1; k < list.Count; k++) toRemove[list[k]] = true;
					}
					int write = 0;
					for (int i = 0; i < f; i++)
					{
						if (toRemove[i]) continue;
						if (write != i) faces[write] = faces[i];
						write++;
					}
					f = write;
				}
			}
			// finalize arrays

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


