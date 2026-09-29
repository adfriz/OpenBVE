using System;
using System.Collections.Generic;
using LibRender2.Shaders;
using LibRender2.ShadowMapping;
using OpenBveApi.Colors;
using OpenBveApi.Hosts;
using OpenBveApi.Math;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Lightings
{
	/// <summary>Picks which lights matter for the current view.</summary>
	public interface ILightSelectionStrategy
	{
		/// <summary>False means unavailable; the registry then uses the CPU path.</summary>
		bool TrySelect(List<SceneLight> lights, Vector3 cameraPosition, FrustumPlane[] frustum, int maxCount, out List<SceneLight> selected);
	}

	/// <summary>CPU selection: range, frustum, priority, closest-N. Always works.</summary>
	public sealed class CpuNearestStrategy : ILightSelectionStrategy
	{
		public static readonly CpuNearestStrategy Instance = new CpuNearestStrategy();

		private CpuNearestStrategy()
		{
		}

		public bool TrySelect(List<SceneLight> lights, Vector3 cameraPosition, FrustumPlane[] frustum, int maxCount, out List<SceneLight> selected)
		{
			selected = SelectCore(lights, cameraPosition, frustum, maxCount);
			return true;
		}

		internal static List<SceneLight> SelectCore(List<SceneLight> lights, Vector3 cameraPosition, FrustumPlane[] frustum, int maxCount)
		{
			List<SceneLight> inRange = new List<SceneLight>();
			for (int i = 0; i < lights.Count; i++)
			{
				SceneLight light = lights[i];
				if (!light.Enabled || light.Type == LightType.Directional || light.Range <= 0.0f)
				{
					continue;
				}
				if (DistanceSquared(light.Position, cameraPosition) > (double)light.Range * light.Range)
				{
					continue;
				}
				if (!FrustumUtils.SphereVisible(frustum, light.Position, light.Range))
				{
					continue;
				}
				inRange.Add(light);
			}
			inRange.Sort((a, b) =>
			{
				int priority = a.Priority.CompareTo(b.Priority);
				if (priority != 0)
				{
					return priority;
				}
				double da = DistanceSquared(a.Position, cameraPosition);
				double db = DistanceSquared(b.Position, cameraPosition);
				return da.CompareTo(db);
			});
			if (inRange.Count > maxCount)
			{
				inRange.RemoveRange(maxCount, inRange.Count - maxCount);
			}
			return inRange;
		}

		private static double DistanceSquared(Vector3 a, Vector3 b)
		{
			double dx = a.X - b.X;
			double dy = a.Y - b.Y;
			double dz = a.Z - b.Z;
			return dx * dx + dy * dy + dz * dz;
		}
	}

	/// <summary>Compute selection once hardware allows it. CPU until then.</summary>
	public sealed class ComputeLightSelector : ILightSelectionStrategy
	{
		private readonly BaseRenderer renderer;

		public ComputeLightSelector(BaseRenderer renderer)
		{
			this.renderer = renderer;
		}

		public bool TrySelect(List<SceneLight> lights, Vector3 cameraPosition, FrustumPlane[] frustum, int maxCount, out List<SceneLight> selected)
		{
			selected = null;
			if (!LightCapabilities.CanUseCompute(renderer))
			{
				return false;
			}
			// Dispatch not wired yet: same result as CPU until light_clusters/light_cull land.
			selected = CpuNearestStrategy.SelectCore(lights, cameraPosition, frustum, maxCount);
			return true;
		}
	}

	/// <summary>Checks once whether compute shaders can run. macOS caps at GL 4.1, so never there.</summary>
	public static class LightCapabilities
	{
		private static bool probed;
		private static bool computeAvailable;

		public static bool CanUseCompute(BaseRenderer renderer)
		{
			if (probed)
			{
				return computeAvailable;
			}
			if (renderer == null || renderer.currentHost == null || renderer.currentHost.Platform == HostPlatform.AppleOSX)
			{
				// Don't latch: no valid context to probe yet.
				return false;
			}
			probed = true;
			computeAvailable = false;
			try
			{
				string version = GL.GetString(StringName.Version);
				if (!string.IsNullOrEmpty(version))
				{
					string[] parts = version.Split(new[] { '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
					int major = 0;
					int minor = 0;
					if (parts.Length >= 2 && int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor))
					{
						computeAvailable = major > 4 || (major == 4 && minor >= 3);
					}
				}
			}
			catch
			{
				computeAvailable = false;
			}
			return computeAvailable;
		}
	}

	/// <summary>Keeps every light in the scene and sends the visible ones to the shaders.</summary>
	public class LightRegistry
	{
		private readonly BaseRenderer renderer;

		/// <summary>Point/spot/area lights in world space. Empty until something registers one.</summary>
		public readonly List<SceneLight> DynamicLights = new List<SceneLight>();

		/// <summary>How lights get picked. CPU by default; swap in ComputeLightSelector to try compute.</summary>
		public ILightSelectionStrategy SelectionStrategy;

		private int version;
		private int cachedVersion = -1;
		private Vector3 cachedCamera;
		private int cachedMax;
		private FrustumPlane[] cachedFrustum;
		private List<SceneLight> cachedSelection = new List<SceneLight>();

		internal LightRegistry(BaseRenderer renderer)
		{
			this.renderer = renderer;
			SelectionStrategy = CpuNearestStrategy.Instance;
		}

		// Any list change bumps the version so the cached selection refreshes.
		private void Touch()
		{
			version++;
		}

		/// <summary>Adds a light and returns its index.</summary>
		/// <remarks>Producer contract (PR #1328): owners (animated objects, cars) push world-space
		/// lights here, refresh animated ones via Update() each frame, and toggle Enabled for
		/// time-of-day or headlight-state switches. Eviction order is LightPriority.</remarks>
		public int Register(SceneLight light)
		{
			DynamicLights.Add(light);
			Touch();
			return DynamicLights.Count - 1;
		}

		/// <summary>Replaces the light at an index (e.g. animated objects each frame).</summary>
		public void Update(int index, SceneLight light)
		{
			if (index < 0 || index >= DynamicLights.Count)
			{
				throw new ArgumentOutOfRangeException(nameof(index));
			}
			DynamicLights[index] = light;
			Touch();
		}

		/// <summary>Swap-removes the light at an index. The last light moves into the gap, so any stored indices for it change.</summary>
		public void RemoveAt(int index)
		{
			if (index < 0 || index >= DynamicLights.Count)
			{
				throw new ArgumentOutOfRangeException(nameof(index));
			}
			int last = DynamicLights.Count - 1;
			DynamicLights[index] = DynamicLights[last];
			DynamicLights.RemoveAt(last);
			Touch();
		}

		/// <summary>Drops the cached selection so the next query recomputes (e.g. projection changed).</summary>
		public void Invalidate()
		{
			cachedVersion = -1;
		}

		public void Clear()
		{
			if (DynamicLights.Count != 0)
			{
				DynamicLights.Clear();
				Touch();
			}
		}

		// Uploads one directional sun. Position must already be in view space.
		public void UploadSun(Shader shader, Vector3 viewSpacePosition, Color24 ambient, Color24 diffuse)
		{
			Lighting lighting = renderer.Lighting;
			shader.SetIsLight(true);
			shader.SetLightPosition(viewSpacePosition);
			shader.SetLightAmbient(ambient);
			shader.SetLightDiffuse(diffuse);
			shader.SetLightSpecular(lighting.OptionSpecularColor);
			shader.SetLightModel(lighting.LightModel);
		}

		// Same as above, with colors from the sun entry.
		public void UploadSun(Shader shader, Vector3 viewSpacePosition)
		{
			SceneLight sun = SceneLight.Sun(renderer.Lighting);
			UploadSun(shader, viewSpacePosition, sun.Ambient, sun.Color);
		}

		// Closest-N lights to the camera, priority first. No frustum culling.
		public List<SceneLight> SelectNearest(Vector3 cameraPosition, int maxCount)
		{
			return SelectNearest(cameraPosition, null, maxCount);
		}

		// Closest-N lights whose range sphere touches the frustum, priority first.
		public List<SceneLight> SelectNearest(Vector3 cameraPosition, FrustumPlane[] frustum, int maxCount)
		{
			if (version == cachedVersion && cameraPosition == cachedCamera && maxCount == cachedMax && FrustumEquals(cachedFrustum, frustum))
			{
				return new List<SceneLight>(cachedSelection);
			}
			List<SceneLight> selected;
			if (SelectionStrategy == null || !SelectionStrategy.TrySelect(DynamicLights, cameraPosition, frustum, maxCount, out selected) || selected == null)
			{
				selected = CpuNearestStrategy.SelectCore(DynamicLights, cameraPosition, frustum, maxCount);
			}
			cachedVersion = version;
			cachedCamera = cameraPosition;
			cachedMax = maxCount;
			cachedFrustum = CloneFrustum(frustum);
			cachedSelection = selected;
			return new List<SceneLight>(selected);
		}

		// Frustum is derived from the view-projection matrix, so camera rotation alone changes it.
		// Without this check a stationary camera that turns would keep a stale selection.
		private static bool FrustumEquals(FrustumPlane[] a, FrustumPlane[] b)
		{
			if (ReferenceEquals(a, b))
			{
				return true;
			}
			if (a == null || b == null || a.Length != b.Length)
			{
				return false;
			}
			for (int i = 0; i < a.Length; i++)
			{
				if (a[i].Normal.X != b[i].Normal.X || a[i].Normal.Y != b[i].Normal.Y || a[i].Normal.Z != b[i].Normal.Z || a[i].Distance != b[i].Distance)
				{
					return false;
				}
			}
			return true;
		}

		private static FrustumPlane[] CloneFrustum(FrustumPlane[] frustum)
		{
			if (frustum == null)
			{
				return null;
			}
			FrustumPlane[] copy = new FrustumPlane[frustum.Length];
			Array.Copy(frustum, copy, frustum.Length);
			return copy;
		}

		// Uploads dynamic lights in view space. Sun stays on uLight; this only fills uDynamicLights.
		public void UploadDynamic(Shader shader, Matrix4D viewMatrix, Vector3 cameraPosition)
		{
			if (DynamicLights.Count == 0)
			{
				shader.SetDynamicLightCount(0);
				return;
			}
			Matrix4D viewProjection = viewMatrix * renderer.CurrentProjectionMatrix;
			FrustumPlane[] frustum = FrustumUtils.GetFrustumPlanesWorldSpace(viewProjection);
			List<SceneLight> selected = SelectNearest(cameraPosition, frustum, Shader.MaxDynamicLights);
			shader.SetDynamicLightCount(selected.Count);
			for (int i = 0; i < selected.Count; i++)
			{
				SceneLight light = selected[i];
				Vector3 position = new Vector3(light.Position);
				position.Transform(viewMatrix, false);
				Vector3 direction = new Vector3(light.Direction);
				direction.Transform(viewMatrix);
				float cutoff = -1.0f;
				if (light.Type == LightType.Spot)
				{
					if (direction.X == 0.0 && direction.Y == 0.0 && direction.Z == 0.0)
					{
						direction = new Vector3(0.0, 0.0, -1.0);
					}
					cutoff = light.SpotCutoff;
				}
				shader.SetDynamicLight(i, position, direction, light.Color, light.Range, cutoff);
			}
		}
	}
}
