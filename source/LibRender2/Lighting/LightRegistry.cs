using System.Collections.Generic;
using LibRender2.Shaders;
using OpenBveApi.Colors;
using OpenBveApi.Math;

namespace LibRender2.Lightings
{
	/// <summary>Collects scene lights (sun + dynamic point/spot) and uploads them to shaders.</summary>
	public class LightRegistry
	{
		private readonly BaseRenderer renderer;

		/// <summary>Dynamic point/spot lights in world space. Empty until producers register.</summary>
		public readonly List<SceneLight> DynamicLights = new List<SceneLight>();

		internal LightRegistry(BaseRenderer renderer)
		{
			this.renderer = renderer;
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

		// Closest-N lights to the camera, priority first. Frustum culling plugs in here later.
		public List<SceneLight> SelectNearest(Vector3 cameraPosition, int maxCount)
		{
			List<SceneLight> inRange = new List<SceneLight>();
			for (int i = 0; i < DynamicLights.Count; i++)
			{
				SceneLight light = DynamicLights[i];
				if (light.Type == LightType.Directional)
				{
					continue;
				}
				double dx = light.Position.X - cameraPosition.X;
				double dy = light.Position.Y - cameraPosition.Y;
				double dz = light.Position.Z - cameraPosition.Z;
				if (dx * dx + dy * dy + dz * dz <= (double)light.Range * light.Range)
				{
					inRange.Add(light);
				}
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

		// Uploads dynamic lights in view space. Sun stays on uLight; this only fills uDynamicLights.
		public void UploadDynamic(Shader shader, Matrix4D viewMatrix, Vector3 cameraPosition)
		{
			List<SceneLight> selected = SelectNearest(cameraPosition, Shader.MaxDynamicLights);
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
