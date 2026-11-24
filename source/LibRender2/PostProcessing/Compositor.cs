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
		/// <summary>A target created by a pass, with the scale used to keep it in sync with the window.</summary>
		private sealed class OwnedTarget
		{
			public RenderTarget Target;
			public float ScaleX;
			public float ScaleY;
		}

		private readonly BaseRenderer renderer;
		private readonly List<PostProcessPass> passes = new List<PostProcessPass>();
		private readonly List<RenderTarget> buffers = new List<RenderTarget>();
		private readonly RenderTargetPool pool = new RenderTargetPool();
		private readonly List<RenderTarget> frameTargets = new List<RenderTarget>();
		private readonly List<PostHistory> histories = new List<PostHistory>();
		private readonly List<OwnedTarget> ownedTargets = new List<OwnedTarget>();
		private int frameNumber;
		private int nextTextureUnit = 2;
		private int maxTextureUnits = -1;
		private readonly CopyPass finalPass = new CopyPass();
		private readonly ToneMapPass toneMapPass = new ToneMapPass();
		private SinglePassDownsampler downsampler;
		private CompositorColorMode colorMode = CompositorColorMode.Sdr;
		private int width;
		private int height;
		private bool disposed;
		private bool loggedFrame;

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

		/// <summary>Texture unit reserved for the pass chain's source texture.</summary>
		public const int SourceTextureUnit = 0;
		/// <summary>Texture unit conventionally used for the scene depth texture.</summary>
		public const int DepthTextureUnit = 1;

		/// <summary>Depth texture of the scene target, readable by effects such as depth of field.</summary>
		public int SceneDepthTexture => buffers.Count > 0 ? buffers[0].DepthTexture : 0;

		/// <summary>Frame counter used by temporal effects when advancing their history.</summary>
		public int FrameNumber => frameNumber;

		/// <summary>Whether 16-bit float render targets are usable on this driver.</summary>
		public bool HdrSupported { get; private set; }

		/// <summary>
		/// Builds the mip chain of an SDR RGBA8 target. Uses the GL 4.3 single-pass compute path
		/// when available and glGenerateMipmap on the GL 4.1 baseline or after a driver failure.
		/// </summary>
		public void GenerateMipmaps(RenderTarget target)
		{
			downsampler?.Downsample(target);
		}

		/// <summary>True when mip chains are generated with the GL 4.3 compute path.</summary>
		public bool ComputeDownsamplingAvailable => downsampler != null && downsampler.Available;

		/// <summary>
		/// Reserves consecutive texture units for a pass. Unit 0 is always the chain source and
		/// unit 1 is conventionally depth; this hands out units 2 and up so multi-input effects
		/// cannot silently overwrite each other's samplers.
		/// </summary>
		public int ReserveTextureUnits(int count)
		{
			if (count < 1)
			{
				return SourceTextureUnit;
			}
			if (maxTextureUnits < 0)
			{
				int value;
				GL.GetInteger(GetPName.MaxTextureImageUnits, out value);
				maxTextureUnits = value > 0 ? value : 8;
			}
			if (nextTextureUnit + count > maxTextureUnits)
			{
				renderer.fileSystem?.AppendToLogFile("[PostFX] Texture units exhausted; reusing depth unit.");
				return DepthTextureUnit;
			}
			int first = nextTextureUnit;
			nextTextureUnit += count;
			return first;
		}

		/// <summary>
		/// Creates a render target owned by the compositor. It is resized with the window using
		/// the given scale and disposed with the compositor, so passes only need to keep the handle.
		/// </summary>
		public RenderTarget CreateTarget(float scaleX, float scaleY, RenderTargetFormat format = RenderTargetFormat.Sdr,
			bool withDepth = false, bool mipmaps = false, bool nearest = false)
		{
			scaleX = Math.Max(0.01f, scaleX);
			scaleY = Math.Max(0.01f, scaleY);
			RenderTarget target = new RenderTarget(Scaled(scaleX, width), Scaled(scaleY, height), withDepth, format, mipmaps, nearest);
			ownedTargets.Add(new OwnedTarget { Target = target, ScaleX = scaleX, ScaleY = scaleY });
			return target;
		}

		/// <summary>Releases a target previously returned by <see cref="CreateTarget"/>.</summary>
		public void ReleaseTarget(RenderTarget target)
		{
			if (target == null)
			{
				return;
			}
			for (int i = ownedTargets.Count - 1; i >= 0; i--)
			{
				if (ownedTargets[i].Target == target)
				{
					ownedTargets.RemoveAt(i);
					break;
				}
			}
			target.Dispose();
		}

		/// <summary>
		/// Borrows a target from the shared pool for the current frame. It is returned
		/// automatically at the end of <see cref="EndFrame"/>, so a pass must not keep it
		/// across frames. Use <see cref="CreateTarget"/> for persistent targets.
		/// </summary>
		public RenderTarget AcquireTarget(PostTargetDesc desc)
		{
			RenderTarget target = pool.Acquire(desc, width, height);
			frameTargets.Add(target);
			return target;
		}

		/// <summary>
		/// Gets a persistent two-target history for a temporal effect. The compositor advances
		/// and resizes it each frame; the pass writes <see cref="PostHistory.Current"/> and calls
		/// <see cref="PostHistory.MarkWritten"/> after a successful resolve.
		/// </summary>
		public PostHistory GetHistory(PostTargetDesc desc)
		{
			PostHistory history = new PostHistory(pool, desc, width, height);
			histories.Add(history);
			return history;
		}

		private int Scaled(float scale, int extent)
		{
			return Math.Max(1, (int)Math.Round(extent * scale));
		}

		private int ChainSteps()
		{
			int steps = colorMode == CompositorColorMode.HdrInternal ? 1 : 0;
			for (int i = 0; i < passes.Count; i++)
			{
				if (passes[i].Enabled)
				{
					steps++;
				}
			}
			return steps;
		}

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
			// GLNext defaults to the internal-HDR pipeline when the driver can render RGBA16F.
			// Manual SDR is still available through ColorMode; no buffers exist yet at this point.
			colorMode = HdrSupported ? CompositorColorMode.HdrInternal : CompositorColorMode.Sdr;
			renderer.fileSystem?.AppendToLogFile("[PostFX] Default colour mode: " + colorMode + ".");
			try
			{
				EnsureSceneTarget();
				if (buffers[0] == null || !buffers[0].IsComplete())
				{
					throw new InvalidOperationException("Scene framebuffer is incomplete.");
				}
				// Adding an effect: add a post_*.frag asset, register it as an EmbeddedResource in
				// LibRender2.csproj, then add one line here, e.g.:
				//   passes.Add(new SimpleShaderPass("Brightness", "post_brightness", 0.25f));
				// The RouteViewer F8 tab lists every registered pass automatically.
				passes.Add(new VignettePass());
				passes.Add(new DofPass());
				downsampler?.Dispose();
				downsampler = new SinglePassDownsampler(renderer);
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

		/// <summary>The scene target is persistent; only intermediate targets are pooled.</summary>
		private void EnsureSceneTarget()
		{
			bool hdr = colorMode == CompositorColorMode.HdrInternal;
			if (buffers.Count == 0)
			{
				buffers.Add(new RenderTarget(width, height, true, hdr));
			}
			else if (buffers[0].Hdr != hdr)
			{
				buffers[0].Dispose();
				buffers[0] = new RenderTarget(width, height, true, hdr);
			}
			else
			{
				buffers[0].Resize(width, height);
			}
		}

		/// <summary>Borrows one pooled target per intermediate chain step for this frame.</summary>
		private void AcquireChainTargets()
		{
			// Every step writes to its own target; the final blit always goes straight to screen.
			int needed = ChainSteps();
			PostTargetDesc desc = new PostTargetDesc("chain", RenderTargetFormat.Sdr, PostTargetScale.Full);
			while (buffers.Count - 1 < needed)
			{
				buffers.Add(pool.Acquire(desc, width, height));
			}
			while (buffers.Count - 1 > needed)
			{
				RenderTarget extra = buffers[buffers.Count - 1];
				buffers.RemoveAt(buffers.Count - 1);
				pool.Release(extra);
			}
		}

		/// <summary>Returns every per-frame target to the pool.</summary>
		private void ReleaseFrameTargets()
		{
			for (int i = 1; i < buffers.Count; i++)
			{
				pool.Release(buffers[i]);
			}
			if (buffers.Count > 1)
			{
				buffers.RemoveRange(1, buffers.Count - 1);
			}
			for (int i = 0; i < frameTargets.Count; i++)
			{
				pool.Release(frameTargets[i]);
			}
			frameTargets.Clear();
		}

		/// <summary>Binds the offscreen scene target. Call before the scene is drawn.</summary>
		public void BeginFrame(int frameWidth, int frameHeight)
		{
			if (!Enabled || frameWidth < 1 || frameHeight < 1)
			{
				return;
			}
			// A previous frame might have ended early; never hand out the same target twice.
			ReleaseFrameTargets();
			EnsureSceneTarget();
			if (frameWidth != width || frameHeight != height)
			{
				width = frameWidth;
				height = frameHeight;
				EnsureSceneTarget();
				for (int i = 0; i < passes.Count; i++)
				{
					passes[i].Resize(width, height);
				}
				for (int i = 0; i < ownedTargets.Count; i++)
				{
					OwnedTarget owned = ownedTargets[i];
					owned.Target.Resize(Scaled(owned.ScaleX, width), Scaled(owned.ScaleY, height));
				}
				for (int i = 0; i < histories.Count; i++)
				{
					histories[i].Resize(width, height);
				}
				pool.Trim(width, height);
			}
			frameNumber++;
			for (int i = 0; i < histories.Count; i++)
			{
				histories[i].Advance(frameNumber);
			}
			AcquireChainTargets();
			buffers[0].Bind();
			if (!loggedFrame)
			{
				loggedFrame = true;
				renderer.fileSystem?.AppendToLogFile("[PostFX] Scene target FBO=" + buffers[0].Framebuffer +
					" texture=" + buffers[0].ColorTexture + " targets=" + buffers.Count +
					" pooled=" + frameTargets.Count + " err=" + GL.GetError());
			}
		}

		private void BindScreen()
		{
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
			GL.Viewport(0, 0, width, height);
		}

		/// <summary>
		/// Binds an intermediate target for all but the final chain step. The final step goes
		/// straight to the window, so enabling one effect costs one draw call, not pass + blit.
		/// </summary>
		private void BindChainTarget(bool last, ref int nextBuffer, out RenderTarget destination)
		{
			if (last)
			{
				BindScreen();
				destination = null;
			}
			else
			{
				destination = buffers[nextBuffer++];
				destination.Bind();
			}
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
				// Full-screen passes must not inherit scene depth/cull/blend state: a depth test
				// against a target without a depth attachment, or SrcAlpha blending against an
				// empty destination, silently turns the chain black.
				GL.Disable(EnableCap.DepthTest);
				GL.Disable(EnableCap.CullFace);
				GL.Disable(EnableCap.Blend);
				int nextBuffer = 0;
				RenderTarget current = buffers[nextBuffer++];
				if (colorMode == CompositorColorMode.HdrInternal)
				{
					// Float scene -> display-linear SDR. gamma is encoded in the final blit.
					BindChainTarget(false, ref nextBuffer, out RenderTarget destination);
					toneMapPass.Execute(renderer, current);
					current = destination;
				}
				for (int i = 0; i < passes.Count; i++)
				{
					if (!passes[i].Enabled)
					{
						continue;
					}
					BindChainTarget(false, ref nextBuffer, out RenderTarget destination);
					passes[i].Execute(renderer, current);
					current = destination;
				}
				// Gamma correction is the very last step, after every effect (GPU Gems 3 ch.24).
				finalPass.EncodeGamma = colorMode == CompositorColorMode.HdrInternal;
				finalPass.Execute(renderer, current);
			}
			catch (Exception ex)
			{
				renderer.fileSystem?.AppendToLogFile("[PostFX] Pass failed, presenting source directly: " + ex.Message);
				Enabled = false;
			}
			finally
			{
				ReleaseFrameTargets();
				GL.BindVertexArray(0);
				TextureBinder.ResetActiveUnit();
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
			if (buffers.Count > 0)
			{
				buffers[0].Dispose();
			}
			for (int i = 1; i < buffers.Count; i++)
			{
				pool.Release(buffers[i]);
			}
			buffers.Clear();
			for (int i = 0; i < frameTargets.Count; i++)
			{
				pool.Release(frameTargets[i]);
			}
			frameTargets.Clear();
		}

		public void Dispose()
		{
			if (!disposed)
			{
				enabled = false;
				Available = false;
				downsampler?.Dispose();
				downsampler = null;
				DisposeBuffers();
				for (int i = 0; i < passes.Count; i++)
				{
					passes[i].Dispose();
				}
				passes.Clear();
				for (int i = 0; i < ownedTargets.Count; i++)
				{
					ownedTargets[i].Target.Dispose();
				}
				ownedTargets.Clear();
				for (int i = 0; i < histories.Count; i++)
				{
					histories[i].Dispose();
				}
				histories.Clear();
				pool.Dispose();
				toneMapPass.Dispose();
				finalPass.Dispose();
				disposed = true;
				GC.SuppressFinalize(this);
			}
		}
	}
}
