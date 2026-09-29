using System;
using OpenBveApi.Math;

namespace LibRender2.ShadowMapping
{
	/// <summary>
	/// Utility class for camera frustum calculations, specifically for Cascaded Shadow Mapping.
	/// </summary>
	public static class FrustumUtils
	{
		/// <summary>
		/// Extracts the 8 corners of the frustum in world space using the inverse View-Projection matrix.
		/// </summary>
		public static Vector3[] GetFrustumCornersWorldSpace(Matrix4D invVP)
		{
			Vector3[] corners = new Vector3[8];
			Vector4[] ndcCorners = 
			{
				new Vector4(-1, -1, -1, 1), new Vector4(1, -1, -1, 1),
				new Vector4(-1,  1, -1, 1), new Vector4(1,  1, -1, 1),
				new Vector4(-1, -1,  1, 1), new Vector4(1, -1,  1, 1),
				new Vector4(-1,  1,  1, 1), new Vector4(1,  1,  1, 1)
			};

			for (int i = 0; i < 8; i++)
			{
				Vector4 worldPt = Vector4.Transform(ndcCorners[i], invVP);
				if (Math.Abs(worldPt.W) < 1e-10)
				{
					corners[i] = worldPt.Xyz; // fallback for W=0
				}
				else
				{
					corners[i] = worldPt.Xyz / worldPt.W;
				}
			}

			return corners;
		}

		/// <summary>
		/// Computes split distances using the Parallel Split Shadow Maps (PSSM) algorithm.
		/// </summary>
		public static double[] ComputeSplitDistances(int cascadeCount, double zNear, double zFar, double lambda)
		{
			double[] splits = new double[cascadeCount + 1];
			splits[0] = zNear;
			splits[cascadeCount] = zFar;

			for (int i = 1; i < cascadeCount; i++)
			{
				double p = (double)i / cascadeCount;
				double log = zNear * Math.Pow(zFar / zNear, p);
				double lin = zNear + (zFar - zNear) * p;
				splits[i] = lambda * log + (1.0 - lambda) * lin;
			}

			return splits;
		}

		/// <summary>
		/// Interpolates between full frustum corners to get a sub-frustum between nearFrac and farFrac [0,1].
		/// </summary>
		public static Vector3[] GetSubFrustumCorners(Vector3[] fullCorners, double nearFrac, double farFrac)
		{
			Vector3[] corners = new Vector3[8];
			for (int i = 0; i < 4; i++)
			{
				Vector3 dir = fullCorners[i + 4] - fullCorners[i];
				corners[i] = fullCorners[i] + dir * nearFrac;
				corners[i + 4] = fullCorners[i] + dir * farFrac;
			}
			return corners;
		}

		/// <summary>
		/// Computes a stable radius for a bounding sphere circumscribing the sub-frustum.
		/// </summary>
		public static double GetStableRadius(double zNear, double zFar, double fovYRad, double aspect)
		{
			// Clamp min FOV to 45 degrees (0.785 rad) to prevent the sphere from shrinking too much when zooming.
			// This ensures large objects (like trains) don't get clipped from the shadow map at high zoom.
			fovYRad = Math.Max(fovYRad, 0.785398); 
			// Half-height/width of the far plane of this sub-frustum in camera space
			double h = zFar * Math.Tan(fovYRad / 2.0);
			double w = h * aspect;
			
			// The farthest corner of the subfrustum is on the far plane
			// Vector3 farCorner = (w, h, zFar)
			// Vector3 center = (0, 0, (zNear + zFar) / 2)
			// radius = distance from center to farCorner
			double centerZ = (zNear + zFar) / 2.0;
			double dz = zFar - centerZ;
			
			return Math.Sqrt(w * w + h * h + dz * dz);
		}

		/// <summary>
		/// Computes the average center of a set of points.
		/// </summary>
		public static Vector3 ComputeCenter(Vector3[] points)
		{
			Vector3 center = Vector3.Zero;
			if (points == null || points.Length == 0) return center;
			foreach (var p in points) center += p;
			return center / points.Length;
		}

		/// <summary>
		/// Computes the radius of a bounding sphere that encompasses all points.
		/// </summary>
		public static double ComputeBoundingSphereRadius(Vector3[] points, Vector3 center)
		{
			double maxDistSq = 0;
			foreach (var p in points)
			{
				double d2 = (p - center).NormSquared();
				if (d2 > maxDistSq) maxDistSq = d2;
			}
			return Math.Sqrt(maxDistSq);
		}

		/// <summary>
		/// Extracts the six frustum planes from a view-projection matrix.
		/// Row-vector convention (clip = world * (view * projection)): planes come from matrix columns.
		/// </summary>
		public static FrustumPlane[] GetFrustumPlanesWorldSpace(Matrix4D viewProjection)
		{
			return new FrustumPlane[]
			{
				MakePlane(viewProjection.Row0.X + viewProjection.Row0.W, viewProjection.Row1.X + viewProjection.Row1.W, viewProjection.Row2.X + viewProjection.Row2.W, viewProjection.Row3.X + viewProjection.Row3.W),
				MakePlane(viewProjection.Row0.W - viewProjection.Row0.X, viewProjection.Row1.W - viewProjection.Row1.X, viewProjection.Row2.W - viewProjection.Row2.X, viewProjection.Row3.W - viewProjection.Row3.X),
				MakePlane(viewProjection.Row0.Y + viewProjection.Row0.W, viewProjection.Row1.Y + viewProjection.Row1.W, viewProjection.Row2.Y + viewProjection.Row2.W, viewProjection.Row3.Y + viewProjection.Row3.W),
				MakePlane(viewProjection.Row0.W - viewProjection.Row0.Y, viewProjection.Row1.W - viewProjection.Row1.Y, viewProjection.Row2.W - viewProjection.Row2.Y, viewProjection.Row3.W - viewProjection.Row3.Y),
				MakePlane(viewProjection.Row0.Z + viewProjection.Row0.W, viewProjection.Row1.Z + viewProjection.Row1.W, viewProjection.Row2.Z + viewProjection.Row2.W, viewProjection.Row3.Z + viewProjection.Row3.W),
				MakePlane(viewProjection.Row0.W - viewProjection.Row0.Z, viewProjection.Row1.W - viewProjection.Row1.Z, viewProjection.Row2.W - viewProjection.Row2.Z, viewProjection.Row3.W - viewProjection.Row3.Z),
			};
		}

		private static FrustumPlane MakePlane(double nx, double ny, double nz, double distance)
		{
			double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
			if (length < 1e-10)
			{
				// Degenerate: keep everything rather than cull it.
				return new FrustumPlane { Normal = Vector3.Zero, Distance = double.MaxValue };
			}
			return new FrustumPlane { Normal = new Vector3(nx / length, ny / length, nz / length), Distance = distance / length };
		}

		/// <summary>True when a sphere touches the frustum. Null planes disable culling.</summary>
		public static bool SphereVisible(FrustumPlane[] planes, Vector3 center, double radius)
		{
			if (planes == null)
			{
				return true;
			}
			for (int i = 0; i < planes.Length; i++)
			{
				if (Vector3.Dot(planes[i].Normal, center) + planes[i].Distance < -radius)
				{
					return false;
				}
			}
			return true;
		}
	}

	/// <summary>One normalized frustum plane: Dot(Normal, p) + Distance >= 0 is inside.</summary>
	public struct FrustumPlane
	{
		public Vector3 Normal;
		public double Distance;
	}
}
