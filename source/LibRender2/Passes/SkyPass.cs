using System;
using LibRender2.Pipeline;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Passes
{
	/// <summary>
	/// Renders the background/sky.
	/// </summary>
	public class SkyPass : IRenderPass
	{
		private readonly Action<RenderContext> updateBackgroundAction;

		/// <summary>
		/// Initializes a new SkyPass.
		/// </summary>
		/// <param name="updateBackgroundAction">A callback to update and render the background. 
		/// This is usually handled by the application-specific route data.</param>
		public SkyPass(Action<RenderContext> updateBackgroundAction)
		{
			this.updateBackgroundAction = updateBackgroundAction;
		}

		public void Execute(RenderContext context)
		{
			BaseRenderer renderer = context.Renderer;

			// Background must be rendered with depth testing disabled and no shadow mapping
			renderer.Device.SetDepthTest(false);
			renderer.DefaultShader.SetShadowEnabled(false);

			updateBackgroundAction?.Invoke(context);

			renderer.DefaultShader.SetShadowEnabled(renderer.ShadowsEnabled);
			renderer.Device.SetDepthTest(true);
		}
	}
}
