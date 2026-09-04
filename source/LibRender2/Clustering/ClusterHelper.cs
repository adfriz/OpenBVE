using System;
using System.Collections.Generic;
using OpenBveApi.Math;
using OpenBveApi.Objects;

namespace LibRender2.Clustering
{
	/// <summary>
	/// CPU-side helpers for 2D tiled (16x8) light culling.
	/// No OpenGL calls, no P/Invoke.
	/// All positions/directions must already be in the same space (typically view space).
	/// </summary>
	internal static class ClusterHelper
	{
		/// <summary>
		/// Screen-space tile index from pixel coordinates.
		/// </summary>
		/// <param name="sx">Screen X in pixels.</param>
		/// <param name="sy">Screen Y in pixels.</param>
		/// <param name="tileW">Tile width in pixels (&gt;0).</param>
		/// <param name="tileH">Tile height in pixels (&gt;0).</param>
		public static int TileIndex(float sx, float sy, float tileW, float tileH, int numX, int numY)
		{
			if (numX <= 0 || numY <= 0 || tileW <= 0.0f || tileH <= 0.0f)
			{
				return 0;
			}
			int x = (int)(sx / tileW);
			int y = (int)(sy / tileH);
			if (x < 0) x = 0;
			else if (x >= numX) x = numX - 1;
			if (y < 0) y = 0;
			else if (y >= numY) y = numY - 1;
			return x + y * numX;
		}

		/// <summary>
		/// Conservative sphere-vs-tile test for point lights.
		/// Tile is an XY AABB in view space; depth is the visible half-space Z &lt;= 0.
		/// </summary>
		/// <param name="viewPos">Light position in view space.</param>
		/// <param name="range">Light range (radius).</param>
		/// <param name="tileMinX">Tile AABB min X (view space).</param>
		/// <param name="tileMaxX">Tile AABB max X (view space).</param>
		/// <param name="tileMinY">Tile AABB min Y (view space).</param>
		/// <param name="tileMaxY">Tile AABB max Y (view space).</param>
		public static bool LightAffectsTile(Vector3 viewPos, float range,
			float tileMinX, float tileMaxX, float tileMinY, float tileMaxY)
		{
			if (range <= 0.0f)
			{
				return false;
			}
			double cx = viewPos.X < tileMinX ? tileMinX : (viewPos.X > tileMaxX ? tileMaxX : viewPos.X);
			double cy = viewPos.Y < tileMinY ? tileMinY : (viewPos.Y > tileMaxY ? tileMaxY : viewPos.Y);
			// Closest visible Z to the light: view space is visible for Z <= 0.
			double cz = viewPos.Z < 0.0 ? viewPos.Z : 0.0;

			double dx = viewPos.X - cx;
			double dy = viewPos.Y - cy;
			double dz = viewPos.Z - cz;
			double distSq = dx * dx + dy * dy + dz * dz;
			double rangeSq = (double)range * range;
			return distSq <= rangeSq;
		}

		/// <summary>
		/// Sphere-vs-tile test plus spot cone check.
		/// </summary>
		/// <param name="viewPos">Light position in view space.</param>
		/// <param name="range">Light range (radius).</param>
		/// <param name="direction">Spot direction in view space (need not be normalized).</param>
		/// <param name="cutoff">Spot cutoff as cosine (dot threshold, e.g. cos(45deg) ~= 0.707).</param>
		public static bool LightAffectsTile(Vector3 viewPos, float range,
			float tileMinX, float tileMaxX, float tileMinY, float tileMaxY,
			Vector3 direction, float cutoff)
		{
			// Broad phase: sphere must touch the tile at all.
			if (!LightAffectsTile(viewPos, range, tileMinX, tileMaxX, tileMinY, tileMaxY))
			{
				return false;
			}

			// Degenerate direction/cutoff => treat as point light.
			double dirLenSq = direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z;
			if (dirLenSq < 1e-12 || cutoff >= 1.0f)
			{
				return true;
			}
			double dirLen = System.Math.Sqrt(dirLenSq);
			double dirX = direction.X / dirLen;
			double dirY = direction.Y / dirLen;
			double dirZ = direction.Z / dirLen;

			// Closest point on the tile slab to the light.
			double cx = viewPos.X < tileMinX ? tileMinX : (viewPos.X > tileMaxX ? tileMaxX : viewPos.X);
			double cy = viewPos.Y < tileMinY ? tileMinY : (viewPos.Y > tileMaxY ? tileMaxY : viewPos.Y);
			double cz = viewPos.Z < 0.0 ? viewPos.Z : 0.0;

			double vx = cx - viewPos.X;
			double vy = cy - viewPos.Y;
			double vz = cz - viewPos.Z;
			double distSq = vx * vx + vy * vy + vz * vz;

			// Light center inside (or on) the tile slab => conservatively affected.
			if (distSq < 1e-12)
			{
				return true;
			}
			double dist = System.Math.Sqrt(distSq);
			double dot = (vx * dirX + vy * dirY + vz * dirZ) / dist;
			if (dot >= cutoff)
			{
				return true;
			}

			// Conservative angular margin: tile may still poke into the cone
			// even when its closest point is outside. tileRadius/dist approximates
			// the angular radius of the tile as seen from the light.
			double sizeX = (double)tileMaxX - tileMinX;
			double sizeY = (double)tileMaxY - tileMinY;
			double tileRadius = 0.5 * System.Math.Sqrt(sizeX * sizeX + sizeY * sizeY);
			double adjusted = (double)cutoff - tileRadius / dist;
			return dot >= adjusted;
		}

