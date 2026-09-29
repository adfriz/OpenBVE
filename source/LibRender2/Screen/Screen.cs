using System.Collections.Generic;
using OpenBveApi.Hosts;
using OpenTK;

namespace LibRender2.Screens
{
	/// <summary>A screen resolution</summary>
	public class ScreenResolution
	{
		/// <summary>The width</summary>
		public readonly int Width;
		/// <summary>The height</summary>
		public readonly int Height;

		public ScreenResolution(int width, int height)
		{
			Width = width;
			Height = height;
		}

		public override string ToString()
		{
			return Width + " x " + Height;
		}
	}

	/// <summary>Renderer overlays.</summary>
	public enum OutputMode
	{
		/// <summary>Show active overlays.</summary>
		Default = 0,
		/// <summary>Show debug overlay (F10).</summary>
		Debug = 1,
		/// <summary>Show ATS debug overlay (F10).</summary>
		DebugATS = 2,
		/// <summary>Hide all overlays.</summary>
		None = 3
	}

	/// <summary>What the game is showing right now.</summary>
	public enum InterfaceType
	{
		/// <summary>Loading screen.</summary>
		LoadScreen,
		/// <summary>Normal gameplay.</summary>
		Normal,
		/// <summary>Paused.</summary>
		Pause,
		/// <summary>In a menu.</summary>
		Menu,
		/// <summary>OpenGL main menu.</summary>
		GLMainMenu,
		/// <summary>Switch change map.</summary>
		SwitchChangeMap
	}

	public class Screen
	{
		/// <summary>Current width.</summary>
		public int Width = 0;
		/// <summary>Current height.</summary>
		public int Height = 0;
		/// <summary>Width / height.</summary>
		public double AspectRatio;
		/// <summary>Fullscreen on/off.</summary>
		public bool Fullscreen = false;
		/// <summary>Minimized or not.</summary>
		public bool Minimized = false;
		/// <summary>Resolutions this display supports.</summary>
		public readonly List<ScreenResolution> AvailableResolutions;

		internal Screen(BaseRenderer renderer)
		{
			// TrainEditor uses a GLControl: resolution lookup crashes the Linux SDL2 backend,
			// and fullscreen doesn't matter there, so skip it.
			if (renderer.currentHost.Application != HostApplication.TrainEditor2 && renderer.currentHost.Application != HostApplication.TrainEditor)
			{
				// Collect each unique W x H (skip refresh-rate variants).
				AvailableResolutions = new List<ScreenResolution>();
				ScreenResolution lastResolution = new ScreenResolution(0, 0);
				for (int i = 0; i < DisplayDevice.Default.AvailableResolutions.Count; i++)
				{
					if (DisplayDevice.Default.AvailableResolutions[i].Width != lastResolution.Width || DisplayDevice.Default.AvailableResolutions[i].Height != lastResolution.Height)
					{
						lastResolution = new ScreenResolution(DisplayDevice.Default.AvailableResolutions[i].Width, DisplayDevice.Default.AvailableResolutions[i].Height);
						AvailableResolutions.Add(lastResolution);
					}
				}
			}
			else
			{
				// TrainEditor default size.
				Width = 568;
				Height = 593;
			}
			
		}
	}
}
