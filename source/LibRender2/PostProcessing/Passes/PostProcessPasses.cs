using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing.Passes
{
	/// <summary>
	/// Maps the internal HDR (16-bit float) scene to displayable SDR values. This is NOT HDR
	/// output: the destination of this pass is always a regular 8-bit target / the window.
	/// </summary>
	public sealed class ToneMapPass : PostProcessPass
	{
		private PostMaterial material;
		private FullScreenTriangle quad;
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
				quad = new FullScreenTriangle();
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
		private FullScreenTriangle quad;

		public override string Name => "Copy";

		public override void Execute(BaseRenderer renderer, RenderTarget source)
		{
			if (material == null)
			{
				material = new PostMaterial(renderer, "post_copy");
				quad = new FullScreenTriangle();
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
		private FullScreenTriangle quad;

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
				quad = new FullScreenTriangle();
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

	/// <summary>
	/// Depth of field with a golden-angle bokeh disk. The expensive gather runs at half
	/// resolution and is composited over the full-resolution source; alpha carries coverage
	/// so in-focus pixels keep their original detail. All parameters are exposed as tunings.
	/// </summary>
	public sealed class DofPass : PostProcessPass
	{
		private PostMaterial material;
		private PostMaterial compositeMaterial;
		private FullScreenTriangle quad;
		private RenderTarget halfTarget;
		private int halfWidth;
		private int halfHeight;
		private bool halfHdr;
		private float focusDistance = 50.0f;
		private float focusRange = 120.0f;
		private float aperture = 1.0f;
		private float maxBlur = 8.0f;
		private float nearBoost = 1.5f;
		private float strength = 1.0f;
		private bool halfResolution = false;

		public override string Name => "DepthOfField";
		public override string DisplayName => "Depth of Field";

		public float FocusDistance
		{
			get { return focusDistance; }
			set { focusDistance = Math.Max(0.1f, value); }
		}
		public float FocusRange
		{
			get { return focusRange; }
			set { focusRange = Math.Max(0.1f, value); }
		}
		public float Aperture
		{
			get { return aperture; }
			set { aperture = Math.Max(0.0f, value); }
		}
		public float MaxBlur
		{
			get { return maxBlur; }
			set { maxBlur = Math.Max(0.0f, value); }
		}
		public float NearBoost
		{
			get { return nearBoost; }
			set { nearBoost = Math.Max(0.0f, value); }
		}
		public float Strength
		{
			get { return strength; }
			set { strength = Math.Min(1.0f, Math.Max(0.0f, value)); }
		}

		/// <summary>Runs the gather at half resolution and composites back. Faster, slightly softer.</summary>
		public bool HalfResolution
		{
			get { return halfResolution; }
			set { halfResolution = value; }
		}

		public DofPass()
		{
			Enabled = false;
		}

		public override IList<PostProcessTuning> Tunings => new[]
		{
			new PostProcessTuning("Focus distance", 0.5f, 500.0f, () => FocusDistance, v => FocusDistance = v),
			new PostProcessTuning("Focus range", 1.0f, 500.0f, () => FocusRange, v => FocusRange = v),
			new PostProcessTuning("Aperture", 0.0f, 4.0f, () => Aperture, v => Aperture = v),
			new PostProcessTuning("Max blur (px)", 0.0f, 16.0f, () => MaxBlur, v => MaxBlur = v),
			new PostProcessTuning("Near boost", 0.5f, 4.0f, () => NearBoost, v => NearBoost = v),
			new PostProcessTuning("Strength", 0.0f, 1.0f, () => Strength, v => Strength = v),
			new PostProcessTuning("Half resolution", 0.0f, 1.0f, () => HalfResolution ? 1.0f : 0.0f, v => HalfResolution = v > 0.5f)
		};

		private void EnsureHalfTarget(RenderTarget source)
		{
			int w = Math.Max(1, source.Width / 2);
			int h = Math.Max(1, source.Height / 2);
			if (halfTarget == null || halfWidth != w || halfHeight != h || halfHdr != source.Hdr)
			{
				halfTarget?.Dispose();
				halfTarget = new RenderTarget(w, h, false, source.Hdr);
				halfWidth = w;
				halfHeight = h;
				halfHdr = source.Hdr;
			}
		}

		private void SetUniforms(PostMaterial mat, BaseRenderer renderer, RenderTarget source)
		{
			mat.SetInt("uDepth", 1);
			mat.SetFloat("uFocus", FocusDistance);
			mat.SetFloat("uFocusRange", FocusRange);
			mat.SetFloat("uAperture", Aperture);
			mat.SetFloat("uMaxBlur", MaxBlur);
			mat.SetFloat("uNearBoost", NearBoost);
			mat.SetFloat("uNear", renderer.SceneNear);
			mat.SetFloat("uFar", renderer.SceneFar);
			mat.SetFloat("uTexelX", 1.0f / Math.Max(1, source.Width));
			mat.SetFloat("uTexelY", 1.0f / Math.Max(1, source.Height));
		}

		public override void Execute(BaseRenderer renderer, RenderTarget source)
		{
			if (material == null)
			{
				material = new PostMaterial(renderer, "post_dof");
				compositeMaterial = new PostMaterial(renderer, "post_dof_composite");
				quad = new FullScreenTriangle();
			}
			if (source == null)
			{
				return;
			}
			int depthTexture = renderer.Compositor == null ? 0 : renderer.Compositor.SceneDepthTexture;
			if (depthTexture == 0)
			{
				return;
			}

			if (!HalfResolution)
			{
				// Full resolution: one draw, shader mixes sharp and blurred itself.
				material.Activate(source.ColorTexture, Strength);
				TextureBinder.Bind(1, depthTexture);
				material.SetFloat("uOutputCoc", 0.0f);
				SetUniforms(material, renderer, source);
				quad.Draw();
				return;
			}

			EnsureHalfTarget(source);

			// The compositor already bound this pass's destination. Remember it, and its
			// viewport, so the composite writes back to exactly the same place.
			int destinationFbo;
			GL.GetInteger(GetPName.FramebufferBinding, out destinationFbo);
			int[] viewport = new int[4];
			GL.GetInteger(GetPName.Viewport, viewport);

			// 1) Half-resolution bokeh gather. Texel size stays full-resolution so CoC
			//    radii are still measured in screen pixels; alpha carries coverage.
			halfTarget.Bind();
			material.Activate(source.ColorTexture, Strength);
			TextureBinder.Bind(1, depthTexture);

			material.SetFloat("uOutputCoc", 1.0f);
			SetUniforms(material, renderer, source);
			quad.Draw();

			// 2) Upsample + composite at the destination's full resolution.
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, destinationFbo);
			GL.Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
			compositeMaterial.Activate(source.ColorTexture, Strength);
			TextureBinder.Bind(1, halfTarget.ColorTexture);
			compositeMaterial.SetInt("uBlur", 1);
			quad.Draw();
		}

		public override void Dispose()
		{
			material?.Dispose();
			material = null;
			compositeMaterial?.Dispose();
			compositeMaterial = null;
			halfTarget?.Dispose();
			halfTarget = null;
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
