using System.Runtime.InteropServices;

namespace LibRender2.Lightings
{
	/// <summary>CPU mirror of ClusterLight in light_cull.comp. World space; the shader applies viewMatrix.</summary>
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

		/// <summary>Byte size of one entry. Must stay 48 (3 x vec3+float pairs).</summary>
		public const int Stride = 48;

		/// <summary>Packs a scene light for the SSBO. False for entries the culler must skip.</summary>
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
			data.Intensity = 1.0f;
			data.Dx = (float)light.Direction.X;
			data.Dy = (float)light.Direction.Y;
			data.Dz = (float)light.Direction.Z;
			data.Cutoff = light.Type == LightType.Spot ? light.SpotCutoff : -1.0f;
			return true;
		}
	}
}
