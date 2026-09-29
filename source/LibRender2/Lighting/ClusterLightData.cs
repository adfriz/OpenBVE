using System.Runtime.InteropServices;

namespace LibRender2.Lightings
{
	/// <summary>One light as the culling shader sees it. World space; the shader applies the view matrix.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct ClusterLightData
	{
		public float Px;
		public float Py;
		public float Pz;
		public float Range;
		public float R;
		public float G;
		public float B;
		public float Intensity;
		public float Dx;
		public float Dy;
		public float Dz;
		public float Cutoff;

		/// <summary>48 bytes: three vec3+float pairs.</summary>
		public const int Stride = 48;

		/// <summary>Fills one entry. False for lights the culler must ignore.</summary>
		public static bool TryPack(SceneLight light, out ClusterLightData data)
		{
			data = new ClusterLightData();
			if (!light.Enabled || light.Type == LightType.Directional || light.Range <= 0.0f)
			{
				return false;
			}
			data.Px = (float)light.Position.X;
			data.Py = (float)light.Position.Y;
			data.Pz = (float)light.Position.Z;
			data.Range = light.Range;
			data.R = light.Color.R / 255.0f;
			data.G = light.Color.G / 255.0f;
			data.B = light.Color.B / 255.0f;
			// Baked per the PR #1328 model. Radius/softness/area need a wider SSBO and shader support first.
			data.Intensity = light.ShadingIntensity();
			data.Dx = (float)light.Direction.X;
			data.Dy = (float)light.Direction.Y;
			data.Dz = (float)light.Direction.Z;
			data.Cutoff = light.Type == LightType.Spot ? light.SpotCutoff : -1.0f;
			return true;
		}
	}
}
