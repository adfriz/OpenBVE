using System.Collections.Generic;

namespace LibRender2.Menu
{
	/// <summary>
	/// Single helper for the GL options post section.
	/// Replaces the triplicated postItems.Add + supportsCompute gate in the
	/// 3 SingleMenu implementations (OpenBVE + ObjectViewer + RouteViewer).
	///
	/// Granularity note (UI parity):
	/// - Viewer GL menus (ObjectViewer/RouteViewer): master + AO mode only.
	/// - OpenBVE GL menu: master + AO mode + intensity/radius (detailed).
	/// - Full tuning (power/bias/falloff/sharpness/blur/presets) lives in the
	///   WinForms options dialogs and options.cfg, not in the GL menus.
	/// </summary>
	public static class MenuBuilder
	{
		/// <summary>True when the renderer exposes compute AO (false when renderer is null).</summary>
		public static bool SupportsCompute(BaseRenderer renderer)
		{
			try
			{
				if (renderer == null)
				{
					return false;
				}
				return renderer.Capabilities.SupportsCompute;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Appends master + (optionally) AO mode + back entries.
		/// Viewer menus use this (no intensity/radius details).
		/// </summary>
		public static void AddPostItems(List<MenuEntry> items, AbstractMenu menu, string postLabel, string aoModeLabel, string backLabel, bool supportsCompute)
		{
			if (items == null || menu == null)
			{
				return;
			}
			items.Add(new MenuOption(menu, OptionType.PostProcessingEnabled, postLabel, new[] { "true", "false" }));
			if (supportsCompute)
			{
				items.Add(new MenuOption(menu, OptionType.AoMode, aoModeLabel, new[] { "Off", "SAO", "GTAO" }));
			}
			items.Add(new MenuCommand(menu, backLabel, MenuTag.MenuBack, 0));
		}

		/// <summary>
		/// Appends master + AO mode/intensity/radius + back entries.
		/// OpenBVE main menu uses this (detailed AO controls).
		/// Reuses AddPostItems, then inserts intensity/radius before the back entry.
		/// </summary>
		public static void AddPostItemsDetailed(List<MenuEntry> items, AbstractMenu menu, string postLabel, string aoModeLabel, string aoIntensityLabel, string aoRadiusLabel, string backLabel, bool supportsCompute)
		{
			AddPostItems(items, menu, postLabel, aoModeLabel, backLabel, supportsCompute);
			if (items == null || menu == null || !supportsCompute || items.Count == 0)
			{
				return;
			}
			int backIndex = items.Count - 1;
			items.Insert(backIndex, new MenuOption(menu, OptionType.AoIntensity, aoIntensityLabel, new[] { "0.0", "0.5", "1.0", "1.5", "2.0" }));
			items.Insert(backIndex + 1, new MenuOption(menu, OptionType.AoRadius, aoRadiusLabel, new[] { "0.1", "0.5", "1.0", "2.0", "5.0" }));
		}
	}
}
