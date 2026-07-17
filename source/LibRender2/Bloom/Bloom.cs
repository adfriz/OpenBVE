//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
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

using System;
using LibRender2.Shaders;
using OpenBveApi.Graphics;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Blooms
{
	/// <summary>Implements a threshold bright-pass bloom post-processing effect for the SDR pipeline.</summary>
	/// <remarks>
	/// The internal render pipeline is 8-bit (SDR), so this uses a luminance-threshold
	/// bright-pass on the rendered frame, a separable Gaussian blur and an additive
	/// composite. This matches the existing MotionBlur approach of sampling the
	/// backbuffer and does not require a float render target.
	/// </remarks>
	public class Bloom
	{
		private readonly BaseRenderer renderer;

		private Shader extractShader;
		private Shader blurShader;
		private Shader compositeShader;

		// Scene copy captured from the backbuffer.
		private int sceneTexture;
		private int sceneWidth;
		private int sceneHeight;

		// Two ping-pong bloom buffers at reduced resolution.
		private int bloomA;
		private int bloomB;
		private int bloomWidth;
		private int bloomHeight;

		// Framebuffer used to render the extract / blur passes offscreen.
		private int bloomFbo;

		private BloomMode currentMode;

		internal Bloom(BaseRenderer renderer)
		{
			this.renderer = renderer;
		}

		/// <summary>Initializes the bloom effect for the given quality mode.</summary>
		public void Initialize(BloomMode mode)
		{
			currentMode = mode;
			if (mode == BloomMode.Off)
			{
				DisposeTextures();
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
					downsample = 0.5f;
					blurPasses = 3;
					break;
				case BloomMode.High:
					downsample = 0.5f;
					blurPasses = 5;
					break;
				default:
					downsample = 0.5f;
					blurPasses = 3;
					break;
			}

			sceneWidth = renderer.Screen.Width;
			sceneHeight = renderer.Screen.Height;
			bloomWidth = Math.Max(1, (int)(sceneWidth * downsample));
			bloomHeight = Math.Max(1, (int)(sceneHeight * downsample));
			BlurPasses = blurPasses;

			CreateTexture(ref sceneTexture, sceneWidth, sceneHeight);
			CreateTexture(ref bloomA, bloomWidth, bloomHeight);
			CreateTexture(ref bloomB, bloomWidth, bloomHeight);

			if (bloomFbo == 0)
			{
				GL.GenFramebuffers(1, out bloomFbo);
			}

			if (extractShader == null)
			{
				extractShader = new Shader(renderer, "post", "bloom_extract", true);
				blurShader = new Shader(renderer, "post", "bloom_blur", true);
				compositeShader = new Shader(renderer, "post", "bloom_composite", true);
			}
		}

		/// <summary>The number of blur passes performed per frame.</summary>
		internal int BlurPasses { get; private set; }

		private static void CreateTexture(ref int handle, int width, int height)
		{
			if (handle != 0)
			{
				GL.DeleteTextures(1, new[] { handle });
			}

			int[] a = new int[1];
			GL.GenTextures(1, a);
			GL.BindTexture(TextureTarget.Texture2D, a[0]);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb8, width, height, 0, PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);
			handle = a[0];
		}

		/// <summary>Renders the bloom effect over the current frame.</summary>
		/// <remarks>Must be called after the 3D scene has been drawn to the backbuffer, but before the HUD.</remarks>
		public void Render(BloomMode mode)
		{
			if (mode == BloomMode.Off || renderer.Screen.Minimized || renderer.currentOptions.ForceForwardsCompatibleContext)
			{
				// Forward-compatible contexts break CopyTexImage2D; skip gracefully.
				return;
			}

			if (renderer.Screen.Width != sceneWidth || renderer.Screen.Height != sceneHeight || currentMode != mode)
			{
				Initialize(mode);
			}

			if (sceneTexture == 0)
			{
				return;
			}

			renderer.LastBoundTexture = null;

			// The post-processing passes draw a fullscreen triangle and must not be
			// affected by the scene's depth buffer. Disable depth testing/mask and
			// bind the empty VAO so the attribute-less draw is always valid.
			GL.Disable(EnableCap.DepthTest);
			GL.DepthMask(false);
			renderer.dummyVao?.Bind();

			// Capture the rendered frame.
			GL.BindTexture(TextureTarget.Texture2D, sceneTexture);
			GL.CopyTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgb8, 0, 0, sceneWidth, sceneHeight, 0);

			// Bright-pass extract into bloomA (downsampled, offscreen).
			extractShader.Activate();
			BindTextureUnit(0, sceneTexture);
			GL.ProgramUniform1(extractShader.Handle, GL.GetUniformLocation(extractShader.Handle, "uScene"), 0);
			GL.ProgramUniform1(extractShader.Handle, GL.GetUniformLocation(extractShader.Handle, "uThreshold"), 0.6f);
			GL.ProgramUniform1(extractShader.Handle, GL.GetUniformLocation(extractShader.Handle, "uEmissiveBoost"), 0.0f);
			BindBloomTarget(bloomA);
			GL.Viewport(0, 0, bloomWidth, bloomHeight);
			DrawFullscreen();
			extractShader.Deactivate();

			// Separable Gaussian blur, ping-ponging between bloomA and bloomB (offscreen).
			blurShader.Activate();
			float texelX = 1.0f / bloomWidth;
			float texelY = 1.0f / bloomHeight;
			int blurTexLoc = GL.GetUniformLocation(blurShader.Handle, "uTexture");
			int dirLoc = GL.GetUniformLocation(blurShader.Handle, "uDirection");
			GL.ProgramUniform1(blurShader.Handle, blurTexLoc, 0);

			int source = bloomA;
			int destination = bloomB;
			for (int i = 0; i < BlurPasses; i++)
			{
				// Horizontal
				BindTextureUnit(0, source);
				GL.ProgramUniform2(blurShader.Handle, dirLoc, texelX, 0.0f);
				BindBloomTarget(destination);
				GL.Viewport(0, 0, bloomWidth, bloomHeight);
				DrawFullscreen();

				// Vertical
				BindTextureUnit(0, destination);
				GL.ProgramUniform2(blurShader.Handle, dirLoc, 0.0f, texelY);
				BindBloomTarget(source);
				GL.Viewport(0, 0, bloomWidth, bloomHeight);
				DrawFullscreen();
			}
			blurShader.Deactivate();

			// Composite back to the screen. The composite shader already includes the
			// scene, so we replace the framebuffer contents (blending disabled) rather
			// than adding, to avoid doubling the scene.
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
			GL.Viewport(0, 0, sceneWidth, sceneHeight);
			compositeShader.Activate();
			BindTextureUnit(0, sceneTexture);
			BindTextureUnit(1, source);
			GL.ProgramUniform1(compositeShader.Handle, GL.GetUniformLocation(compositeShader.Handle, "uScene"), 0);
			GL.ProgramUniform1(compositeShader.Handle, GL.GetUniformLocation(compositeShader.Handle, "uBloom"), 1);
			GL.ProgramUniform1(compositeShader.Handle, GL.GetUniformLocation(compositeShader.Handle, "uStrength"), 0.8f);
			GL.Disable(EnableCap.Blend);
			DrawFullscreen();
			GL.Enable(EnableCap.Blend);
			compositeShader.Deactivate();

			// Restore depth state for the subsequent HUD/overlay rendering.
			GL.Enable(EnableCap.DepthTest);
			GL.DepthMask(true);
			GL.BindVertexArray(0);

			renderer.LastBoundTexture = null;
		}

		private void BindBloomTarget(int texture)
		{
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, bloomFbo);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
		}

		private static void BindTextureUnit(int unit, int texture)
		{
			GL.ActiveTexture(TextureUnit.Texture0 + unit);
			GL.BindTexture(TextureTarget.Texture2D, texture);
			GL.ActiveTexture(TextureUnit.Texture0);
		}

		private static void DrawFullscreen()
		{
			// Fullscreen triangle generated in the vertex shader from gl_VertexID.
			GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
		}

		private void DisposeTextures()
		{
			if (sceneTexture != 0)
			{
				GL.DeleteTextures(1, new[] { sceneTexture });
				sceneTexture = 0;
			}
			if (bloomA != 0)
			{
				GL.DeleteTextures(1, new[] { bloomA });
				bloomA = 0;
			}
			if (bloomB != 0)
			{
				GL.DeleteTextures(1, new[] { bloomB });
				bloomB = 0;
			}
			if (bloomFbo != 0)
			{
				GL.DeleteFramebuffers(1, new[] { bloomFbo });
				bloomFbo = 0;
			}
		}

		/// <summary>Disposes of all resources held by the bloom effect.</summary>
		public void Dispose()
		{
			DisposeTextures();
			extractShader?.Dispose();
			blurShader?.Dispose();
			compositeShader?.Dispose();
			extractShader = null;
			blurShader = null;
			compositeShader = null;
		}
	}
}
