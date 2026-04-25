using System.Collections.Generic;
using LibRender2.Objects;
using OpenBveApi.Interface;
using Raylib_cs;

namespace LibRender2.Passes
{
	public class AlphaPass : IRenderPass
	{
		public string Name => "Alpha";
		public bool Enabled { get; set; } = true;

		public void Render(BaseRenderer renderer, double timeElapsed, double realTimeElapsed)
		{
			List<FaceState> alphaFaces;
			lock (renderer.VisibleObjects.LockObject)
			{
				alphaFaces = renderer.VisibleObjects.GetSortedPolygons();
			}

			renderer.ResetOpenGlState();
			// Simplified alpha rendering for modularity
			// TODO: Add complex blend mode handling from NewRenderer.RenderScene
			foreach (var face in alphaFaces)
			{
				face.Draw();
			}
		}
	}
}
