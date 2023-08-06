using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LibRender2.Menu;
using LibRender2.Screens;
using OpenBveApi.Graphics;
using OpenTK;

namespace OpenBve
{
	public sealed partial class Menu
	{
		private class MenuOption : MenuEntry
		{
			private readonly OptionType Type;

			/// <summary>Holds the entries for all options</summary>
			private readonly object[] Entries;

			/// <summary>Gets the current option</summary>
			internal object CurrentOption => Entries[CurrentlySelectedOption];

			private int CurrentlySelectedOption;

			internal MenuOption(OptionType type, string text, object[] entries)
			{
				Type = type;
				Text = text;
				Entries = entries;
				switch (type)
				{
					case OptionType.ScreenResolution:
						if (entries is ScreenResolution[] castEntries)
						{
							for (int i = 0; i < castEntries.Length; i++)
							{
								if (castEntries[i].Width == Program.Renderer.Screen.Width && castEntries[i].Height == Program.Renderer.Screen.Height)
								{
									CurrentlySelectedOption = i;
									return;
								}
							}
						}
						else
						{
							throw new InvalidDataException("Entries must be a list of screen resolutions");
						}

						break;
					case OptionType.FullScreen:
						CurrentlySelectedOption = Interface.CurrentOptions.FullscreenMode ? 0 : 1;
						return;
					case OptionType.Interpolation:
						CurrentlySelectedOption = (int)Interface.CurrentOptions.Interpolation;
						return;
					case OptionType.AnisotropicLevel:
						for (int i = 0; i < Entries.Length; i++)
						{
							int level = int.Parse(entries[i] as string ?? string.Empty, NumberStyles.Integer);
							if (level == Interface.CurrentOptions.AnisotropicFilteringLevel)
							{
								CurrentlySelectedOption = i;
								return;
							}
						}
						break;
					case OptionType.AntialiasingLevel:
						for (int i = 0; i < Entries.Length; i++)
						{
							int level = int.Parse(entries[i] as string ?? string.Empty, NumberStyles.Integer);
							if (level == Interface.CurrentOptions.AntiAliasingLevel)
							{
								CurrentlySelectedOption = i;
								return;
							}
						}
						break;
					case OptionType.ViewingDistance:
						switch (Interface.CurrentOptions.ViewingDistance)
						{
							case 400:
								CurrentlySelectedOption = 0;
								break;
							case 600:
								CurrentlySelectedOption = 1;
								break;
							case 800:
								CurrentlySelectedOption = 2;
								break;
							case 1000:
								CurrentlySelectedOption = 3;
								break;
							case 1500:
								CurrentlySelectedOption = 4;
								break;
							case 2000:
								CurrentlySelectedOption = 5;
								break;
						}
						return;
				}
				CurrentlySelectedOption = 0;
			}

