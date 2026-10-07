using System;
using System.Linq;
using LibRender2.Cameras;
using LibRender2.Objects;
using OpenBveApi;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Routes;

namespace LibRender2.Scene
{
	/// <summary>
	/// Owns object culling / visibility bookkeeping extracted from BaseRenderer.
	/// Threading stays with the renderer; this class is the pure algorithm.
	/// </summary>
	public class VisibilityManager
	{
		private readonly Func<BaseOptions> optionsProvider;

		public int[] ObjectsSortedByStart { get; set; }
		public int[] ObjectsSortedByEnd { get; set; }
		public int ObjectsSortedByStartPointer { get; set; }
		public int ObjectsSortedByEndPointer { get; set; }
		public double LastUpdatedTrackPosition { get; set; }

		public VisibilityManager(Func<BaseOptions> optionsProvider)
		{
			this.optionsProvider = optionsProvider;
		}

		public VisibilityManager() : this(null)
		{
		}

		/// <summary>Builds the sorted start/end indices after objects are created.</summary>
		public void BuildSortIndices(System.Collections.Generic.List<ObjectState> staticObjects)
		{
			if (staticObjects == null)
			{
				return;
			}
			ObjectsSortedByStart = staticObjects.Select((x, i) => new { Index = i, Distance = x.StartingDistance }).OrderBy(x => x.Distance).Select(x => x.Index).ToArray();
			ObjectsSortedByEnd = staticObjects.Select((x, i) => new { Index = i, Distance = x.EndingDistance }).OrderBy(x => x.Distance).Select(x => x.Index).ToArray();
			ObjectsSortedByStartPointer = 0;
			ObjectsSortedByEndPointer = 0;
		}

		/// <summary>Populates the visible set for the initial camera position.</summary>
		public void InitializeVisibility(System.Collections.Generic.List<ObjectState> staticObjects, VisibleObjectLibrary visibleObjects, CameraProperties camera, TrackFollower cameraTrackFollower)
		{
			BaseOptions options = optionsProvider?.Invoke();
			if (options != null && options.ObjectDisposalMode == ObjectDisposalMode.QuadTree)
			{
				foreach (ObjectState state in staticObjects)
				{
					visibleObjects.quadTree.Add(state, Orientation3.Default);
				}
				visibleObjects.quadTree.Initialize(options.QuadTreeLeafSize);
				camera.UpdateQuadTreeLeaf();
				return;
			}

			double p = cameraTrackFollower.TrackPosition + camera.Alignment.Position.Z;
			foreach (ObjectState state in staticObjects.Where(recipe => recipe.StartingDistance <= p + camera.ForwardViewingDistance & recipe.EndingDistance >= p - camera.BackwardViewingDistance))
			{
				visibleObjects.ShowObject(state, ObjectType.Static);
			}
		}

		/// <summary>Incremental legacy visibility update; returns the new track position.</summary>
		public void UpdateLegacyVisibility(double trackPosition, System.Collections.Generic.List<ObjectState> staticObjects, VisibleObjectLibrary visibleObjects, CameraProperties camera, TrackFollower cameraTrackFollower)
		{
			if (ObjectsSortedByStart == null || ObjectsSortedByStart.Length == 0 || staticObjects.Count == 0)
			{
				return;
			}
			double d = trackPosition - LastUpdatedTrackPosition;
			int n = ObjectsSortedByStart.Length;
			double p = cameraTrackFollower.TrackPosition + camera.Alignment.Position.Z;

			if (d < 0.0)
			{
				if (ObjectsSortedByStartPointer >= n)
				{
					ObjectsSortedByStartPointer = n - 1;
				}

				if (ObjectsSortedByEndPointer >= n)
				{
					ObjectsSortedByEndPointer = n - 1;
				}

				// dispose
				while (ObjectsSortedByStartPointer >= 0)
				{
					int o = ObjectsSortedByStart[ObjectsSortedByStartPointer];

					if (staticObjects[o].StartingDistance > p + camera.ForwardViewingDistance)
					{
						visibleObjects.HideObject(staticObjects[o]);
						ObjectsSortedByStartPointer--;
					}
					else
					{
						break;
					}
				}

				// introduce
				while (ObjectsSortedByEndPointer >= 0)
				{
					int o = ObjectsSortedByEnd[ObjectsSortedByEndPointer];

					if (staticObjects[o].EndingDistance >= p - camera.BackwardViewingDistance)
					{
						if (staticObjects[o].StartingDistance <= p + camera.ForwardViewingDistance)
						{
							visibleObjects.ShowObject(staticObjects[o], ObjectType.Static);
						}

						ObjectsSortedByEndPointer--;
					}
					else
					{
						break;
					}
				}
			}
			else if (d > 0.0)
			{
				if (ObjectsSortedByStartPointer < 0)
				{
					ObjectsSortedByStartPointer = 0;
				}

				if (ObjectsSortedByEndPointer < 0)
				{
					ObjectsSortedByEndPointer = 0;
				}

				// dispose
				while (ObjectsSortedByEndPointer < n)
				{
					int o = ObjectsSortedByEnd[ObjectsSortedByEndPointer];

					if (staticObjects[o].EndingDistance < p - camera.BackwardViewingDistance)
					{
						visibleObjects.HideObject(staticObjects[o]);
						ObjectsSortedByEndPointer++;
					}
					else
					{
						break;
					}
				}
				n = ObjectsSortedByStart.Length;

				// introduce
				while (ObjectsSortedByStartPointer < n)
				{
					int o = ObjectsSortedByStart[ObjectsSortedByStartPointer];

					if (staticObjects[o].StartingDistance <= p + camera.ForwardViewingDistance)
					{
						if (staticObjects[o].EndingDistance >= p - camera.BackwardViewingDistance)
						{
							visibleObjects.ShowObject(staticObjects[o], ObjectType.Static);
						}

						ObjectsSortedByStartPointer++;
					}
					else
					{
						break;
					}
				}
			}

			LastUpdatedTrackPosition = trackPosition;
		}

		/// <summary>Derives forward/backward viewing distances from the camera frustum.</summary>
		public void UpdateViewingDistances(double backgroundImageDistance, CameraProperties camera, TrackFollower cameraTrackFollower)
		{
			double f = Math.Atan2(cameraTrackFollower.WorldDirection.Z, cameraTrackFollower.WorldDirection.X);
			double c = Math.Atan2(camera.AbsoluteDirection.Z, camera.AbsoluteDirection.X) - f;
			if (c < -Math.PI)
			{
				c += 2.0 * Math.PI;
			}
			else if (c > Math.PI)
			{
				c -= 2.0 * Math.PI;
			}

			double a0 = c - 0.5 * camera.HorizontalViewingAngle;
			double a1 = c + 0.5 * camera.HorizontalViewingAngle;

			double max;
			if (a0 <= 0.0 & a1 >= 0.0)
			{
				max = 1.0;
			}
			else
			{
				double c0 = Math.Cos(a0);
				double c1 = Math.Cos(a1);
				max = c0 > c1 ? c0 : c1;
				if (max < 0.0) max = 0.0;
			}

			double min;
			if (a0 <= -Math.PI | a1 >= Math.PI)
			{
				min = -1.0;
			}
			else
			{
				double c0 = Math.Cos(a0);
				double c1 = Math.Cos(a1);
				min = c0 < c1 ? c0 : c1;
				if (min > 0.0) min = 0.0;
			}

			double d = backgroundImageDistance + camera.ExtraViewingDistance;
			camera.ForwardViewingDistance = d * max;
			camera.BackwardViewingDistance = -d * min;
		}
	}
}
