using Raylib_cs;
using ImGuiNET;
using System.Numerics;
using OpenBve.Tools;

namespace OpenBve
{
	internal static class GUIManager
	{
		public static bool IsInLauncher = true;

		public static void Initialize()
		{
			rlImGui.Setup(true);
		}

		public static void Draw()
		{
			rlImGui.Begin();
			
			if (IsInLauncher)
			{
				Launcher.Draw();
			}
			else if (ImGui.BeginMainMenuBar())
			{
				if (ImGui.BeginMenu("File"))
				{
					if (ImGui.MenuItem("Exit"))
					{
						MainLoop.Quit = QuitMode.QuitProgram;
					}
					ImGui.EndMenu();
				}

				if (ImGui.BeginMenu("Tools"))
				{
					if (ImGui.MenuItem("Object Viewer", "", ObjectViewer.IsOpen))
					{
						ObjectViewer.Show();
					}
					if (ImGui.MenuItem("Train Editor", "", TrainEditor.IsOpen))
					{
						TrainEditor.Show();
					}
					ImGui.EndMenu();
				}
				ImGui.EndMainMenuBar();
			}

			// Update Tools
			ObjectViewer.Update();
			TrainEditor.Update();

			rlImGui.End();
		}

		public static void Deinitialize()
		{
			rlImGui.Shutdown();
		}
	}
}