			/// <summary>Flips to the next option</summary>
			internal void Flip()
			{
				if (CurrentlySelectedOption < Entries.Length - 1)
				{
					CurrentlySelectedOption++;
				}
				else
				{
					CurrentlySelectedOption = 0;
				}

				//Apply
				switch (Type)
				{
					case OptionType.ScreenResolution:
						if (!(CurrentOption is ScreenResolution res))
						{
							return;
						}
						Program.Renderer.Screen.Width = (int)(res.Width * DisplayDevice.Default.ScaleFactor.X);
						Program.Renderer.Screen.Height = (int)(res.Height * DisplayDevice.Default.ScaleFactor.Y);
						Program.currentGameWindow.Width = (int)(res.Width * DisplayDevice.Default.ScaleFactor.X);
						Program.currentGameWindow.Height = (int)(res.Height * DisplayDevice.Default.ScaleFactor.Y);
						if (Interface.CurrentOptions.FullscreenMode)
						{
							IList<DisplayResolution> resolutions = DisplayDevice.Default.AvailableResolutions;
							foreach (DisplayResolution currentResolution in resolutions)
							{
								//Test resolution
								if (currentResolution.Width == Program.Renderer.Screen.Width / DisplayDevice.Default.ScaleFactor.X &&
								    currentResolution.Height == Program.Renderer.Screen.Height / DisplayDevice.Default.ScaleFactor.Y)
								{
									try
									{
										//HACK: some resolutions will result in openBVE not appearing on screen in full screen, so restore resolution then change resolution
										DisplayDevice.Default.RestoreResolution();
										DisplayDevice.Default.ChangeResolution(currentResolution);
										Program.currentGameWindow.WindowState = WindowState.Fullscreen;
										Program.currentGameWindow.X = 0;
										Program.currentGameWindow.Y = 0;
										Program.currentGameWindow.Width = (int)(currentResolution.Width * DisplayDevice.Default.ScaleFactor.X);
										Program.currentGameWindow.Height = (int)(currentResolution.Height * DisplayDevice.Default.ScaleFactor.Y);
										Program.Renderer.Screen.Width = Program.currentGameWindow.Width;
										Program.Renderer.Screen.Height = Program.currentGameWindow.Height;
										return;
									}
									catch
									{
										//refresh rate wrong? - Keep trying in case a different refresh rate works OK
									}
								}
							}
						}
						break;
					case OptionType.FullScreen:
						Interface.CurrentOptions.FullscreenMode = !Interface.CurrentOptions.FullscreenMode;
						if (Program.currentGameWindow.WindowState == WindowState.Fullscreen)
						{
							Program.currentGameWindow.WindowState = WindowState.Normal;
							DisplayDevice.Default.RestoreResolution();
						}
						else
						{
							IList<DisplayResolution> resolutions = DisplayDevice.Default.AvailableResolutions;
							foreach (DisplayResolution currentResolution in resolutions)
							{
								//Test resolution
								if (currentResolution.Width == Program.Renderer.Screen.Width / DisplayDevice.Default.ScaleFactor.X &&
									currentResolution.Height == Program.Renderer.Screen.Height / DisplayDevice.Default.ScaleFactor.Y)
								{
									try
									{
										//HACK: some resolutions will result in openBVE not appearing on screen in full screen, so restore resolution then change resolution
										DisplayDevice.Default.RestoreResolution();
										DisplayDevice.Default.ChangeResolution(currentResolution);
										Program.currentGameWindow.WindowState = WindowState.Fullscreen;
										Program.currentGameWindow.X = 0;
										Program.currentGameWindow.Y = 0;
										Program.currentGameWindow.Width = (int)(currentResolution.Width * DisplayDevice.Default.ScaleFactor.X);
										Program.currentGameWindow.Height = (int)(currentResolution.Height * DisplayDevice.Default.ScaleFactor.Y);
										Program.Renderer.Screen.Width = Program.currentGameWindow.Width;
										Program.Renderer.Screen.Height = Program.currentGameWindow.Height;
										return;
									}
									catch
									{
										//refresh rate wrong? - Keep trying in case a different refresh rate works OK
									}
								}
							}
						}
						break;
					case OptionType.Interpolation:
						Interface.CurrentOptions.Interpolation = (InterpolationMode)CurrentlySelectedOption;
						break;
					//HACK: We can't store plain ints due to to boxing, so store strings and parse instead
					case OptionType.AnisotropicLevel:
						Interface.CurrentOptions.AnisotropicFilteringLevel = int.Parse((string)CurrentOption, NumberStyles.Integer);
						break;
					case OptionType.AntialiasingLevel:
						Interface.CurrentOptions.AntiAliasingLevel = int.Parse((string)CurrentOption, NumberStyles.Integer);
						break;
					case OptionType.ViewingDistance:
						Interface.CurrentOptions.ViewingDistance = int.Parse((string)CurrentOption, NumberStyles.Integer);
						break;
					
				}
				
			}
		}

	}
}
