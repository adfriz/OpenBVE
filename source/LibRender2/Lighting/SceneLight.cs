using System;
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

	/// <summary>Who wins when there are more lights than shader slots.</summary>
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
		/// <summary>Where the light sits (sun: where it shines from).</summary>
		public Vector3 Position;
		/// <summary>Where a spot aims.</summary>
		public Vector3 Direction;
		public Color24 Color;
		public Color24 Ambient;
		/// <summary>How far the light reaches, in meters.</summary>
		public float Range;
		/// <summary>Spot cone tightness, as cos(half-angle).</summary>
		public float SpotCutoff;
		public bool CastsShadow;
		/// <summary>Lights that are off get skipped. Factories switch this on.</summary>
		public bool Enabled;

		// The one sun, built from the current sky.
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
				CastsShadow = true,
				Enabled = true
			};
		}

		// A plain point light, no shadows.
		public static SceneLight Point(Vector3 position, Color24 color, float range, LightPriority priority)
		{
			return new SceneLight
			{
				Type = LightType.Point,
				Priority = priority,
				Position = position,
				Color = color,
				Range = range,
				SpotCutoff = -1.0f,
				CastsShadow = false,
				Enabled = true
			};
		}

		// A spot light. Pass the full cone width in degrees.
		public static SceneLight Spot(Vector3 position, Vector3 direction, Color24 color, float range, double coneAngleDegrees, LightPriority priority)
		{
			double halfAngle = coneAngleDegrees * Math.PI / 360.0;
			return new SceneLight
			{
				Type = LightType.Spot,
				Priority = priority,
				Position = position,
				Direction = direction,
				Color = color,
				Range = range,
				SpotCutoff = (float)Math.Cos(halfAngle),
				CastsShadow = false,
				Enabled = true
			};
		}
	}
}
