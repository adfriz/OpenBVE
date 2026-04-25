using Raylib_cs;
using OpenBveApi.Interface;
using OpenBveApi.Hosts;
using System;

namespace OpenBve
{
	internal static class WindowManager
	{
		public static bool ShouldClose => Raylib.WindowShouldClose();

		public static void Initialize(int width, int height, string title, bool fullscreen)
		{
			Raylib.InitWindow(width, height, title);
			Raylib.SetWindowState(ConfigFlags.FLAG_WINDOW_RESIZABLE);
			
			if (fullscreen)
			{
				Raylib.ToggleFullscreen();
			}

			Raylib.SetTargetFPS(60); // Default, can be updated from options
		}

		public static void UpdateOptions(BaseOptions options)
		{
			if (options.VerticalSynchronization)
			{
				Raylib.SetWindowState(ConfigFlags.FLAG_VSYNC_HINT);
			}
			else
			{
				Raylib.ClearWindowState(ConfigFlags.FLAG_VSYNC_HINT);
			}

			Raylib.SetTargetFPS(options.FramerateLimit > 0 ? options.FramerateLimit : 0);
		}

		public static void Deinitialize()
		{
			Raylib.CloseWindow();
		}
	}
}
