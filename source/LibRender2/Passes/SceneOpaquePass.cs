using System.Collections.Generic;
using System.Linq;
using LibRender2.Objects;

namespace LibRender2.Passes
{
	public class SceneOpaquePass : IRenderPass
	{
		public string Name => "SceneOpaque";
		public bool Enabled { get; set; } = true;

		public void Render(BaseRenderer renderer, double timeElapsed, double realTimeElapsed)
		{
			renderer.ResetOpenGlState();
			lock (renderer.VisibleObjects.LockObject)
			{
				foreach (FaceState face in renderer.VisibleObjects.OpaqueFaces)
				{
					face.Draw();
				}
			}
		}
	}
}
