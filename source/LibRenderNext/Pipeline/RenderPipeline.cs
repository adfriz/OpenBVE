using System.Collections.Generic;

namespace LibRenderNext.Pipeline
{
	public class RenderPipeline
	{
		private readonly List<IRenderPass> passes = new List<IRenderPass>();

		public void AddPass(IRenderPass pass)
		{
			passes.Add(pass);
		}

		public void Clear()
		{
			passes.Clear();
		}

		public void Execute(RenderContext context)
		{
			foreach (var pass in passes)
			{
				pass.Execute(context);
			}
		}
	}
}
