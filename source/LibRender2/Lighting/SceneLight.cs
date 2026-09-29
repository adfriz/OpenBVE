using System;
using OpenBveApi.Colors;
using OpenBveApi.Math;

namespace LibRender2.Lightings
{
	/// <summary>What kind of light this is. Mirrors the [Light] Type in PR #1328.</summary>
	public enum LightType
	{
		Directional,
		Point,
		Spot,
		Area
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

	/// <summary>One light in the scene, in world space. Shape follows PR #1328 ([Light] blocks).</summary>
	public struct SceneLight
	{
		/// <summary>Physical output in Watts. Default is 4*pi, so a normalized light has intensity 1.</summary>
		public const float DefaultPower = 12.5663706f;

		public LightType Type;
		public LightPriority Priority;
		/// <summary>Where the light sits (sun: where it shines from).</summary>
		public Vector3 Position;
		/// <summary>Spot beam / area rect normal.</summary>
		public Vector3 Direction;
		public Color24 Color;
		public Color24 Ambient;
		/// <summary>How far the light reaches, in meters.</summary>
		public float Range;
		/// <summary>Spot cone tightness, as cos(half-angle).</summary>
		public float SpotCutoff;
		/// <summary>Physical output in Watts. Applied as Power * exp2(Exposure).</summary>
		public float Power;
		/// <summary>Exposure multiplier, applied as exp2(Exposure). 0 means x1.</summary>
		public float Exposure;
		/// <summary>Divide spot power by its solid angle. Points always divide by 4*pi.</summary>
		public bool Normalize;
		/// <summary>Physical source size in meters. Softens the d-squared falloff.</summary>
		public float Radius;
		/// <summary>Smooth fade near the range limit.</summary>
		public bool SoftFalloff;
		/// <summary>Spot edge penumbra, 0 to 1.</summary>
		public float Softness;
		/// <summary>Rect size for area lights.</summary>
		public Vector2 AreaSize;
		/// <summary>Debug helper cone. Never affects lighting.</summary>
		public bool ShowCone;
		public bool CastsShadow;
		/// <summary>Lights that are off get skipped. Factories switch this on.</summary>
		public bool Enabled;

		/// <summary>Shader-ready intensity. Mirrors the PR #1328 fragment math.</summary>
		public float ShadingIntensity()
		{
			float intensity = Power * (float)Math.Pow(2.0, Exposure);
			if (Type == LightType.Spot && Normalize)
			{
				float solidAngle = 6.2831853f * (1.0f - SpotCutoff);
				return intensity / Math.Max(solidAngle, 0.0001f);
			}
			if (Type == LightType.Point)
			{
				return intensity / 12.5663706f;
			}
			return intensity;
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

		// A plain point light, no shadows.
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

		// A spot light. Pass the full cone width in degrees.
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

		// A rectangular area light. Direction is the rect normal.
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
