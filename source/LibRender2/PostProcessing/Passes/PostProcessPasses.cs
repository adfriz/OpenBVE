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

	/// <summary>
	/// Ready-made pass for any single full-screen shader. A new effect needs only a fragment shader
	/// using the shared post.vert conventions (uniform sampler2D uSource, uniform float uStrength,
	/// in vec2 vUv, out vec4 fragColor) and one registration line in Compositor.Initialize().
	/// Override Execute() for effects that need extra uniforms or multiple draw calls.
	/// </summary>
	public class SimpleShaderPass : PostProcessPass, IDisposable
	{
		private readonly string shaderName;
		private PostMaterial material;
		private FullScreenQuad quad;

		public override string Name { get; }

		/// <summary>Value sent to the shader's uStrength uniform.</summary>
		public float Strength { get; set; }

		public SimpleShaderPass(string name, string shaderName, float strength = 0.0f, bool enabled = true)
		{
			Name = name;
			this.shaderName = shaderName;
			Strength = strength;
			Enabled = enabled;
		}

		public override void Execute(BaseRenderer renderer, RenderTarget source)
		{
			if (material == null)
			{
				material = new PostMaterial(renderer, shaderName);
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

	/// <summary>Example effect pass, disabled by default. Demonstrates how effects chain.</summary>
	public sealed class VignettePass : SimpleShaderPass
	{
		public VignettePass() : base("Vignette", "post_vignette", 0.45f, false)
		{
		}
	}
}
