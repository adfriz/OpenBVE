using System;
using LibRenderNext.Pipeline;
using OpenBveApi.Math;
using OpenTK.Graphics.OpenGL;

namespace LibRenderNext.Pipeline.Passes
{
	public class HUDPass : IRenderPass
	{
		private readonly Action<RenderContext> renderUiAction;

		public HUDPass(Action<RenderContext> renderUiAction)
		{
			this.renderUiAction = renderUiAction;
		}

		public void Execute(RenderContext context)
		{
			var renderer = context.Renderer;

			// Render UI
			renderer.OptionLighting = false;
			renderer.ResetOpenGlState();
			renderer.SetBlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
			renderer.SetAlphaFunc(AlphaFunction.Greater, 0.0f);
			RDI.RDIStateCache.SetDepthState(false, DepthFunction.Lequal, false);

			renderer.CurrentViewMatrix = Matrix4D.Identity;
			renderer.DefaultShader.SetCurrentViewMatrix(renderer.CurrentViewMatrix);

			renderUiAction?.Invoke(context);
		}
	}
}
