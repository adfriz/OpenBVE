using System;
using System.IO;
using System.Linq;
using ImGuiNET;
using OpenBveApi.Hosts;
using OpenBveApi.Interface;
using OpenBveApi.Math;

namespace OpenBve.Tools
{
	internal static class Launcher
	{
		private static string routeFile = "";
		private static string trainFolder = "";
		private static string[] routeFiles = new string[0];
		private static string[] trainFolders = new string[0];
		private static int selectedRoute = -1;
		private static int selectedTrain = -1;

		private static string currentRouteDir = "";
		private static string currentTrainDir = "";

		public static void Draw()
		{
			if (currentRouteDir == "")
			{
				currentRouteDir = Program.FileSystem.InitialRouteFolder;
				RefreshRoutes();
			}
			if (currentTrainDir == "")
			{
				currentTrainDir = Program.FileSystem.InitialTrainFolder;
				RefreshTrains();
			}

			ImGui.Begin("OpenBVE Launcher", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);
			ImGui.SetWindowSize(new System.Numerics.Vector2(ImGui.GetIO().DisplaySize.X, ImGui.GetIO().DisplaySize.Y));
			ImGui.SetWindowPos(new System.Numerics.Vector2(0, 0));

			if (ImGui.BeginTabBar("LauncherTabs"))
			{
				if (ImGui.BeginTabItem("Start Game"))
				{
					DrawStartGame();
					ImGui.EndTabItem();
				}
				if (ImGui.BeginTabItem("Options"))
				{
					DrawOptions();
					ImGui.EndTabItem();
				}
				if (ImGui.BeginTabItem("Tools"))
				{
					DrawTools();
					ImGui.EndTabItem();
				}
				ImGui.EndTabBar();
			}

			ImGui.End();
		}

		private static void DrawStartGame()
		{
			ImGui.Text("Select Route:");
			if (ImGui.Button("Browse Route..."))
			{
				// TODO: Proper file dialog
			}
			
			ImGui.Text("Current Folder: " + currentRouteDir);
			if (ImGui.ListBox("##Routes", ref selectedRoute, routeFiles, routeFiles.Length))
			{
				if (selectedRoute >= 0)
				{
					string path = Path.Combine(currentRouteDir, routeFiles[selectedRoute]);
					if (Directory.Exists(path))
					{
						currentRouteDir = path;
						RefreshRoutes();
						selectedRoute = -1;
					}
					else
					{
						routeFile = path;
					}
				}
			}
			if (ImGui.Button("Back ##Route"))
			{
				currentRouteDir = Path.GetDirectoryName(currentRouteDir) ?? currentRouteDir;
				RefreshRoutes();
			}

			ImGui.Separator();

			ImGui.Text("Select Train:");
			ImGui.Text("Current Folder: " + currentTrainDir);
			if (ImGui.ListBox("##Trains", ref selectedTrain, trainFolders, trainFolders.Length))
			{
				if (selectedTrain >= 0)
				{
					string path = Path.Combine(currentTrainDir, trainFolders[selectedTrain]);
					if (Directory.Exists(path))
					{
						currentTrainDir = path;
						RefreshTrains();
						selectedTrain = -1;
					}
					else
					{
						trainFolder = path;
					}
				}
			}
			if (ImGui.Button("Back ##Train"))
			{
				currentTrainDir = Path.GetDirectoryName(currentTrainDir) ?? currentTrainDir;
				RefreshTrains();
			}

			ImGui.Separator();

			ImGui.Text("Selected Route: " + (routeFile == "" ? "(None)" : Path.GetFileName(routeFile)));
			ImGui.Text("Selected Train: " + (trainFolder == "" ? "(None)" : Path.GetFileName(trainFolder)));

			if (routeFile != "" && trainFolder != "" && ImGui.Button("START SIMULATION", new System.Numerics.Vector2(200, 50)))
			{
				LaunchParameters result = new LaunchParameters
				{
					RouteFile = routeFile,
					TrainFolder = trainFolder,
					Start = true
				};
				// We need to trigger the main loop transition
				MainLoop.currentResult = result;
				// In a real implementation, we'd close the launcher and start the sim
			}
		}

		private static void DrawOptions()
		{
			ImGui.Text("Basic Options");
			bool vsync = Interface.CurrentOptions.VerticalSync;
			if (ImGui.Checkbox("Vertical Sync", ref vsync))
			{
				Interface.CurrentOptions.VerticalSync = vsync;
			}
			
			int width = Interface.CurrentOptions.WindowWidth;
			int height = Interface.CurrentOptions.WindowHeight;
			ImGui.InputInt("Width", ref width);
			ImGui.InputInt("Height", ref height);
			Interface.CurrentOptions.WindowWidth = width;
			Interface.CurrentOptions.WindowHeight = height;

			if (ImGui.Button("Save Options"))
			{
				Interface.CurrentOptions.Save(Path.Combine(Program.FileSystem.SettingsFolder, "1.5.0/options.cfg"));
			}
		}

		private static void DrawTools()
		{
			if (ImGui.Button("Object Viewer", new System.Numerics.Vector2(200, 40)))
			{
				ObjectViewer.Visible = true;
			}
			if (ImGui.Button("Train Editor", new System.Numerics.Vector2(200, 40)))
			{
				TrainEditor.Visible = true;
			}
		}

		private static void RefreshRoutes()
		{
			try
			{
				var dirs = Directory.GetDirectories(currentRouteDir).Select(Path.GetFileName).ToArray();
				var files = Directory.GetFiles(currentRouteDir, "*.csv").Concat(Directory.GetFiles(currentRouteDir, "*.rw")).Select(Path.GetFileName).ToArray();
				routeFiles = dirs.Concat(files).ToArray();
			}
			catch { routeFiles = new string[0]; }
		}

		private static void RefreshTrains()
		{
			try
			{
				var dirs = Directory.GetDirectories(currentTrainDir).Select(Path.GetFileName).ToArray();
				trainFolders = dirs.ToArray();
			}
			catch { trainFolders = new string[0]; }
		}
	}
}