		/// <summary>
		/// Sorts lights in place by squared distance to the camera, truncates to cap.
		/// </summary>
		/// <param name="lights">Light list to sort (sorted in place).</param>
		/// <param name="camPos">Camera position in the same space as light positions.</param>
		/// <param name="cap">Maximum lights to keep (default 64).</param>
		/// <returns>Number of lights kept (min(original count, cap)).</returns>
		public static int SortAndCap(List<SceneLight> lights, Vector3 camPos, int cap = 64)
		{
			if (lights == null || lights.Count == 0)
			{
				return 0;
			}
			if (cap < 0)
			{
				cap = 0;
			}
			lights.Sort(delegate (SceneLight a, SceneLight b)
			{
				double adx = a.Position.X - camPos.X;
				double ady = a.Position.Y - camPos.Y;
				double adz = a.Position.Z - camPos.Z;
				double bdx = b.Position.X - camPos.X;
				double bdy = b.Position.Y - camPos.Y;
				double bdz = b.Position.Z - camPos.Z;
				double da = adx * adx + ady * ady + adz * adz;
				double db = bdx * bdx + bdy * bdy + bdz * bdz;
				if (da < db) return -1;
				if (da > db) return 1;
				return 0;
			});
			if (lights.Count > cap)
			{
				lights.RemoveRange(cap, lights.Count - cap);
			}
			return lights.Count;
		}

		/// <summary>
		/// Packs per-tile light index lists into flat headers + flat index buffer.
		/// Each tile is capped to maxPerTile entries (default 16).
		/// </summary>
		/// <param name="perTileLights">Per-tile light indices, length == headers.Length.</param>
		/// <param name="headers">Output headers (offset + count per tile).</param>
		/// <param name="flatIndices">Output flat index buffer.</param>
		/// <param name="maxPerTile">Cap per tile (default 16).</param>
		public static void PackTileList(IList<int>[] perTileLights, GpuCluster[] headers, uint[] flatIndices, int maxPerTile = 16)
		{
			if (headers == null || flatIndices == null || perTileLights == null)
			{
				return;
			}
			if (maxPerTile < 0)
			{
				maxPerTile = 0;
			}
			int tileCount = headers.Length < perTileLights.Length ? headers.Length : perTileLights.Length;
			int cursor = 0;
			for (int t = 0; t < tileCount; t++)
			{
				IList<int> list = perTileLights[t];
				int count = 0;
				uint offset = (uint)cursor;
				if (list != null && list.Count > 0 && cursor < flatIndices.Length)
				{
					int n = list.Count < maxPerTile ? list.Count : maxPerTile;
					int room = flatIndices.Length - cursor;
					if (n > room)
					{
						n = room;
					}
					for (int i = 0; i < n; i++)
					{
						int idx = list[i];
						flatIndices[cursor++] = idx < 0 ? 0u : (uint)idx;
					}
					count = n;
				}
				GpuCluster h = new GpuCluster();
				h.LightOffset = offset;
				h.LightCount = (uint)count;
				headers[t] = h;
			}
		}
	}
}
