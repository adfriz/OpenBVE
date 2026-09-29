using OpenBveApi.Colors;
using OpenBveApi.Math;

namespace LibRender2.Lightings
{
	/// <summary>What kind of light this is.</summary>
	public enum LightType
	{
		Directional,
		Point,
		Spot
	}

	/// <summary>Selection priority when lights outnumber shader slots (highest first).</summary>
	public enum LightPriority
	{
		Head,
		Tail,
		Scenic,
		Station,
		Ambient
	}

	/// <summary>One light in the scene, in world space.</summary>
	public struct SceneLight
	{
		public LightType Type;
		public LightPriority Priority;
		/// <summary>Sun direction, or point/spot position.</summary>
		public Vector3 Position;
		/// <summary>Spot beam direction.</summary>
		public Vector3 Direction;
		public Color24 Color;
		public Color24 Ambient;
		/// <summary>Reach in meters (point/spot).</summary>
		public float Range;
		/// <summary>Cos(half-angle) of the spot cone.</summary>
		public float SpotCutoff;
		public bool CastsShadow;

		// The single global sun, built from current lighting state.
		public static SceneLight Sun(Lighting lighting)
		{
			return new SceneLight
			{
				Type = LightType.Directional,
				Priority = LightPriority.Ambient,
				Position = lighting.OptionLightPosition,
				Color = lighting.OptionDiffuseColor,
				Ambient = lighting.OptionAmbientColor,
				Range = float.PositiveInfinity,
				SpotCutoff = -1.0f,
				CastsShadow = true
			};
		}
	}
}
