using System;
using OpenBveApi;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing.Effects
{
	/// <summary>
	/// Generic single-pass fragment effect.
	/// FXAA/Sharpen/Vignette share ~95% identical code
	/// (Initialize/Resize/Render/Dispose); only Id/Name/Order/fragName/
	/// defaultIntensity + the options flag differ. Subclasses are ~10 lines.
	/// OFF/disable keeps the manager in bypass (pixel-identical).
	/// </summary>
	public abstract class SinglePassEffect : IPostEffect
	{
		public string Id { get; }
		public string Name { get; }
		public bool Enabled { get; set; }
		public int Order { get; }
		public float Intensity { get; set; }

		private readonly BaseRenderer renderer;
		private readonly string fragName;
		private readonly Func<BaseOptions, bool> syncEnabled;
		private PostEffectShader shader;
		private FullscreenTriangle triangle;
		protected int width;
		protected int height;
		private bool initialized;
		private bool disposed;

		protected SinglePassEffect(BaseRenderer renderer, string id, string name, int order, string fragName, float defaultIntensity, Func<BaseOptions, bool> syncEnabled)
		{
			this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
			Id = id;
			Name = name;
			Order = order;
			this.fragName = fragName;
			Intensity = defaultIntensity;
			this.syncEnabled = syncEnabled;
			Enabled = false;
		}

		/// <inheritdoc/>
		public void Initialize(int width, int height)
		{
			this.width = width;
			this.height = height;
			if (initialized)
			{
				return;
			}
			shader = new PostEffectShader(renderer, fragName);
			triangle = new FullscreenTriangle();
			triangle.Initialize();
			initialized = true;
		}

		/// <inheritdoc/>
		public void Resize(int width, int height)
		{
			this.width = width;
			this.height = height;
		}

		/// <inheritdoc/>
		public void Render(int inputTex, int depthTex, int targetFboOrZero)
		{
			if (!Enabled || !initialized || disposed || inputTex == 0 || shader == null || triangle == null)
			{
				return;
			}
			bool depthWasEnabled = false;
			bool blendWasEnabled = false;
			try
			{
				depthWasEnabled = GL.IsEnabled(EnableCap.DepthTest);
				blendWasEnabled = GL.IsEnabled(EnableCap.Blend);
			}
			catch
			{
				// ignored: state query must not break the blit
			}
		try
		{
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFboOrZero);
			GL.Disable(EnableCap.DepthTest);
			GL.Disable(EnableCap.Blend);
			GL.ActiveTexture(TextureUnit.Texture0);
			GL.BindTexture(TextureTarget.Texture2D, inputTex);
			shader.Activate();
			try
			{
				shader.SetInputTextureUnit(0);
				shader.SetResolution(width, height);
				shader.SetTime(0.0f);
				shader.SetIntensity(Intensity);
				// Harden the present path: an earlier stage (or thread) may have
				// left viewport/program/coherence in a state that silently drops
				// this draw, so set all three explicitly right before drawing.
				GL.Viewport(0, 0, width, height);
				GL.MemoryBarrier(MemoryBarrierFlags.AllBarrierBits);
				GL.UseProgram(shader.Handle);
				if (!TryBlitPresent(inputTex, targetFboOrZero))
				{
					// Fullscreen draws must not inherit a stale scissor rect (e.g. left
					// enabled by loading-screen rendering after a route reload): it would
					// clip the triangle to nothing and present a cleared frame with no
					// error raised. Same save/disable/restore pattern as CopyEffect blit.
					bool scissorWasEnabled = false;
					try
					{
						scissorWasEnabled = GL.IsEnabled(EnableCap.ScissorTest);
					}
					catch
					{
						// ignored: state query must not break the draw
					}
					if (scissorWasEnabled)
					{
						try
						{
							GL.Disable(EnableCap.ScissorTest);
						}
						catch
						{
							// ignored
						}
					}
					try
					{
						triangle.Draw();
					}
					finally
					{
						if (scissorWasEnabled)
						{
							try
							{
								GL.Enable(EnableCap.ScissorTest);
							}
							catch
							{
								// ignored: restore must not throw
							}
						}
					}
				}
			}
				finally
				{
					try
					{
						GL.BindTexture(TextureTarget.Texture2D, 0);
					}
					catch
					{
						// ignored
					}
					shader.Deactivate();
				}
			}
			finally
			{
				try
				{
					if (depthWasEnabled)
					{
						GL.Enable(EnableCap.DepthTest);
					}
					if (blendWasEnabled)
					{
						GL.Enable(EnableCap.Blend);
					}
					GL.ActiveTexture(TextureUnit.Texture0);
				}
				catch
				{
					// ignored: restore must not throw
				}
			}
		}

	// Scene-cache invalidation is owned by PostProcessManager.RenderChain only.

	/// <inheritdoc/>
	public virtual void SyncFromOptions(BaseOptions options)
		{
			if (options == null || disposed)
			{
				return;
			}
			try
			{
				Enabled = options.EnablePostProcessing && (syncEnabled != null && syncEnabled(options));
			}
			catch
			{
				// ignored: keep previous state on bad options
			}
		}

		/// <summary>
		/// Fast-path present hook. Default draws via shader; subclasses may
		/// present via driver blit instead. True = presented, skip the draw.
		/// </summary>
		protected virtual bool TryBlitPresent(int inputTex, int targetFboOrZero)
		{
			return false;
		}

		/// <inheritdoc/>
		public virtual void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			try
			{
				if (triangle != null)
				{
					triangle.Dispose();
				}
			}
			catch
			{
				// ignored
			}
			triangle = null;
			try
			{
				if (shader != null)
				{
					shader.Dispose();
				}
			}
			catch
			{
				// ignored
			}
			shader = null;
			initialized = false;
			GC.SuppressFinalize(this);
		}
	}
}
