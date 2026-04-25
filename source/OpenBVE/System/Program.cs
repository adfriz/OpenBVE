using System;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenBve.Graphics;
using OpenBve.Input;
using OpenBveApi;
using OpenBveApi.FileSystem;
using OpenBveApi.Hosts;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using RouteManager2;
using System.Diagnostics;
using Control = OpenBveApi.Interface.Control;
using System.IO;
using Raylib_cs;

namespace OpenBve {
	internal static partial class Program {
		internal static ImageFileMachine CurrentCPUArchitecture;
		internal static Host CurrentHost;
		internal static FileSystem FileSystem;
		internal static string RestartArguments;
		internal static readonly Random RandomNumberGenerator = new Random();
		internal static JoystickManager Joysticks;
		internal static NewRenderer Renderer;
		internal static Sounds Sounds;
		internal static CurrentRoute CurrentRoute;
		internal static TrainManager TrainManager;

		[STAThread]
		private static void Main(string[] args) {
			CurrentHost = new Host();
			try {
				FileSystem = FileSystem.FromCommandLineArgs(args, CurrentHost);
				FileSystem.CreateFileSystem();
				Interface.LoadOptions();
			} catch { }

			AppDomain.CurrentDomain.UnhandledException += (CrashHandler.CurrentDomain_UnhandledException);
			typeof(object).Module.GetPEKind(out PortableExecutableKinds _, out CurrentCPUArchitecture);
			
			Joysticks = new JoystickManager32();

			Renderer = new NewRenderer(CurrentHost, Interface.CurrentOptions, FileSystem);
			Sounds = new Sounds(CurrentHost);
			CurrentRoute = new CurrentRoute(CurrentHost, Renderer);
			TrainManager = new TrainManager(CurrentHost, Renderer, Interface.CurrentOptions, FileSystem);
			
			string folder = Program.FileSystem.GetDataFolder("Languages");
			Translations.LoadLanguageFiles(folder);
			
			folder = Program.FileSystem.GetDataFolder("Cursors");
			LibRender2.AvailableCursors.LoadCursorImages(Program.Renderer, folder);
			
			Interface.LoadControls(null, out Interface.CurrentControls);
			folder = Program.FileSystem.GetDataFolder("Controls");
			string file = Path.Combine(folder, "Default keyboard assignment.controls");
			Interface.LoadControls(file, out Control[] controls);
			Interface.AddControls(ref Interface.CurrentControls, controls);
			
			InputDevicePlugin.LoadPlugins(Program.FileSystem);
			
			LaunchParameters result = CommandLine.ParseArguments(args);
			
			// Simple check for route/train
			if (result.RouteFile == null || result.TrainFolder == null) {
				Console.WriteLine("Route or Train not specified. Starting in Launcher mode.");
				WindowManager.Initialize(Interface.CurrentOptions.WindowWidth, Interface.CurrentOptions.WindowHeight, "OpenBVE", Interface.CurrentOptions.FullscreenMode);
				GUIManager.Initialize();
			} else {
				result.Start = true;
				Translations.SetInGameLanguage(Translations.CurrentLanguageCode);
				WindowManager.Initialize(Interface.CurrentOptions.WindowWidth, Interface.CurrentOptions.WindowHeight, "OpenBVE", Interface.CurrentOptions.FullscreenMode);
				GUIManager.Initialize();
			}

			if (result.Start) {
				GUIManager.IsInLauncher = false;
				if (Initialize()) {
					MainLoop.StartLoopEx(result);
				}
			} else {
				// Launcher Loop
				while (!WindowManager.ShouldClose) {
					Raylib.BeginDrawing();
					Raylib.ClearBackground(Color.DARKGRAY);
					GUIManager.Draw();
					Raylib.EndDrawing();

					if (MainLoop.currentResult.Start) {
						GUIManager.IsInLauncher = false;
						if (Initialize()) {
							MainLoop.StartLoopEx(MainLoop.currentResult);
						}
						break;
					}
				}
			}

			GUIManager.Deinitialize();
			Deinitialize();
			WindowManager.Deinitialize();
		}

		private static bool Initialize() {
			if (!CurrentHost.LoadPlugins(FileSystem, Interface.CurrentOptions, out string error, TrainManager, Renderer)) {
				Console.WriteLine("Plugin error: " + error);
				return false;
			}
			
			Joysticks.RefreshJoysticks();
			Renderer.Camera.VerticalViewingAngle = 45.0.ToRadians();
			Renderer.Camera.HorizontalViewingAngle = 2.0 * Math.Atan(Math.Tan(0.5 * Renderer.Camera.VerticalViewingAngle) * Renderer.Screen.AspectRatio);
			Renderer.Camera.OriginalVerticalViewingAngle = Renderer.Camera.VerticalViewingAngle;
			Renderer.Camera.ExtraViewingDistance = 50.0;
			Renderer.Camera.ForwardViewingDistance = Interface.CurrentOptions.ViewingDistance;
			Renderer.Camera.BackwardViewingDistance = 0.0;
			Program.CurrentRoute.CurrentBackground.BackgroundImageDistance = Interface.CurrentOptions.ViewingDistance;
			
			string programVersion = "v" + Assembly.GetExecutingAssembly().GetName().Version.ToString();
			FileSystem.ClearLogFile(programVersion);
			return true;
		}

		private static void Deinitialize() {
			Program.CurrentHost.UnloadPlugins(out _);
			Sounds.DeInitialize();
			Renderer.DeInitialize();
		}

		internal static void ShowMessageBox(string messageText, string captionText) {
			Console.WriteLine("[" + captionText + "] " + messageText);
		}
	}
}
