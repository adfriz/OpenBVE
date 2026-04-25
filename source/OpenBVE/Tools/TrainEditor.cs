using System;
using ImGuiNET;

namespace OpenBve.Tools
{
	public static class TrainEditor
	{
		private static bool isOpen = false;
		public static bool IsOpen => isOpen;

		public static void Show()
		{
			isOpen = true;
		}

		public static void Update()
		{
			if (!isOpen) return;

			if (ImGui.Begin("Train Editor", ref isOpen, ImGuiWindowFlags.MenuBar))
			{
				if (ImGui.BeginMenuBar())
				{
					if (ImGui.BeginMenu("File"))
					{
						if (ImGui.MenuItem("New Train")) { }
						if (ImGui.MenuItem("Open Train...")) { }
						ImGui.Separator();
						if (ImGui.MenuItem("Save")) { }
						ImGui.EndMenu();
					}
					ImGui.EndMenuBar();
				}

				if (ImGui.BeginTabBar("TrainTabs"))
				{
					if (ImGui.BeginTabItem("General"))
					{
						ImGui.Text("Train Information");
						static int pNotches = 5;
						static int bNotches = 8;
						static float mass = 40000.0f;
						ImGui.InputInt("Power Notches", ref pNotches);
						ImGui.InputInt("Brake Notches", ref bNotches);
						ImGui.InputFloat("Mass (kg)", ref mass);
						if (ImGui.Button("Apply Changes"))
						{
							// Logic to update TrainManager
						}
						ImGui.EndTabItem();
					}
					if (ImGui.BeginTabItem("Physics"))
					{
						ImGui.Text("Performance & Braking");
						ImGui.EndTabItem();
					}
					if (ImGui.BeginTabItem("Sound"))
					{
						ImGui.Text("Sound Mapping");
						ImGui.EndTabItem();
					}
					ImGui.EndTabBar();
				}
				
				ImGui.End();
			}
		}
	}
}
