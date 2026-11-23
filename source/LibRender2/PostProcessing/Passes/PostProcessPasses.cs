using System;

namespace LibRender2.PostProcessing.Passes
{
	/// <summary>
	/// Maps the internal HDR (16-bit float) scene to displayable SDR values. This is NOT HDR
	/// output: the destination of this pass is always a regular 8-bit target / the window.
	/// </summary>
	public sealed class ToneMapPass : PostProcessPass
	{
		private PostMaterial material;
		private FullScreenQuad quad;
		private float exposure = 1.0f;

		public override string Name => "ToneMap";

		public float Exposure
		{
			get { return exposure; }
			set { exposure = Math.Max(0.01f, value); }
		}

		public override void Execute(BaseRenderer renderer, RenderTarget source)
		{
			if (material == null)
			{
				material = new PostMaterial(renderer, "post_tonemap");
				quad = new FullScreenQuad();
			}
			material.Activate(source.ColorTexture, 0.0f, exposure);
			quad.Draw();
		}

		public override void Dispose()
		{
			material?.Dispose();
			material = null;
			quad?.Dispose();
			quad = null;
		}
	}

	/// <summary>Identity pass. Also used by the compositor for the final blit to the screen.</summary>
	public sealed class CopyPass : PostProcessPass
	{
		private PostMaterial material;
		private FullScreenQuad quad;

		public override string Name => "Copy";

		public override void Execute(BaseRenderer renderer, RenderTarget source)
		{
			if (material == null)
			{
				material = new PostMaterial(renderer, "post_copy");
				quad = new FullScreenQuad();
			}
			material.Activate(source.ColorTexture, 0.0f);
			quad.Draw();
		}

		public override void Dispose()
		{
			material?.Dispose();
			material = null;
			quad?.Dispose();
			quad = null;
		}
	}

	/// <summary>Example effect pass, disabled by default. Demonstrates how effects chain.</summary>
	public sealed class VignettePass : PostProcessPass
	{
		private PostMaterial material;
		private FullScreenQuad quad;

		public override string Name => "Vignette";

		/// <summary>Darkening amount at the corners (0..1).</summary>
		public float Strength { get; set; } = 0.45f;

		public VignettePass()
		{
			Enabled = false;
		}

		public override void Execute(BaseRenderer renderer, RenderTarget source)
		{
			if (material == null)
			{
				material = new PostMaterial(renderer, "post_vignette");
				quad = new FullScreenQuad();
			}
			material.Activate(source.ColorTexture, Strength);
			quad.Draw();
		}

		public override void Dispose()
		{
			material?.Dispose();
			material = null;
			quad?.Dispose();
			quad = null;
		}
	}
}
