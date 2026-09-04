using System;

namespace LibRender2.Clustering
{
	/// <summary>
	/// 2D tiled grid: NumX x NumY = 16 x 8 = 128 tiles total.
	/// No depth (Z) slices — pure screen-space tiling.
	/// </summary>
	public class ClusterGrid
	{
		// --- Grid dimensions (2D tiled) ---
		public readonly int NumX = 16;
		public readonly int NumY = 8;

		/// <summary>
		/// Kept as compatibility stub so existing ClusterEngine code
		/// referencing grid.NumZ still compiles. Effective 2D grid has no Z slices.
		/// </summary>
		public readonly int NumZ = 1;

		/// <summary>Total tile count: NumX * NumY = 128</summary>
		public int TotalClusters => NumX * NumY;

		// --- Camera params (stored by RebuildAABBs) ---
		public float NearPlane { get; private set; }
		public float FarPlane { get; private set; }

		/// <summary>NDC tile size: 2 / NumX, 2 / NumY. Updated by RebuildAABBs.</summary>
		public float TileW { get; private set; }
		public float TileH { get; private set; }

		/// <summary>
		/// Compatibility stub: kept so ClusterEngine.Upload(grid.ClusterAABBs) still compiles.
		/// 2D tiled path does not use per-cluster AABBs; array stays empty (zeroed).
		/// </summary>
		public GpuClusterAABB[] ClusterAABBs { get; private set; }

		public ClusterGrid()
		{
			TileW = 2.0f / NumX;
			TileH = 2.0f / NumY;
			ClusterAABBs = new GpuClusterAABB[NumX * NumY];
		}

		/// <summary>
		/// Lightweight rebuild: stores near/far and refreshes tile size.
		/// No AABB computation (no-op for 2D tiling). Signature kept for ClusterEngine.
		/// </summary>
		public void RebuildAABBs(float nearPlane, float farPlane, float fovYRad, float aspectRatio)
		{
			NearPlane = nearPlane;
			FarPlane = farPlane;
			TileW = 2.0f / NumX;
			TileH = 2.0f / NumY;
			if (ClusterAABBs == null || ClusterAABBs.Length != TotalClusters)
			{
				ClusterAABBs = new GpuClusterAABB[TotalClusters];
			}
		}

		/// <summary>
		/// Linear tile index from 2D tile coordinates. Layout: idx = x + y * NumX
		/// Coordinates are clamped into range.
		/// </summary>
		public int TileIndex(int x, int y)
		{
			if (x < 0) x = 0;
			else if (x >= NumX) x = NumX - 1;
			if (y < 0) y = 0;
			else if (y >= NumY) y = NumY - 1;
			return x + y * NumX;
		}

		/// <summary>2D tile index: idx = x + y * NumX</summary>
		public int GetClusterIndex(int x, int y)
		{
			return TileIndex(x, y);
		}

		/// <summary>
		/// Compatibility overload: Z is ignored (2D grid), returns x + y * NumX.
		/// Kept so old 3-argument call sites still compile.
		/// </summary>
		public int GetClusterIndex(int x, int y, int z)
		{
			return TileIndex(x, y);
		}
	}
}
