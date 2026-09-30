using System;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Rendering
{
	/// World renders here in half-float. Tonemap reads it after.
	public class HdrPipeline : IDisposable
	{
		private readonly BaseRenderer renderer;

		/// Off for good after a GL failure.
		public bool Enabled = true;

		private int targetFbo;
		private int resolveFbo;
		private int hdrTexture;
		private int colorRb;
		private int depthRb;
		private int seenWidth = -1;
		private int seenHeight = -1;
		private int seenSamples = -1;

		internal HdrPipeline(BaseRenderer renderer)
		{
			this.renderer = renderer;
		}

		// Same size? Reuse. Else rebuild. False = draw direct to screen.
		private bool Ensure(int width, int height, int samples)
		{
			if (!Enabled)
			{
				return false;
			}
			if (width == seenWidth && height == seenHeight && samples == seenSamples && targetFbo != 0)
			{
				return true;
			}
			DeleteTargets();
			try
			{
				if (samples > 0)
				{
					BuildMsTargets(width, height, samples);
				}
				else
				{
					BuildSingleTargets(width, height);
				}
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
				seenWidth = width;
				seenHeight = height;
				seenSamples = samples;
				return true;
			}
			catch
			{
				FullCleanup();
				Enabled = false;
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
				return false;
			}
		}

		// MSAA color + depth, then resolve into a texture Tonemap can read.
		private void BuildMsTargets(int width, int height, int samples)
		{
			targetFbo = GL.GenFramebuffer();
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFbo);
			colorRb = GL.GenRenderbuffer();
			GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, colorRb);
			GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, samples, RenderbufferStorage.Rgba16f, width, height);
			GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, colorRb);
			depthRb = GL.GenRenderbuffer();
			GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthRb);
			GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, samples, RenderbufferStorage.DepthComponent24, width, height);
			GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthRb);
			CheckComplete("MSAA HDR target incomplete.");
			resolveFbo = GL.GenFramebuffer();
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, resolveFbo);
			hdrTexture = NewHdrTexture(width, height);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, hdrTexture, 0);
			CheckComplete("HDR resolve target incomplete.");
		}

		// No MSAA: texture + depth, sampled directly.
		private void BuildSingleTargets(int width, int height)
		{
			targetFbo = GL.GenFramebuffer();
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFbo);
			hdrTexture = NewHdrTexture(width, height);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, hdrTexture, 0);
			depthRb = GL.GenRenderbuffer();
			GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthRb);
			GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, width, height);
			GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthRb);
			CheckComplete("HDR target incomplete.");
		}

		private static void CheckComplete(string message)
		{
			if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
			{
				throw new InvalidOperationException(message);
			}
		}

		// Half-float color texture that End() hands out.
		private static int NewHdrTexture(int width, int height)
		{
			int tex = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2D, tex);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, width, height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.HalfFloat, IntPtr.Zero);
			return tex;
		}

		/// Draw the world here. Screen when HDR is off.
		public void Begin()
		{
			int width = Math.Max(renderer.Screen.Width, 1);
			int height = Math.Max(renderer.Screen.Height, 1);
			int samples = Math.Max(renderer.currentOptions.AntiAliasingLevel, 0);
			if (!Ensure(width, height, samples))
			{
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
				GL.Viewport(0, 0, width, height);
				GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
				return;
			}
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFbo);
			GL.Viewport(0, 0, width, height);
			GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
		}

		/// Resolve, unbind, hand out the HDR texture. 0 when HDR is off.
		public int End()
		{
			if (!Enabled || targetFbo == 0)
			{
				return 0;
			}
			if (resolveFbo != 0)
			{
				GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, targetFbo);
				GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, resolveFbo);
				GL.BlitFramebuffer(0, 0, seenWidth, seenHeight, 0, 0, seenWidth, seenHeight, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
			}
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
			GL.Viewport(0, 0, seenWidth, seenHeight);
			return hdrTexture;
		}

		private void DeleteTargets()
		{
			Quiet(() => GL.DeleteFramebuffer(targetFbo));
			targetFbo = 0;
			Quiet(() => GL.DeleteFramebuffer(resolveFbo));
			resolveFbo = 0;
			Quiet(() => GL.DeleteTexture(hdrTexture));
			hdrTexture = 0;
			Quiet(() => GL.DeleteRenderbuffer(colorRb));
			colorRb = 0;
			Quiet(() => GL.DeleteRenderbuffer(depthRb));
			depthRb = 0;
			seenWidth = -1;
			seenHeight = -1;
			seenSamples = -1;
		}

		private void FullCleanup()
		{
			try
			{
				DeleteTargets();
			}
			catch
			{
				// Context may be gone; handles are dropped anyway.
				targetFbo = 0;
				resolveFbo = 0;
				hdrTexture = 0;
				colorRb = 0;
				depthRb = 0;
			}
		}

		public void Dispose()
		{
			FullCleanup();
		}

		private static void Quiet(Action run)
		{
			try
			{
				run();
			}
			catch
			{
			}
		}
	}
}
