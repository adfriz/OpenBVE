using System;
using OpenBveApi.Textures;

namespace LibRender2.Textures
{
	/// <summary>Coarse runtime policy for distance-tiered texture streaming (Currently only for JPG/PNG).</summary>
	/// <remarks>Deliberately coarse: 3 tiers driven by track distance, no per-pixel feedback.
	/// Small, animated and UI textures are protected and always upload full-size.</remarks>
	public static class TexturePolicy
	{
		/// <summary>Textures with both dimensions below this are protected (icons, text, UI).</summary>
		public const int MinStreamingDimension = 512;
		/// <summary>Highest (smallest) tier: 2 = quarter resolution.</summary>
		public const int MaxTier = 2;
		/// <summary>Minimum ticks between downgrades of the same texture (anti-flapping).</summary>
		public const int DowngradeCooldownTicks = 5000;
		/// <summary>Maximum tier-change unloads applied per streaming pass.</summary>
		public const int MaxTierSwapsPerPass = 4;
		/// <summary>Streaming pass interval in ticks.</summary>
		public const int TierPassIntervalTicks = 500;

		/// <summary>Whether the texture must always upload full-size.</summary>
		public static bool IsProtected(Texture texture)
		{
			if (texture == null || texture.MultipleFrames)
			{
				return true;
			}
			return texture.Width < MinStreamingDimension && texture.Height < MinStreamingDimension;
		}

		/// <summary>Maps a camera distance in meters to a mip tier.</summary>
		public static int TierForDistance(double distance, double viewingDistance)
		{
			if (viewingDistance <= 0)
			{
				viewingDistance = 600.0;
			}
			if (distance < viewingDistance * 0.25)
			{
				return 0;
			}
			if (distance < viewingDistance * 0.66)
			{
				return 1;
			}
			return MaxTier;
		}

		/// <summary>Coarse track-distance from the camera to an object interval.</summary>
		public static double DistanceToCamera(double startDistance, double endDistance, double cameraTrackPosition)
		{
			if (cameraTrackPosition < startDistance)
			{
				return startDistance - cameraTrackPosition;
			}
			if (cameraTrackPosition > endDistance)
			{
				return cameraTrackPosition - endDistance;
			}
			return 0.0;
		}

		/// <summary>Computes resident dimensions for a tier (halving per level, min 1px).</summary>
		public static void TierDimensions(int width, int height, int tier, out int tierWidth, out int tierHeight)
		{
			if (tier < 0)
			{
				tier = 0;
			}
			tierWidth = Math.Max(1, width >> tier);
			tierHeight = Math.Max(1, height >> tier);
		}

		/// <summary>Resolves the configured budget (0 = automatic by process bitness).</summary>
		public static int ResolveBudgetMB(int configuredMB)
		{
			if (configuredMB > 0)
			{
				return configuredMB;
			}
			return IntPtr.Size == 8 ? 1024 : 512;
		}
	}
}
