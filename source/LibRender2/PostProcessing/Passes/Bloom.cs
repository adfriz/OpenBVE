//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the above disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the above disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
//ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
//ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using LibRender2.Shaders;
using OpenBveApi.Graphics;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// Selective bloom implemented as a composable <see cref="IPostProcessPass"/>.
	/// </summary>
	/// <remarks>
	/// Reads the scene captured by the pipeline (the emissive mask is carried in the alpha
	/// channel by the material shader). The bright-pass extract step only blooms pixels whose
	/// alpha mask is non-zero, so background and lit geometry never glow. The separable blur
	/// is delegated to a shared <see cref="GaussianBlur"/> instance; the blurred result is
	/// composited back over the scene into a dedicated output texture returned to the pipeline.
	/// </remarks>
	public sealed class BloomPass : IPostProcessPass
	{
		private readonly BaseRenderer renderer;
		private readonly PostProcessResources resources;
		private readonly GaussianBlur blur;

		private Shader extractShader;
		private Shader compositeShader;

		// Cached uniform locations (avoid per-frame string lookups).
		private int uExtractScene;
		private int uExtractThreshold;
		private int uExtractEmissiveBoost;
		private int uCompositeScene;
		private int uCompositeBloom;
		private int uCompositeStrength;

		// Downsampled bright-pass target fed into the blur (pooled).
		private RenderTarget bloomSourceTarget;
		private int bloomWidth;
		private int bloomHeight;

		// Full-resolution output target (scene + bloom) returned to the pipeline.
		private RenderTarget outputTarget;
		private int outWidth;
		private int outHeight;

		private BloomMode currentMode;

		internal BloomPass(BaseRenderer renderer, PostProcessResources resources)
		{
			this.renderer = renderer;
			this.resources = resources;
			blur = new GaussianBlur(renderer, resources);
		}

		public bool Enabled => renderer.currentOptions.Bloom != BloomMode.Off;

		public int BeginScene()
		{
			return 0;
		}

		/// <summary>Initializes the bloom effect for the given quality mode.</summary>
		public void Initialize(BloomMode mode)
		{
			currentMode = mode;
			if (mode == BloomMode.Off)
			{
				DisposeTargets();
				return;
			}

			renderer.LastBoundTexture = null;

			float downsample;
			int blurPasses;
			switch (mode)
			{
				case BloomMode.Low:
					downsample = 0.25f;
					blurPasses = 2;
					break;
				case BloomMode.Medium:
					downsample = 0.25f;
					blurPasses = 4;
					break;
				case BloomMode.High:
					downsample = 0.25f;
					blurPasses = 6;
					break;
				default:
					downsample = 0.5f;
					blurPasses = 3;
					break;
			}

			int width = renderer.Screen.Width;
			int height = renderer.Screen.Height;
			bloomWidth = System.Math.Max(1, (int)(width * downsample));
			bloomHeight = System.Math.Max(1, (int)(height * downsample));

			EnsureOutputTarget(width, height);
			bloomSourceTarget = resources.TargetPool.Acquire(bloomWidth, bloomHeight, PixelInternalFormat.Rgb8, PixelFormat.Rgb);

			if (extractShader == null)
			{
				extractShader = new Shader(renderer, "post", "bloom_extract", true);
				compositeShader = new Shader(renderer, "post", "bloom_composite", true);
				uExtractScene = GL.GetUniformLocation(extractShader.Handle, "uScene");
				uExtractThreshold = GL.GetUniformLocation(extractShader.Handle, "uThreshold");
				uExtractEmissiveBoost = GL.GetUniformLocation(extractShader.Handle, "uEmissiveBoost");
				uCompositeScene = GL.GetUniformLocation(compositeShader.Handle, "uScene");
				uCompositeBloom = GL.GetUniformLocation(compositeShader.Handle, "uBloom");
				uCompositeStrength = GL.GetUniformLocation(compositeShader.Handle, "uStrength");
			}

			blur.Initialize(bloomWidth, bloomHeight);
			blur.Iterations = blurPasses;
		}

		public int Render(int sceneTexture, int inputTexture, int sceneWidth, int sceneHeight)
		{
			if (currentMode == BloomMode.Off)
			{
				System.Diagnostics.Debug.WriteLine("[BLOOM] skip currentMode=Off sceneTex=" + sceneTexture);
				return 0;
			}

			if (sceneTexture == 0)
			{
				return 0;
			}

			// (Re)initialize if the mode or size changed.
			if (currentMode == BloomMode.Off || renderer.Screen.Width != outWidth || renderer.Screen.Height != outHeight)
			{
				Initialize(renderer.currentOptions.Bloom);
			}

			resources.Initialize();
			GL.Disable(EnableCap.DepthTest);
			GL.DepthMask(false);
			GL.Disable(EnableCap.CullFace);
			resources.BindFullscreen();

			float threshold = (float)renderer.currentOptions.BloomThreshold;
			float strength = (float)renderer.currentOptions.BloomStrength;

			// No visible glow: copy the scene through unchanged so the pipeline receives it.
			if (strength <= 0.0f)
			{
				EnsureOutputTarget(outWidth, outHeight);
				outputTarget.Bind();
				GL.Viewport(0, 0, outWidth, outHeight);
				compositeShader.Activate();
				PostProcessResources.BindTextureUnit(0, sceneTexture);
				PostProcessResources.BindTextureUnit(1, sceneTexture);
				GL.ProgramUniform1(compositeShader.Handle, uCompositeScene, 0);
				GL.ProgramUniform1(compositeShader.Handle, uCompositeBloom, 1);
				GL.ProgramUniform1(compositeShader.Handle, uCompositeStrength, 0.0f);
				GL.Disable(EnableCap.Blend);
				PostProcessResources.DrawFullscreen();
				GL.Enable(EnableCap.Blend);
				compositeShader.Deactivate();
				RestoreState();
				return outputTarget.Texture;
			}

			// BloomSpread widens the halo by adding blur iterations (proper Gaussian via
			// more H/V pairs) rather than just stretching the tap offset, which would
			// alias at large values. Offset keeps a small contribution for smoothness.
			float spread = (float)renderer.currentOptions.BloomSpread;
			blur.Iterations = System.Math.Min(12, 2 + (int)(spread * 1.5f));
			blur.Spread = System.Math.Min(spread, 2.0f);

			// Bright-pass extract: scene -> bloomSource (downsampled).
			extractShader.Activate();
			PostProcessResources.BindTextureUnit(0, sceneTexture);
			GL.ProgramUniform1(extractShader.Handle, uExtractScene, 0);
			GL.ProgramUniform1(extractShader.Handle, uExtractThreshold, threshold);
			GL.ProgramUniform1(extractShader.Handle, uExtractEmissiveBoost, 0.0f);
			bloomSourceTarget.Bind();
			GL.Viewport(0, 0, bloomWidth, bloomHeight);
			PostProcessResources.DrawFullscreen();
			extractShader.Deactivate();

			// Separable blur (reuses the shared GaussianBlur pass).
			int blurred = blur.Render(sceneTexture, bloomSourceTarget.Texture, sceneWidth, sceneHeight);

			// Composite scene + bloom into the output texture.
			outputTarget.Bind();
			GL.Viewport(0, 0, outWidth, outHeight);
			compositeShader.Activate();
			PostProcessResources.BindTextureUnit(0, sceneTexture);
			PostProcessResources.BindTextureUnit(1, blurred);
			GL.ProgramUniform1(compositeShader.Handle, uCompositeScene, 0);
			GL.ProgramUniform1(compositeShader.Handle, uCompositeBloom, 1);
			GL.ProgramUniform1(compositeShader.Handle, uCompositeStrength, strength);
			GL.Disable(EnableCap.Blend);
			PostProcessResources.DrawFullscreen();
			GL.Enable(EnableCap.Blend);
			compositeShader.Deactivate();

			RestoreState();
			return outputTarget.Texture;
		}

		private void EnsureOutputTarget(int width, int height)
		{
			outWidth = width;
			outHeight = height;
			outputTarget = resources.TargetPool.Acquire(width, height, PixelInternalFormat.Rgba8, PixelFormat.Rgba);
		}

		private void RestoreState()
		{
			GL.Enable(EnableCap.DepthTest);
			GL.DepthMask(true);
			GL.BindVertexArray(0);
			renderer.LastBoundTexture = null;
		}

		public void Resize(int width, int height)
		{
			if (renderer.currentOptions.Bloom != BloomMode.Off)
			{
				Initialize(renderer.currentOptions.Bloom);
			}
		}

		private void DisposeTargets()
		{
			// Targets are owned by the pool and disposed there; just drop our references.
			outputTarget = null;
			bloomSourceTarget = null;
		}

		public void Dispose()
		{
			DisposeTargets();
			blur.Dispose();
			extractShader?.Dispose();
			compositeShader?.Dispose();
			extractShader = null;
			compositeShader = null;
		}
	}
}
