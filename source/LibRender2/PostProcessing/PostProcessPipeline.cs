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

using System;
using System.Collections.Generic;
using LibRender2.Shaders;
using OpenBveApi.Graphics;
using OpenBveApi.Math;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// Ordered collection of post-processing passes applied after the 3D scene is drawn.
	/// </summary>
	/// <remarks>
	/// The pipeline owns the shared <see cref="PostProcessResources"/> (fullscreen geometry,
	/// the scene-capture framebuffer, and the camera matrices) and runs each enabled pass in
	/// order, then blits the final result to the default framebuffer. With an empty pass list
	/// it is a transparent passthrough: the scene renders straight to the screen and
	/// <see cref="EndScene"/> is a no-op, so there is zero cost.
	/// </remarks>
	public sealed class PostProcessPipeline : IDisposable
	{
		private readonly BaseRenderer renderer;

		/// <summary>Shared GL resources available to all passes.</summary>
		internal PostProcessResources Resources;

		private readonly List<IPostProcessPass> passes = new List<IPostProcessPass>();

		/// <summary>
		/// Execution order of passes, by type. Passes whose type is not listed run
		/// after the ordered ones, in registration order. Set via <see cref="SetPassOrder"/>.
		/// </summary>
		private readonly List<Type> passOrder = new List<Type>();

		// Pipeline-owned scene-capture target (colour + depth).
		private int captureFbo;
		private int captureColor;
		private int captureDepth;
		private int captureWidth;
		private int captureHeight;

		private bool sceneCaptured;
		private bool hasPreviousMatrix;
		private bool dbgLogged;

		internal PostProcessPipeline(BaseRenderer renderer)
		{
			this.renderer = renderer;
			Resources = new PostProcessResources(renderer);
		}

		/// <summary>Registers a pass at the end of the pipeline.</summary>
		public void AddPass(IPostProcessPass pass)
		{
			passes.Add(pass);
		}

		/// <summary>
		/// Sets the execution order of passes by their concrete type. Types not present
		/// in the list keep their registration order and run after the ordered passes.
		/// </summary>
		public void SetPassOrder(IEnumerable<Type> orderedTypes)
		{
			passOrder.Clear();
			passOrder.AddRange(orderedTypes);
		}

		/// <summary>Returns the passes sorted by the configured <see cref="passOrder"/>.</summary>
		private List<IPostProcessPass> OrderedPasses()
		{
			if (passOrder.Count == 0)
			{
				return new List<IPostProcessPass>(passes);
			}

			List<IPostProcessPass> result = new List<IPostProcessPass>(passes.Count);
			foreach (Type type in passOrder)
			{
				foreach (IPostProcessPass pass in passes)
				{
					if (pass.GetType() == type)
					{
						result.Add(pass);
					}
				}
			}
			// Append any passes whose type is not in the order list.
			foreach (IPostProcessPass pass in passes)
			{
				if (!passOrder.Contains(pass.GetType()))
				{
					result.Add(pass);
				}
			}
			return result;
		}

		/// <summary>
		/// Applies a saved pass order string (comma-separated concrete type names in the
		/// <see cref="LibRender2.PostProcessing"/> namespace). Unknown or missing types are ignored.
		/// </summary>
		public void ApplyPassOrder(string order)
		{
			passOrder.Clear();
			if (string.IsNullOrWhiteSpace(order))
			{
				return;
			}

			foreach (string raw in order.Split(','))
			{
				string name = raw.Trim();
				if (name.Length == 0)
				{
					continue;
				}
				Type type = Type.GetType("LibRender2.PostProcessing." + name);
				if (type != null && typeof(IPostProcessPass).IsAssignableFrom(type))
				{
					passOrder.Add(type);
				}
			}
		}

		/// <summary>Removes all registered passes.</summary>
		public void ClearPasses()
		{
			foreach (IPostProcessPass pass in passes)
			{
				pass.Dispose();
			}

			passes.Clear();
		}

		/// <summary>(Re)initializes the registered bloom pass for the given quality mode.</summary>
		public void InitializeBloom(BloomMode mode)
		{
			foreach (IPostProcessPass pass in passes)
			{
				if (pass is BloomPass bloom)
				{
					bloom.Initialize(mode);
					return;
				}
			}
		}

		/// <summary>(Re)initializes the registered motion blur pass for the given mode.</summary>
		public void InitializeMotionBlur(MotionBlurMode mode)
		{
			foreach (IPostProcessPass pass in passes)
			{
				if (pass is MotionBlur modern)
				{
					modern.Initialize(mode);
				}
			}
		}

		/// <summary>Whether any pass is enabled and the pipeline should intercept the scene.</summary>
		public bool AnyEnabled
		{
			get
			{
				foreach (IPostProcessPass pass in passes)
				{
					if (pass.Enabled)
					{
						return true;
					}
				}

				return false;
			}
		}

		/// <summary>
		/// Called before the 3D scene is rendered. Binds the shared scene-capture framebuffer
		/// (or the default framebuffer if the pipeline is inactive).
		/// </summary>
		/// <remarks>
		/// Safe to call multiple times per frame: the shadow pass in some viewers restores
		/// the default framebuffer, so the capture target is re-bound on each call.
		/// </remarks>
		public void BeginScene()
		{
			sceneCaptured = false;

			if (!dbgLogged)
			{
				dbgLogged = true;
				System.Diagnostics.Debug.WriteLine("[PP] AnyEnabled=" + AnyEnabled + " Bloom=" + renderer.currentOptions.Bloom + " MotionBlur=" + renderer.currentOptions.MotionBlur +
					" Minimized=" + renderer.Screen.Minimized + " ForceFwd=" + renderer.currentOptions.ForceForwardsCompatibleContext +
					" hash=" + renderer.currentOptions.GetHashCode());
			}

			if (!AnyEnabled || renderer.Screen.Minimized || renderer.currentOptions.ForceForwardsCompatibleContext)
			{
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
				return;
			}

			Resources.Initialize();
			if (Resources.TargetPool == null)
			{
				Resources.TargetPool = new RenderTargetPool();
			}
			EnsureCaptureTarget(renderer.Screen.Width, renderer.Screen.Height);

			// If the capture target could not be created (unsupported on this context),
			// fall back to rendering straight to the screen.
			if (captureFbo == 0)
			{
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
				return;
			}

			GL.BindFramebuffer(FramebufferTarget.Framebuffer, captureFbo);
			GL.Viewport(0, 0, captureWidth, captureHeight);
			renderer.LastBoundTexture = null;
			sceneCaptured = true;
		}

		/// <summary>
		/// Called after the 3D scene has been drawn. Runs each enabled pass in order and
		/// composites the final result to the default framebuffer.
		/// </summary>
		/// <remarks>Must be called before any 2D overlay / HUD rendering.</remarks>
		public void EndScene()
		{
			if (!sceneCaptured)
			{
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
				return;
			}

			int width = renderer.Screen.Width;
			int height = renderer.Screen.Height;

			UpdateCameraMatrices();

			int sceneTexture = Resources.SceneColorTexture;
			int current = sceneTexture;

			foreach (IPostProcessPass pass in OrderedPasses())
			{
				if (!pass.Enabled)
				{
					continue;
				}

				int result = pass.Render(sceneTexture, current, width, height);
				if (result != 0 && result != current)
				{
					current = result;
				}
			}

			// Composite the final texture to the screen using the pass-through copy
			// shader (alpha forced to 1.0 so transparency is unaffected).
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
			GL.Viewport(0, 0, width, height);
			GL.Disable(EnableCap.DepthTest);
			GL.DepthMask(false);
			GL.Clear(ClearBufferMask.ColorBufferBit);
			GL.Disable(EnableCap.Blend);
			Resources.BindFullscreen();
			Resources.BlitShader.Activate();
			PostProcessResources.BindTextureUnit(0, current);
			GL.ProgramUniform1(Resources.BlitShader.Handle, Resources.BlitTextureLocation, 0);
			PostProcessResources.DrawFullscreen();
			Resources.BlitShader.Deactivate();
			GL.Enable(EnableCap.Blend);
			GL.BindVertexArray(0);

			// Recycle every render target borrowed by the passes this frame back into the pool.
			Resources.TargetPool?.Clear();

			renderer.LastBoundTexture = null;
		}

		private void UpdateCameraMatrices()
		{
			Matrix4D view = renderer.CurrentViewMatrix;
			Matrix4D proj = renderer.CurrentProjectionMatrix;
			Matrix4D viewProj = proj * view;

			Matrix4 glView = ConvertToMatrix4(view);
			Matrix4 glProj = ConvertToMatrix4(proj);
			Matrix4 glViewProj = glProj * glView;

			if (hasPreviousMatrix)
			{
				Resources.PreviousViewProj = Resources.CurrentViewProj;
			}
			else
			{
				Resources.PreviousViewProj = glViewProj;
				hasPreviousMatrix = true;
			}

			Resources.CurrentViewProj = glViewProj;
			Resources.InverseViewProj = Matrix4.Invert(glViewProj);
		}

		private static Matrix4 ConvertToMatrix4(Matrix4D mat)
		{
			return new Matrix4(
				(float)mat.Row0.X, (float)mat.Row0.Y, (float)mat.Row0.Z, (float)mat.Row0.W,
				(float)mat.Row1.X, (float)mat.Row1.Y, (float)mat.Row1.Z, (float)mat.Row1.W,
				(float)mat.Row2.X, (float)mat.Row2.Y, (float)mat.Row2.Z, (float)mat.Row2.W,
				(float)mat.Row3.X, (float)mat.Row3.Y, (float)mat.Row3.Z, (float)mat.Row3.W
			);
		}

		private void EnsureCaptureTarget(int width, int height)
		{
			if (captureFbo == 0)
			{
				GL.GenFramebuffers(1, out captureFbo);
			}

			if (width != captureWidth || height != captureHeight || captureColor == 0)
			{
				captureWidth = width;
				captureHeight = height;

				PostProcessResources.CreateTexture(ref captureColor, width, height, PixelInternalFormat.Rgba8, PixelFormat.Rgba);

				if (captureDepth != 0)
				{
					GL.DeleteTextures(1, new[] { captureDepth });
				}
				GL.GenTextures(1, out captureDepth);
				GL.BindTexture(TextureTarget.Texture2D, captureDepth);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Nearest);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent32, width, height, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);

				GL.BindFramebuffer(FramebufferTarget.Framebuffer, captureFbo);
				GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, captureColor, 0);
				GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, captureDepth, 0);
				FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
				System.Diagnostics.Debug.WriteLine("[PP] captureFbo status=" + status + " depthFmt=DepthComponent32/Float");
				if (status != FramebufferErrorCode.FramebufferComplete)
				{
					// The offscreen capture target is not supported on this GL context/driver.
					// Disable the pipeline so the scene renders straight to the screen.
					GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
					DisposeCaptureTarget();
					captureFbo = 0;
					captureColor = 0;
					captureDepth = 0;
					Resources.SceneColorTexture = 0;
					Resources.SceneDepthTexture = 0;
					return;
				}
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

				Resources.SceneColorTexture = captureColor;
				Resources.SceneDepthTexture = captureDepth;
			}
		}

		private void DisposeCaptureTarget()
		{
			if (captureColor != 0)
			{
				GL.DeleteTextures(1, new[] { captureColor });
				captureColor = 0;
			}

			if (captureDepth != 0)
			{
				GL.DeleteTextures(1, new[] { captureDepth });
				captureDepth = 0;
			}

			if (captureFbo != 0)
			{
				GL.DeleteFramebuffers(1, new[] { captureFbo });
				captureFbo = 0;
			}
		}

		/// <summary>Releases all GL resources held by the pipeline and its passes.</summary>
		public void Dispose()
		{
			ClearPasses();
			Resources.Dispose();

			if (captureFbo != 0)
			{
				GL.DeleteFramebuffers(1, new[] { captureFbo });
				captureFbo = 0;
			}

			if (captureColor != 0)
			{
				GL.DeleteTextures(1, new[] { captureColor });
				captureColor = 0;
			}

			if (captureDepth != 0)
			{
				GL.DeleteTextures(1, new[] { captureDepth });
				captureDepth = 0;
			}
		}
	}
}
