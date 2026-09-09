namespace OpenBveApi
{
	/// <summary>
	/// Single source for AO/post numeric limits.
	/// Used by Options load, effects Sync, and WinForms numeric clamping
	/// so the three copies of ClampDecimal/ClampFloat/SnapScale disappear.
	/// OFF-bypass behaviour is unchanged: only clamping is centralized.
	/// </summary>
	public static class AoLimits
	{
		public static float Clamp(float v, float lo, float hi)
		{
			if (v < lo) return lo;
			if (v > hi) return hi;
			return v;
		}

		public static double Clamp(double v, double lo, double hi)
		{
			if (v < lo) return lo;
			if (v > hi) return hi;
			return v;
		}

		public static int Clamp(int v, int lo, int hi)
		{
			if (v < lo) return lo;
			if (v > hi) return hi;
			return v;
		}

		public static decimal Clamp(decimal v, decimal lo, decimal hi)
		{
			if (v < lo) return lo;
			if (v > hi) return hi;
			return v;
		}

		/// <summary>Snaps a resolution scale to one of 0.25 / 0.5 / 1.0.</summary>
		public static float SnapScale(float s)
		{
			if (s <= 0.375f) return 0.25f;
			if (s <= 0.75f) return 0.5f;
			return 1.0f;
		}

		/// <summary>Snaps a resolution scale to one of 0.25 / 0.5 / 1.0.</summary>
		public static double SnapScale(double s)
		{
			if (s <= 0.375) return 0.25;
			if (s <= 0.75) return 0.5;
			return 1.0;
		}
	}
}
