using System.Collections.Generic;

namespace LibRender2
{
	public class RenderPipeline
	{
		private readonly List<IRenderPass> passes = new List<IRenderPass>();
		private readonly BaseRenderer renderer;

		public RenderPipeline(BaseRenderer renderer)
		{
			this.renderer = renderer;
		}

		public void AddPass(IRenderPass pass)
		{
			passes.Add(pass);
		}

		public void Execute(double timeElapsed, double realTimeElapsed)
		{
			foreach (var pass in passes)
			{
				if (pass.Enabled)
				{
					pass.Render(renderer, timeElapsed, realTimeElapsed);
				}
			}
		}
	}
}
