using System;
using System.IO;
using ImGuiNET;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.World;

namespace OpenBve.Tools
{
	internal static class ObjectViewer
	{
		public static bool IsOpen = false;
		public static bool Visible = false;
		private static string objectPath = "";
		private static UnifiedObject currentObject = null;

		public static void Show()
		{
			Visible = true;
			IsOpen = true;
		}

		public static void Update()
		{
			if (!Visible) return;

			if (ImGui.Begin("Object Viewer", ref Visible))
			{
				ImGui.InputText("Object Path", ref objectPath, 1024);
				if (ImGui.Button("Load"))
				{
					LoadObject(objectPath);
				}

				if (currentObject != null)
				{
					ImGui.Separator();
					ImGui.Text("Object Information:");
					if (currentObject is StaticObject so)
					{
						ImGui.Text("Faces: " + so.Mesh.Faces.Length);
						ImGui.Text("Vertices: " + so.Mesh.Vertices.Length);
						ImGui.Text("Materials: " + so.Mesh.Materials.Length);
					}
				}
			}
			ImGui.End();

			if (!Visible)
			{
				IsOpen = false;
			}
		}

		private static void LoadObject(string path)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				return;
			}

			if (Program.CurrentHost.LoadObject(path, System.Text.Encoding.UTF8, out var obj))
			{
				currentObject = obj;
				Program.Renderer.Reset();
				
				if (obj is StaticObject so)
				{
					Program.Renderer.CreateStaticObject(so, Vector3.Zero, Transformation.NullTransformation, Transformation.NullTransformation, 0.0, -1000.0, 1000.0, 0.0, false);
				}
				else if (obj is AnimatedObjectCollection aoc)
				{
					// For animated objects, we need to handle them differently
					// For now just show the first state if possible
					foreach (var ao in aoc.Objects)
					{
						if (ao.States.Length > 0 && ao.States[0].Object != null)
						{
							Program.Renderer.CreateStaticObject(ao.States[0].Object, Vector3.Zero, Transformation.NullTransformation, Transformation.NullTransformation, 0.0, -1000.0, 1000.0, 0.0, false);
						}
					}
				}
				
				// Reset camera to see the object
				Program.Renderer.Camera.Alignment.Position = new Vector3(0, 0, -5);
				Program.Renderer.Camera.Alignment.Yaw = 0;
				Program.Renderer.Camera.Alignment.Pitch = 0;
			}
		}
	}
}
