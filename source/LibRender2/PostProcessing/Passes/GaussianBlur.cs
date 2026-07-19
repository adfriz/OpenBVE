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
	/// A reusable separable Gaussian blur post-processing pass.
	/// </summary>
	/// <remarks>
	/// Blurs its input texture with N horizontal/vertical pairs using the shared
	/// 9-tap <c>blur</c> shader (the spread uniform widens the halo without
	/// extra passes). This is effect-agnostic: bloom, depth-of-field, glow and other
	/// passes can all reuse a single instance rather than reimplementing the same
	/// ping-pong blur. It does not capture the scene, so it is safe to register as an
	/// ordinary stage in the <see cref="PostProcessPipeline"/>.
	/// </remarks>
	public sealed class GaussianBlur : IPostProcessPass
	{
		private readonly BaseRenderer renderer;
		private readonly PostProcessResources resources;

		private Shader blurShader;

		// Cached uniform locations (avoid per-frame string lookups).
		private int uTexture;
		private int uDirection;
		private int uSpread;

		private RenderTarget rtA;
		private RenderTarget rtB;
		private int bufferWidth;
		private int bufferHeight;

		/// <summary>The number of H/V blur pairs performed per frame.</summary>
		public int Iterations { get; set; } = 4;

		/// <summary>Halo spread multiplier applied to the tap offset (0 = default).</summary>
		public float Spread { get; set; }

		public bool Enabled => true;

		public int CapturedSceneTexture => 0;

		internal GaussianBlur(BaseRenderer renderer, PostProcessResources resources)
		{
			this.renderer = renderer;
			this.resources = resources;
		}

		/// <summary>Allocates the blur buffers at the given (typically downsampled) resolution.</summary>
		public void Initialize(int width, int height)
		{
			bufferWidth = width;
			bufferHeight = height;
			rtA = resources.TargetPool.Acquire(width, height, PixelInternalFormat.Rgb8, PixelFormat.Rgb);
			rtB = resources.TargetPool.Acquire(width, height, PixelInternalFormat.Rgb8, PixelFormat.Rgb);

			if (blurShader == null)
			{
				blurShader = new Shader(renderer, "post", "blur", true);
				uTexture = GL.GetUniformLocation(blurShader.Handle, "uTexture");
				uDirection = GL.GetUniformLocation(blurShader.Handle, "uDirection");
				uSpread = GL.GetUniformLocation(blurShader.Handle, "uSpread");
			}
		}

		public int BeginScene()
		{
			return 0;
		}

		public int Render(int sceneTexture, int inputTexture, int sceneWidth, int sceneHeight)
		{
			if (inputTexture == 0 || rtA == null || rtB == null)
			{
				return 0;
			}

			resources.Initialize();

			// Disable depth test/mask and culling so the fullscreen triangle always draws.
			GL.Disable(EnableCap.DepthTest);
			GL.DepthMask(false);
			GL.Disable(EnableCap.CullFace);
			resources.BindFullscreen();

			blurShader.Activate();
			GL.ProgramUniform1(blurShader.Handle, uTexture, 0);
			GL.ProgramUniform1(blurShader.Handle, uSpread, Spread);

			float texelX = 1.0f / bufferWidth;
			float texelY = 1.0f / bufferHeight;

			// Ping-pong between the two pool targets. 'read' holds the source for the
			// current H/V pair; after the V pass the blurred result lives in 'read' again.
			RenderTarget read = rtA;
			RenderTarget write = rtB;

			for (int i = 0; i < Iterations; i++)
			{
				// Horizontal: read 'read', write 'write'.
				PostProcessResources.BindTextureUnit(0, read.Texture);
				GL.ProgramUniform2(blurShader.Handle, uDirection, texelX, 0.0f);
				write.Bind();
				GL.Viewport(0, 0, bufferWidth, bufferHeight);
				PostProcessResources.DrawFullscreen();

				// Vertical: read 'write' (the H result), write back into 'read'.
				PostProcessResources.BindTextureUnit(0, write.Texture);
				GL.ProgramUniform2(blurShader.Handle, uDirection, 0.0f, texelY);
				read.Bind();
				GL.Viewport(0, 0, bufferWidth, bufferHeight);
				PostProcessResources.DrawFullscreen();

				// Swap so the next iteration reads the freshly blurred texture.
				RenderTarget temp = read;
				read = write;
				write = temp;
			}

			blurShader.Deactivate();

			// Restore depth state for subsequent HUD/overlay rendering.
			GL.Enable(EnableCap.DepthTest);
			GL.DepthMask(true);
			GL.BindVertexArray(0);
			renderer.LastBoundTexture = null;

			// The final blurred result sits in 'read' (last swap puts it there).
			return read.Texture;
		}

		public void Resize(int width, int height)
		{
			// Buffers are sized by the owning effect; nothing to do standalone.
		}

		public void Dispose()
		{
			// rtA / rtB are owned by the pool and disposed there; just drop our references.
			rtA = null;
			rtB = null;

			blurShader?.Dispose();
			blurShader = null;
		}
	}
}
