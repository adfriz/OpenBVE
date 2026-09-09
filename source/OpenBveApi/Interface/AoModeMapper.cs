using System;

namespace OpenBveApi.Interface
{
	/// <summary>
	/// Single mapper between ComboBox SelectedIndex and AmbientOcclusionMode.
	/// Replaces the triplicated switch blocks in WinForms + GL menu code.
	/// Invalid indices fall back to Off / 0 so the chain stays in bypass.
	/// </summary>
	public static class AoModeMapper
	{
		public static AmbientOcclusionMode FromSelectedIndex(int index)
		{
			try
			{
				if (Enum.IsDefined(typeof(AmbientOcclusionMode), index))
				{
					return (AmbientOcclusionMode)index;
				}
			}
			catch
			{
				// ignored: fall through to Off
			}
			return AmbientOcclusionMode.Off;
		}

		public static int ToSelectedIndex(AmbientOcclusionMode mode)
		{
			try
			{
				int i = (int)mode;
				if (Enum.IsDefined(typeof(AmbientOcclusionMode), i))
				{
					return i;
				}
			}
			catch
			{
				// ignored: fall through to 0
			}
			return 0;
		}
	}
}
