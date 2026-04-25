using Raylib_cs;
using ImGuiNET;
using System.Numerics;

namespace OpenBve
{
	internal static class GUIManager
	{
		public static void Initialize()
		{
			rlImGui.Setup(true);
		}

		public static void Draw()
		{
			rlImGui.Begin();
			
			if (ImGui.BeginMainMenuBar())
			{
				if (ImGui.BeginMenu("File"))
				{
					if (ImGui.MenuItem("Exit"))
					{
						MainLoop.Quit = QuitMode.QuitProgram;
					}
					ImGui.EndMenu();
				}
				ImGui.EndMainMenuBar();
			}

			rlImGui.End();
		}

		public static void Deinitialize()
		{
			rlImGui.Shutdown();
		}
	}
}
