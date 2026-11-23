namespace LibRender2
{
	/// <summary>
	/// Global feature flag for GL 4.4 persistent mapped buffers. Low-level buffer wrappers have no
	/// renderer reference, so this is set once from the detected capabilities in BaseRenderer.Initialize().
	/// </summary>
	public static class BufferSupport
	{
		private static bool persistentMapping;

		public static bool PersistentMapping
		{
			get { return persistentMapping; }
		}

		public static void Configure(GlCapabilities capabilities)
		{
			persistentMapping = capabilities != null && capabilities.IsAtLeast(4, 4);
		}
	}
}
