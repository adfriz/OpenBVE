using System.Collections.Generic;
using System.Linq;
using LibRender2.Objects;
using Raylib_cs;

namespace LibRender2.Passes
{
	public class OverlayPass : IRenderPass
	{
		public string Name => "Overlay";
		public bool Enabled { get; set; } = true;

		public void Render(BaseRenderer renderer, double timeElapsed, double realTimeElapsed)
		{
			renderer.ResetOpenGlState();
			lock (renderer.VisibleObjects.LockObject)
			{
				// Render cab interior / HUD objects
				foreach (var face in renderer.VisibleObjects.OverlayOpaqueFaces)
				{
					face.Draw();
				}
				// Sorted alpha polygons still need a list copy or locking during iteration
				foreach (var face in renderer.VisibleObjects.GetSortedPolygons(true))
				{
					face.Draw();
				}
			}
		}
	}
}
