using System;
using OpenBveApi.Colors;
using OpenBveApi.Math;

namespace LibRender2.Lightings
{
	/// <summary>What kind of light this is. Same kinds as the [Light] blocks (PR #1328).</summary>
	public enum LightType
	{
		Directional,
		Point,
		Spot,
		Area
	}

	/// <summary>Who gets dropped first when lights outnumber shader slots.</summary>
	public enum LightPriority
	{
		Head,
		Tail,
		Scenic,
		Station,
		Ambient
	}

	/// <summary>One light in the scene, in world space. Same shape as the [Light] blocks (PR #1328).</summary>
	public struct SceneLight
	{
		/// <summary>4*pi: with defaults, a normalized light shines at intensity 1.</summary>
		public const float DefaultPower = 12.5663706f;

		public LightType Type;
		public LightPriority Priority;
		/// <summary>World position (sun: the direction it shines from).</summary>
		public Vector3 Position;
		/// <summary>Spot beam, or the area rect normal.</summary>
		public Vector3 Direction;
		public Color24 Color;
		public Color24 Ambient;
		/// <summary>Reach in meters.</summary>
		public float Range;
		/// <summary>Spot cone width as cos(half-angle).</summary>
		public float SpotCutoff;
		/// <summary>Output in Watts. The shader sees Power * 2^Exposure.</summary>
		public float Power;
		/// <summary>Brightness multiplier as 2^Exposure. 0 leaves it alone.</summary>
		public float Exposure;
		/// <summary>Spread power across the cone. Points always do.</summary>
		public bool Normalize;
		/// <summary>Source size in meters. Softens the falloff up close.</summary>
		public float Radius;
		/// <summary>Fade out smoothly at the range edge.</summary>
		public bool SoftFalloff;
		/// <summary>Spot edge softness, 0 to 1.</summary>
		public float Softness;
		/// <summary>Rect size, area lights only.</summary>
		public Vector2 AreaSize;
		/// <summary>Draw the debug cone. Lighting ignores it.</summary>
		public bool ShowCone;
		public bool CastsShadow;
		/// <summary>Off lights are skipped. Factories switch this on.</summary>
		public bool Enabled;

		/// <summary>What the shader multiplies the color by. Same math as the PR #1328 fragment shader.</summary>
		public float ShadingIntensity()
		{
			float intensity = Power * (float)Math.Pow(2.0, Exposure);
			if (Type == LightType.Spot && Normalize)
			{
				return intensity / Math.Max(6.2831853f * (1.0f - SpotCutoff), 0.0001f);
			}
			return Type == LightType.Point ? intensity / DefaultPower : intensity;
		}

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
				Power = DefaultPower,
				Exposure = 0.0f,
				Normalize = true,
				Radius = 0.0f,
				SoftFalloff = false,
				Softness = 1.0f,
				CastsShadow = true,
				Enabled = true
			};
		}

		// Plain point light, no shadows.
		public static SceneLight Point(Vector3 position, Color24 color, float range, LightPriority priority,
			float power = DefaultPower, float exposure = 0.0f, bool normalize = true, float radius = 0.0f, bool softFalloff = true)
		{
			return new SceneLight
			{
				Type = LightType.Point,
				Priority = priority,
				Position = position,
				Color = color,
				Range = range,
				SpotCutoff = -1.0f,
				Power = power,
				Exposure = exposure,
				Normalize = normalize,
				Radius = radius,
				SoftFalloff = softFalloff,
				Softness = 1.0f,
				CastsShadow = false,
				Enabled = true
			};
		}

		// Spot light. Cone width in degrees.
		public static SceneLight Spot(Vector3 position, Vector3 direction, Color24 color, float range, double coneAngleDegrees, LightPriority priority,
			float power = DefaultPower, float exposure = 0.0f, bool normalize = true, float radius = 0.0f, bool softFalloff = true, float softness = 1.0f)
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
				Power = power,
				Exposure = exposure,
				Normalize = normalize,
				Radius = radius,
				SoftFalloff = softFalloff,
				Softness = Math.Min(Math.Max(softness, 0.0f), 1.0f),
				CastsShadow = false,
				Enabled = true
			};
		}

		// Rectangular area light facing along direction.
		public static SceneLight Area(Vector3 position, Vector3 direction, Color24 color, float range, Vector2 areaSize, LightPriority priority,
			float power = DefaultPower, float exposure = 0.0f, float radius = 0.0f, bool softFalloff = true)
		{
			return new SceneLight
			{
				Type = LightType.Area,
				Priority = priority,
				Position = position,
				Direction = direction,
				Color = color,
				Range = range,
				SpotCutoff = -1.0f,
				Power = power,
				Exposure = exposure,
				Normalize = false,
				Radius = radius,
				SoftFalloff = softFalloff,
				Softness = 1.0f,
				AreaSize = areaSize,
				CastsShadow = false,
				Enabled = true
			};
		}
	}
}
