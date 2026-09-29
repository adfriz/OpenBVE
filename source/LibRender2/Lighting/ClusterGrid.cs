using System;
using OpenBveApi.Math;

namespace LibRender2.Lightings
{
	/// <summary>The cluster grid on the CPU. Same math as light_clusters.comp / light_cull.comp.</summary>
	public static class ClusterGrid
	{
		/// <summary>16x9x24 like DOOM 2016. Sent as gridSize at dispatch.</summary>
		public const int GridX = 16;
		public const int GridY = 9;
		public const int GridZ = 24;

		public const int ClusterCount = GridX * GridY * GridZ;

		/// <summary>Max lights per cluster. Must match CLUSTER_MAX_LIGHTS in both .comp files.</summary>
		public const int MaxLightsPerCluster = 100;

		/// <summary>Where slice i starts: exponential split between near and far.</summary>
		public static double SliceDepth(double zNear, double zFar, int slice, int sliceCount)
		{
			return zNear * Math.Pow(zFar / zNear, (double)slice / sliceCount);
		}

		/// <summary>Flat index: x + y*GridX + z*GridX*GridY.</summary>
		public static int TileIndex(int x, int y, int z)
		{
			return x + y * GridX + z * GridX * GridY;
		}

		/// <summary>Which depth slice a view-space depth falls in. Same formula the fragment shader uses.</summary>
		public static int ZSliceForDepth(double viewDepth, double zNear, double zFar)
		{
			if (viewDepth <= zNear)
			{
				return 0;
			}
			if (viewDepth >= zFar)
			{
				return GridZ - 1;
			}
			int slice = (int)(Math.Log(viewDepth / zNear) * GridZ / Math.Log(zFar / zNear));
			return Math.Min(Math.Max(slice, 0), GridZ - 1);
		}

		/// <summary>Which screen tile a pixel falls in. Origin bottom-left, like gl_FragCoord.</summary>
		public static void XyTileForPixel(double pixelX, double pixelY, double screenWidth, double screenHeight, out int tileX, out int tileY)
		{
			double tileSizeX = screenWidth / GridX;
			double tileSizeY = screenHeight / GridY;
			int tx = (int)(pixelX / tileSizeX);
			int ty = (int)(pixelY / tileSizeY);
			tileX = Math.Min(Math.Max(tx, 0), GridX - 1);
			tileY = Math.Min(Math.Max(ty, 0), GridY - 1);
		}

		/// <summary>Tile + slice combined: the one cluster a fragment lives in.</summary>
		public static int ClusterIndexForFragment(double pixelX, double pixelY, double viewDepth, double screenWidth, double screenHeight, double zNear, double zFar)
		{
			XyTileForPixel(pixelX, pixelY, screenWidth, screenHeight, out int tx, out int ty);
			return TileIndex(tx, ty, ZSliceForDepth(viewDepth, zNear, zFar));
		}

		/// <summary>View-space box for every cluster, same as the compute shader builds.</summary>
		public static void BuildAabbs(Matrix4D inverseProjection, double screenWidth, double screenHeight, double zNear, double zFar, Vector3[] minPoints, Vector3[] maxPoints)
		{
			if (minPoints == null || minPoints.Length < ClusterCount || maxPoints == null || maxPoints.Length < ClusterCount)
			{
				throw new ArgumentException("Both AABB arrays need room for ClusterCount entries.");
			}
			double tileSizeX = screenWidth / GridX;
			double tileSizeY = screenHeight / GridY;
			for (int z = 0; z < GridZ; z++)
			{
				double planeNear = SliceDepth(zNear, zFar, z, GridZ);
				double planeFar = SliceDepth(zNear, zFar, z + 1, GridZ);
				for (int y = 0; y < GridY; y++)
				{
					for (int x = 0; x < GridX; x++)
					{
						Vector3 minTile = ScreenToView(x * tileSizeX, y * tileSizeY, inverseProjection, screenWidth, screenHeight);
						Vector3 maxTile = ScreenToView((x + 1) * tileSizeX, (y + 1) * tileSizeY, inverseProjection, screenWidth, screenHeight);
						Vector3 minNear = LineIntersectionWithZPlane(minTile, planeNear);
						Vector3 minFar = LineIntersectionWithZPlane(minTile, planeFar);
						Vector3 maxNear = LineIntersectionWithZPlane(maxTile, planeNear);
						Vector3 maxFar = LineIntersectionWithZPlane(maxTile, planeFar);
						int index = TileIndex(x, y, z);
						// Same as the shader: min from the min-tile ray, max from the max-tile ray.
						minPoints[index] = Min(minNear, minFar);
						maxPoints[index] = Max(maxNear, maxFar);
					}
				}
			}
		}

		// Unproject a screen point pinned on the near plane.
		private static Vector3 ScreenToView(double screenX, double screenY, Matrix4D inverseProjection, double screenWidth, double screenHeight)
		{
			Vector4 ndc = new Vector4(
				screenX / screenWidth * 2.0 - 1.0,
				screenY / screenHeight * 2.0 - 1.0,
				-1.0,
				1.0);
			Vector4 view = Vector4.Transform(ndc, inverseProjection);
			if (Math.Abs(view.W) < 1e-10)
			{
				return view.Xyz;
			}
			return view.Xyz / view.W;
		}

		// Where the ray from the eye through the tile point crosses the depth plane.
		private static Vector3 LineIntersectionWithZPlane(Vector3 tilePoint, double zDistance)
		{
			// Direction from the eye (origin) through the tile point; plane normal is (0,0,-1).
			double t = zDistance / -tilePoint.Z;
			return new Vector3(tilePoint.X * t, tilePoint.Y * t, -zDistance);
		}

		private static Vector3 Min(Vector3 a, Vector3 b)
		{
			return new Vector3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
		}

		private static Vector3 Max(Vector3 a, Vector3 b)
		{
			return new Vector3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
		}
	}
}
