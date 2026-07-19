using OpenBveApi.Graphics;
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
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// Per-object motion blur reconstructed from scene depth.
	/// </summary>
	/// <remarks>
	/// For every pixel the world position is reconstructed from the depth buffer and
	/// reprojected using the previous frame's view-projection matrix, yielding a correct
	/// per-pixel screen velocity. This produces realistic, object-accurate motion blur
	/// (each moving object smears according to its own motion) without modifying the
	/// scene/material shaders. Requires the pipeline-provided scene depth texture.
	/// </remarks>
	public sealed class MotionBlur : IPostProcessPass
	{
		private readonly BaseRenderer renderer;
		private readonly PostProcessResources resources;

		private Shader blurShader;

		// Cached uniform locations (avoid per-frame string lookups).
		private int uScene;
		private int uDepth;
		private int uCurrentViewProj;
		private int uPreviousViewProj;
		private int uInverseViewProj;
		private int uSamples;
		private int uStrength;

		private RenderTarget outputTarget;
		private int outWidth;
		private int outHeight;

		private MotionBlurMode currentMode;
		private int samples;

		internal MotionBlur(BaseRenderer renderer, PostProcessResources resources)
		{
			this.renderer = renderer;
			this.resources = resources;
		}

		public bool Enabled => renderer.currentOptions.MotionBlur != MotionBlurMode.None;

		public int BeginScene()
		{
			return 0;
		}

		public void Initialize(MotionBlurMode mode)
		{
			currentMode = mode;
			switch (mode)
			{
				case MotionBlurMode.Low:
					samples = 8;
					break;
				case MotionBlurMode.Medium:
					samples = 16;
					break;
				case MotionBlurMode.High:
					samples = 32;
					break;
				default:
					samples = 16;
					break;
			}

			if (blurShader == null)
			{
				blurShader = new Shader(renderer, "post", "motionblur", true);
				uScene = GL.GetUniformLocation(blurShader.Handle, "uScene");
				uDepth = GL.GetUniformLocation(blurShader.Handle, "uDepth");
				uCurrentViewProj = GL.GetUniformLocation(blurShader.Handle, "uCurrentViewProj");
				uPreviousViewProj = GL.GetUniformLocation(blurShader.Handle, "uPreviousViewProj");
				uInverseViewProj = GL.GetUniformLocation(blurShader.Handle, "uInverseViewProj");
				uSamples = GL.GetUniformLocation(blurShader.Handle, "uSamples");
				uStrength = GL.GetUniformLocation(blurShader.Handle, "uStrength");
			}
		}

		public int Render(int sceneTexture, int inputTexture, int sceneWidth, int sceneHeight)
		{
			if (!Enabled)
			{
				return 0;
			}

			resources.Initialize();
			EnsureOutputTarget(sceneWidth, sceneHeight);

			// Scale the reconstructed velocity by speed so motion blur fades out at rest.
			float strength = (float)System.Math.Min(1.0, System.Math.Abs(renderer.Camera.CurrentSpeed) * 0.05);

			// At rest there is no motion, so skip the full-screen pass and pass the input through.
			if (strength <= 0.0f)
			{
				return inputTexture;
			}

			GL.Disable(EnableCap.DepthTest);
			GL.DepthMask(false);
			GL.Disable(EnableCap.CullFace);
			resources.BindFullscreen();

			outputTarget.Bind();
			GL.Viewport(0, 0, outWidth, outHeight);
			blurShader.Activate();
			PostProcessResources.BindTextureUnit(0, inputTexture);
			PostProcessResources.BindTextureUnit(1, resources.SceneDepthTexture);
			GL.ProgramUniform1(blurShader.Handle, uScene, 0);
			GL.ProgramUniform1(blurShader.Handle, uDepth, 1);
			GL.ProgramUniformMatrix4(blurShader.Handle, uCurrentViewProj, false, ref resources.CurrentViewProj);
			GL.ProgramUniformMatrix4(blurShader.Handle, uPreviousViewProj, false, ref resources.PreviousViewProj);
			GL.ProgramUniformMatrix4(blurShader.Handle, uInverseViewProj, false, ref resources.InverseViewProj);
			GL.ProgramUniform1(blurShader.Handle, uSamples, samples);
			GL.ProgramUniform1(blurShader.Handle, uStrength, strength);
			GL.Disable(EnableCap.Blend);
			PostProcessResources.DrawFullscreen();
			GL.Enable(EnableCap.Blend);
			blurShader.Deactivate();

			GL.Enable(EnableCap.DepthTest);
			GL.DepthMask(true);
			GL.BindVertexArray(0);
			renderer.LastBoundTexture = null;

			return outputTarget.Texture;
		}

		private void EnsureOutputTarget(int width, int height)
		{
			if (width != outWidth || height != outHeight || outputTarget == null)
			{
				outWidth = width;
				outHeight = height;
				outputTarget = resources.TargetPool.Acquire(width, height, PixelInternalFormat.Rgba8, PixelFormat.Rgba);
			}
		}

		public void Resize(int width, int height)
		{
		}

		public void Dispose()
		{
			// outputTarget is owned by the pool and disposed there; just drop our reference.
			outputTarget = null;

			blurShader?.Dispose();
			blurShader = null;
		}
	}
}
