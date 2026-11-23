using System;
using System.Collections.Generic;
using LibRender2.PostProcessing.Passes;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>Colour format used for the scene inside the compositor.</summary>
	public enum CompositorColorMode
	{
		/// <summary>Current behaviour: scene rendered into an 8-bit target.</summary>
		Sdr,
		/// <summary>Scene rendered into a 16-bit float target, then tone-mapped down to SDR.</summary>
		HdrInternal
	}

	/// <summary>
	/// Owns the offscreen scene target and an ordered chain of full-screen post-processing passes.
	/// Backend-agnostic from the point of view of the rest of the renderer: hosts only call
	/// <see cref="BeginFrame"/> / <see cref="EndFrame"/> around their scene drawing. When disabled,
	/// both calls are no-ops and rendering goes directly to the window as before.
	/// </summary>
	public sealed class Compositor : IDisposable
	{
		private readonly BaseRenderer renderer;
		private readonly List<PostProcessPass> passes = new List<PostProcessPass>();
		private readonly List<RenderTarget> buffers = new List<RenderTarget>();
		private readonly CopyPass finalPass = new CopyPass();
		private readonly ToneMapPass toneMapPass = new ToneMapPass();
		private CompositorColorMode colorMode = CompositorColorMode.Sdr;
		private int width;
		private int height;
		private bool disposed;

		/// <summary>Whether the compositor's targets were created successfully.</summary>
		public bool Available { get; private set; }

		private bool enabled;

		/// <summary>
		/// Off by default so existing rendering (including window MSAA) is unchanged. Set to true
		/// to route the scene through the pass chain; ignored when <see cref="Available"/> is false.
		/// </summary>
		public bool Enabled
		{
			get { return enabled; }
			set { enabled = value && Available; }
		}

		/// <summary>The ordered pass chain. Insert plugins/effects here.</summary>
		public IList<PostProcessPass> Passes => passes;

		/// <summary>Whether 16-bit float render targets are usable on this driver.</summary>
		public bool HdrSupported { get; private set; }

		/// <summary>
		/// Switches between the current SDR pipeline and internal HDR. Switching is a no-op
		/// (with a log message) when HDR targets are unsupported - it never leaves a broken state.
		/// </summary>
		public CompositorColorMode ColorMode
		{
			get { return colorMode; }
			set
			{
				if (value == colorMode)
				{
					return;
				}
				if (value == CompositorColorMode.HdrInternal && !HdrSupported)
				{
					renderer.fileSystem?.AppendToLogFile("[PostFX] HDR targets unsupported; staying in SDR.");
					return;
				}
				colorMode = value;
				DisposeBuffers();
				renderer.fileSystem?.AppendToLogFile("[PostFX] Colour mode: " + colorMode + ".");
			}
		}

		/// <summary>Exposure applied by the internal-HDR tone map. 1.0 is neutral.</summary>
		public float Exposure
		{
			get { return toneMapPass.Exposure; }
			set { toneMapPass.Exposure = value; }
		}

		internal Compositor(BaseRenderer renderer)
		{
			this.renderer = renderer;
		}

		public void Initialize()
		{
			if (renderer.Capabilities == null || !renderer.Capabilities.IsAtLeast(3, 0))
			{
				renderer.fileSystem?.AppendToLogFile("[PostFX] Compositor unavailable: GL 3.0+ required.");
				return;
			}

			width = Math.Max(1, renderer.Screen.Width);
			height = Math.Max(1, renderer.Screen.Height);
			HdrSupported = DetectHdrSupport();
			renderer.fileSystem?.AppendToLogFile("[PostFX] Internal HDR targets " + (HdrSupported ? "available." : "unavailable - SDR only."));
			try
			{
				EnsureBuffers();
				if (buffers[0] == null || !buffers[0].IsComplete())
				{
					throw new InvalidOperationException("Scene framebuffer is incomplete.");
				}
				passes.Add(new VignettePass());
				Available = true;
				Enabled = false;
				renderer.fileSystem?.AppendToLogFile("[PostFX] Compositor ready (" + passes.Count + " registered pass(es), disabled by default).");
			}
			catch (Exception ex)
			{
				renderer.fileSystem?.AppendToLogFile("[PostFX] Compositor unavailable: " + ex.Message);
				DisposeBuffers();
				Available = false;
				enabled = false;
			}
		}

		/// <summary>Probes RGBA16F renderability with a 1x1 framebuffer. Never throws.</summary>
		private static bool DetectHdrSupport()
		{
			try
			{
				int texture = GL.GenTexture();
				int framebuffer = GL.GenFramebuffer();
				GL.BindTexture(TextureTarget.Texture2D, texture);
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, 1, 1, 0,
					OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
				GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
					TextureTarget.Texture2D, texture, 0);
				bool complete = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete;
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
				GL.BindTexture(TextureTarget.Texture2D, 0);
				GL.DeleteFramebuffer(framebuffer);
				GL.DeleteTexture(texture);
				return complete && GL.GetError() == ErrorCode.NoError;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>Allocates the scene target plus one target per active chain step.</summary>
		private void EnsureBuffers()
		{
			int needed = 1 + (colorMode == CompositorColorMode.HdrInternal ? 1 : 0);
			for (int i = 0; i < passes.Count; i++)
			{
				if (passes[i].Enabled)
				{
					needed++;
				}
			}
			while (buffers.Count < needed)
			{
				bool isScene = buffers.Count == 0;
				buffers.Add(new RenderTarget(width, height, isScene,
					isScene && colorMode == CompositorColorMode.HdrInternal));
			}
			for (int i = 0; i < buffers.Count; i++)
			{
				buffers[i].Resize(width, height);
			}
		}

		/// <summary>Binds the offscreen scene target. Call before the scene is drawn.</summary>
		public void BeginFrame(int frameWidth, int frameHeight)
		{
			if (!Enabled || frameWidth < 1 || frameHeight < 1)
			{
				return;
			}
			// Rebuild after a colour-mode switch or a change in the enabled pass set.
			EnsureBuffers();
			if (frameWidth != width || frameHeight != height)
			{
				width = frameWidth;
				height = frameHeight;
				EnsureBuffers();
				for (int i = 0; i < passes.Count; i++)
				{
					passes[i].Resize(width, height);
				}
			}
			buffers[0].Bind();
		}

		/// <summary>Runs the pass chain and presents the result to the default framebuffer.</summary>
		public void EndFrame()
		{
			if (!Enabled)
			{
				return;
			}

			bool depthTest = GL.IsEnabled(EnableCap.DepthTest);
			bool cullFace = GL.IsEnabled(EnableCap.CullFace);
			bool blend = GL.IsEnabled(EnableCap.Blend);
			try
			{
				// Passes may have been enabled/disabled since the last frame.
				EnsureBuffers();
				int nextBuffer = 0;
				RenderTarget current = buffers[nextBuffer++];
				if (colorMode == CompositorColorMode.HdrInternal)
				{
					// Float scene -> tone-mapped 8-bit target. Everything after this is SDR.
					RenderTarget destination = buffers[nextBuffer++];
					destination.Bind();
					toneMapPass.Execute(renderer, current);
					current = destination;
				}
				for (int i = 0; i < passes.Count; i++)
				{
					if (!passes[i].Enabled)
					{
						continue;
					}
					RenderTarget destination = buffers[nextBuffer++];
					destination.Bind();
					passes[i].Execute(renderer, current);
					current = destination;
				}

				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
				GL.Viewport(0, 0, width, height);
				GL.Disable(EnableCap.DepthTest);
				GL.Disable(EnableCap.CullFace);
				GL.Disable(EnableCap.Blend);
				finalPass.Execute(renderer, current);
			}
			catch (Exception ex)
			{
				renderer.fileSystem?.AppendToLogFile("[PostFX] Pass failed, presenting source directly: " + ex.Message);
				Enabled = false;
			}
			finally
			{
				GL.BindVertexArray(0);
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
				GL.Viewport(0, 0, width, height);
				if (depthTest) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
				if (cullFace) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
				if (blend) GL.Enable(EnableCap.Blend); else GL.Disable(EnableCap.Blend);
				renderer.lastVAO = -1;
				renderer.LastBoundTexture = null;
			}
		}

		private void DisposeBuffers()
		{
			for (int i = 0; i < buffers.Count; i++)
			{
				buffers[i].Dispose();
			}
			buffers.Clear();
		}

		public void Dispose()
		{
			if (!disposed)
			{
				enabled = false;
				Available = false;
				DisposeBuffers();
				for (int i = 0; i < passes.Count; i++)
				{
					passes[i].Dispose();
				}
				passes.Clear();
				toneMapPass.Dispose();
				finalPass.Dispose();
				disposed = true;
				GC.SuppressFinalize(this);
			}
		}
	}
}
