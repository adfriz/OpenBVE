using System;
using LibRenderNext.Pipeline;

namespace LibRenderNext.Pipeline.Passes
{
	public class SkyPass : IRenderPass
	{
		private readonly Action<RenderContext> updateBackgroundAction;

		public SkyPass(Action<RenderContext> updateBackgroundAction)
		{
			this.updateBackgroundAction = updateBackgroundAction;
		}

		public void Execute(RenderContext context)
		{
			var renderer = context.Renderer;

			// Background rendered with depth writes disabled
			RDI.RDIStateCache.SetDepthState(true, OpenTK.Graphics.OpenGL.DepthFunction.Lequal, false);
			renderer.DefaultShader.SetShadowEnabled(false);

			updateBackgroundAction?.Invoke(context);

			RDI.RDIStateCache.SetDepthState(true, OpenTK.Graphics.OpenGL.DepthFunction.Lequal, true);
		}
	}
}
