using LibRenderNext.Pipeline;

namespace LibRenderNext.Pipeline.Passes
{
	public class ShadowPass : IRenderPass
	{
		public void Execute(RenderContext context)
		{
			var renderer = context.Renderer;
			if (renderer.ShadowsEnabled && renderer.Shadows != null)
			{
				renderer.Shadows.RenderPass();
			}
		}
	}
}
