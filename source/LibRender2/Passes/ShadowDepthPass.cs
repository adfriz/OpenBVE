using System.Collections.Generic;
using System.Linq;
using LibRender2.Objects;
using LibRender2.Shadows;
using OpenBveApi.Math;
using Raylib_cs;

namespace LibRender2.Passes
{
	public class ShadowDepthPass : IRenderPass
	{
		public string Name => "ShadowDepth";
		public bool Enabled { get; set; } = true;

		public void Render(BaseRenderer renderer, double timeElapsed, double realTimeElapsed)
		{
			if (!renderer.ShadowsEnabled || renderer.CSMShadowMaps == null || renderer.CSMCaster == null)
				return;

			// Logic ported from BaseRenderer.PerformCSMShadowPass
			Vector3 lightDir = new Vector3(
				-renderer.Lighting.OptionLightPosition.X,
				-renderer.Lighting.OptionLightPosition.Y,
				renderer.Lighting.OptionLightPosition.Z
			);

			if (lightDir.IsNullVector()) return;

			renderer.CSMCaster.Resolution = renderer.CSMShadowMaps.Resolution;
			renderer.CSMCaster.Update(lightDir, renderer.CurrentViewMatrix, renderer.CurrentProjectionMatrix, 0.1, renderer.Camera.VerticalViewingAngle, renderer.Screen.AspectRatio);

			Rlgl.DisableBackfaceCulling();
			Rlgl.EnableDepthTest();

			for (int cascade = 0; cascade < renderer.CSMCaster.CascadeCount; cascade++)
			{
				renderer.CSMShadowMaps.BindCascadeForWriting(cascade);
				Rlgl.ClearScreenBuffers(); // Clear depth

				// Render objects into shadow map
				// TODO: Implementation of shadow caster rendering
			}

			renderer.CSMShadowMaps.Unbind();
			Rlgl.EnableBackfaceCulling();
		}
	}
}
