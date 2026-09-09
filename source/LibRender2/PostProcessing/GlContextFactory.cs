using System;
using OpenBveApi.Hosts;

namespace LibRender2.PostProcessing
{
	/// <summary>Shared GL-context creation pattern: try 4.3, fall back to 3.3 (Win/Linux).</summary>
	public static class GlContextFactory
	{
		/// <summary>True when a forward-compatible 3.3 context is required (macOS or forced).</summary>
		public static bool WantForwardCompatible(HostPlatform platform, bool forceForwardsCompatible)
		{
			return forceForwardsCompatible || (platform == HostPlatform.AppleOSX && IntPtr.Size != 4);
		}

		/// <summary>Creates T with GL 4.3, falling back to 3.3 on failure.</summary>
		public static T CreateWithFallback<T>(Func<int, int, T> create)
		{
			try
			{
				return create(4, 3);
			}
			catch
			{
				return create(3, 3);
			}
		}
	}
}
