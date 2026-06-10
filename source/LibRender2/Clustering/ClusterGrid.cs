using System;
using OpenBveApi.Math;
using OTKVector4 = OpenTK.Vector4;

namespace LibRender2.Clustering
{
	/// <summary>
	/// Defines the cluster grid dimensions and computes per-cluster view-space AABBs.
	/// Grid: NumX × NumY × NumZ = 16 × 8 × 24 = 3072 clusters total.
	/// Z-slices use logarithmic distribution for even light coverage across depth.
	/// </summary>
	public class ClusterGrid
	{
		// --- Grid dimensions (matching plan: 16×8×24) ---
		public readonly int NumX = 16;
		public readonly int NumY = 8;
		public readonly int NumZ = 24;

		/// <summary>Total cluster count: NumX * NumY * NumZ</summary>
		public int TotalClusters => NumX * NumY * NumZ;

		// --- Camera params (set each RebuildClusters call) ---
		public float NearPlane { get; private set; }
		public float FarPlane  { get; private set; }

		/// <summary>Precomputed view-space AABBs for all clusters, ready for GPU upload</summary>
		public GpuClusterAABB[] ClusterAABBs { get; private set; }

		public ClusterGrid()
		{
			ClusterAABBs = new GpuClusterAABB[NumX * NumY * NumZ];
		}

		/// <summary>
		/// Recomputes all cluster AABBs in view space from the current projection matrix.
		/// Called whenever the projection matrix changes (FOV/resolution/near/far change).
		/// </summary>
		/// <param name="nearPlane">Camera near plane distance (positive)</param>
		/// <param name="farPlane">Camera far plane distance (positive)</param>
		/// <param name="fovYRad">Vertical field of view in radians</param>
		/// <param name="aspectRatio">Viewport aspect ratio (width/height)</param>
		public void RebuildAABBs(float nearPlane, float farPlane, float fovYRad, float aspectRatio)
		{
			NearPlane = nearPlane;
			FarPlane  = farPlane;

			float tanHalfFovY = (float)Math.Tan(fovYRad * 0.5);
			float tanHalfFovX = tanHalfFovY * aspectRatio;

			// Precompute log-distributed Z-slice boundaries in view space
			float[] zSlices = ComputeLogZSlices(nearPlane, farPlane, NumZ);

			int idx = 0;
			for (int z = 0; z < NumZ; z++)
			{
				float zNear = zSlices[z];
				float zFar  = zSlices[z + 1];

				for (int y = 0; y < NumY; y++)
				{
					// Tile boundaries in NDC [-1,1] mapped to view-space at zNear/zFar
					float yMinNDC = -1.0f + (2.0f * y)       / NumY;
					float yMaxNDC = -1.0f + (2.0f * (y + 1)) / NumY;

					for (int x = 0; x < NumX; x++)
					{
						float xMinNDC = -1.0f + (2.0f * x)       / NumX;
						float xMaxNDC = -1.0f + (2.0f * (x + 1)) / NumX;

						// Project tile edges to view space at near and far slice planes
						// View-space: x goes right, y goes up, z goes INTO screen (negative)
						GpuClusterAABB aabb = ComputeTileAABB(
							xMinNDC, xMaxNDC, yMinNDC, yMaxNDC,
							zNear, zFar,
							tanHalfFovX, tanHalfFovY);

						ClusterAABBs[idx++] = aabb;
					}
				}
			}
		}

		/// <summary>
		/// Gets the linear cluster index from XYZ tile coordinates.
		/// Layout: idx = x + y * NumX + z * NumX * NumY
		/// </summary>
		public int GetClusterIndex(int x, int y, int z)
		{
			return x + y * NumX + z * NumX * NumY;
		}

		// -------------------------------------------------------------------------
		// Private helpers
		// -------------------------------------------------------------------------

		/// <summary>
		/// Computes NumZ+1 logarithmically-distributed Z slice boundaries between near and far.
		/// Matches the depth slice formula used in the fragment shader.
		/// </summary>
		private static float[] ComputeLogZSlices(float zNear, float zFar, int numSlices)
		{
			float[] slices = new float[numSlices + 1];
			slices[0] = zNear;
			slices[numSlices] = zFar;
			float logRatio = (float)Math.Log(zFar / zNear);
			for (int i = 1; i < numSlices; i++)
			{
				float t = (float)i / numSlices;
				slices[i] = zNear * (float)Math.Exp(t * logRatio);
			}
			return slices;
		}

		/// <summary>
		/// Computes the view-space AABB for a single cluster tile.
		/// NDC tile coordinates are unprojected to view space at both zNear and zFar depth planes.
		/// </summary>
		private static GpuClusterAABB ComputeTileAABB(
			float xMinNDC, float xMaxNDC,
			float yMinNDC, float yMaxNDC,
			float zNear,   float zFar,
			float tanHalfFovX, float tanHalfFovY)
		{
			// View-space corners at near plane (z is negative in OpenGL view space)
			float xNearMin = xMinNDC * tanHalfFovX * zNear;
			float xNearMax = xMaxNDC * tanHalfFovX * zNear;
			float yNearMin = yMinNDC * tanHalfFovY * zNear;
			float yNearMax = yMaxNDC * tanHalfFovY * zNear;

			// View-space corners at far plane
			float xFarMin = xMinNDC * tanHalfFovX * zFar;
			float xFarMax = xMaxNDC * tanHalfFovX * zFar;
			float yFarMin = yMinNDC * tanHalfFovY * zFar;
			float yFarMax = yMaxNDC * tanHalfFovY * zFar;

			// AABB = min/max over all 8 corners
			float minX = Math.Min(xNearMin, xFarMin);
			float maxX = Math.Max(xNearMax, xFarMax);
			float minY = Math.Min(yNearMin, yFarMin);
			float maxY = Math.Max(yNearMax, yFarMax);
			// z is negative in view space (camera looks toward -Z)
			float minZ = -zFar;
			float maxZ = -zNear;

			return new GpuClusterAABB
			{
				Min = new OTKVector4(minX, minY, minZ, 0.0f),
				Max = new OTKVector4(maxX, maxY, maxZ, 0.0f)
			};
		}
	}
}
