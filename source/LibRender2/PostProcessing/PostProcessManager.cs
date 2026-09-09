using System;
using System.Collections.Generic;
using LibRender2.PostProcessing.Effects;
using OpenBveApi;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// Owns the stackable post-processing chain (one instance per renderer,
	/// held by BaseRenderer.PostProcessor).
	///
	/// Bypass contract (OFF = pixel-identical):
	/// - <see cref="IsEnabled"/> is false while the master switch is off, no
	///   effect is enabled, resources were disposed, or a GL failure latched
	///   the fallback flag.
	/// - While disabled, <see cref="BeginLayer"/> returns false WITHOUT
	///   binding any FBO, so the caller renders straight to the default
	///   framebuffer exactly as before. <see cref="EndLayerAndComposite"/>
	///   is a no-op in that case.
	///
	/// Post ON vs MSAA: while the chain is active the scene is rendered into
	/// a single-sample FBO, so the MSAA context (GraphicsMode AA samples) no
	/// longer anti-aliases the 3D image inside the chain. Users who want edge
	/// smoothing with post ON should use FXAA instead. OFF bypass renders
	/// straight to the default framebuffer, so MSAA keeps working exactly as
	/// before.
	/// </summary>
	public class PostProcessManager : IDisposable
	{
		private readonly BaseRenderer renderer;
		private readonly List<IPostEffect> effects = new List<IPostEffect>();
		private readonly Dictionary<string, int> csvOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		private string lastCsv;
		private IPostEffect[] sortedCache = new IPostEffect[0];
		private IPostEffect[] activeCache = new IPostEffect[8];
		private bool orderDirty = true;
		private SceneFrameBuffer sceneBuffer;
		private readonly SceneFrameBuffer[] ping = new SceneFrameBuffer[2];
		private bool currentLayerIsCab;
		private bool fallbackDisabled;
		private bool disposed;
		private int width;
		private int height;

		/// <summary>Master switch, default OFF so existing output is untouched.</summary>
		public bool MasterEnabled { get; set; }

		/// <summary>Registered effects (unsorted; chain runs sorted by Order).</summary>
		public IReadOnlyList<IPostEffect> Effects => effects;

		/// <summary>Scene color target used while a layer is captured.</summary>
		public SceneFrameBuffer SceneBuffer => sceneBuffer;

		/// <summary>True while the current layer is the 3D cab (AO honours AffectCab3D itself).</summary>
		public bool CurrentLayerIsCab => currentLayerIsCab;

		/// <summary>True only when a layer must actually be routed through the chain.</summary>
		public bool IsEnabled
		{
			get
			{
				if (!MasterEnabled || fallbackDisabled || disposed)
				{
					return false;
				}
				for (int i = 0; i < effects.Count; i++)
				{
					IPostEffect effect = effects[i];
					if (effect != null && effect.Enabled)
					{
						return true;
					}
				}
				return false;
			}
		}

		public PostProcessManager(BaseRenderer renderer)
		{
			this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
			MasterEnabled = false;
		}

		/// <summary>Registers an effect (duplicates ignored).</summary>
		public void AddEffect(IPostEffect effect)
		{
			if (effect != null && !effects.Contains(effect))
			{
				effects.Add(effect);
				orderDirty = true;
			}
		}

		/// <summary>Unregisters an effect.</summary>
		public void RemoveEffect(IPostEffect effect)
		{
			if (effects.Remove(effect))
			{
				orderDirty = true;
			}
		}

		/// <summary>
		/// Allocates the scene buffer and initializes effects.
		/// Zero sizes are legal (allocation is deferred to the first Resize).
		/// Ping buffers stay lazy: allocated in RenderChain only when 2+ effects run.
		/// </summary>
		public void Initialize(int width, int height)
		{
			this.width = width;
			this.height = height;
			try
			{
				if (sceneBuffer == null)
				{
					sceneBuffer = new SceneFrameBuffer();
				}
				if (width > 0 && height > 0)
				{
					sceneBuffer.Ensure(width, height);
				}
			EnsureSorted();
			for (int i = 0; i < sortedCache.Length; i++)
			{
				try
				{
					sortedCache[i].Initialize(width, height);
				}
				catch
				{
					sortedCache[i].Enabled = false;
				}
			}
		}
			catch
			{
				fallbackDisabled = true;
			}
		}

		/// <summary>
		/// Reallocates for a new viewport size and clears the fallback latch
		/// so a transient failure can recover.
		/// </summary>
		public void Resize(int width, int height)
		{
			this.width = width;
			this.height = height;
			if (disposed)
			{
				return;
			}
			try
			{
				fallbackDisabled = false;
				if (sceneBuffer != null)
				{
					sceneBuffer.Ensure(width, height);
				}
				if (ping[0] != null)
				{
					ping[0].Ensure(width, height);
				}
				if (ping[1] != null)
				{
					ping[1].Ensure(width, height);
				}
				for (int i = 0; i < effects.Count; i++)
				{
					try
					{
						if (effects[i] != null)
						{
							effects[i].Resize(width, height);
						}
					}
					catch
					{
						// ignored: one bad effect must not break resize
					}
				}
			}
			catch
			{
				fallbackDisabled = true;
			}
		}

		/// <summary>
		/// Forwards options to effects and picks up the master switch plus
		/// effect order. PostEffectOrder CSV (e.g. "AmbientOcclusion,FXAA,Sharpen")
		/// sets the chain order by CSV index; effects absent from the CSV keep
		/// their static Order after the listed ones (Copy stays last).
		/// </summary>
		public void SyncFromOptions(BaseOptions options)
		{
			if (options == null)
			{
				return;
			}
			try
			{
				MasterEnabled = options.EnablePostProcessing;
			}
			catch
			{
				// ignored: keep previous master state
			}
			try
			{
				string csv = options.PostEffectOrder;
				if (!string.Equals(csv, lastCsv, StringComparison.OrdinalIgnoreCase))
				{
					lastCsv = csv;
					csvOrder.Clear();
					List<string> ids = BaseOptions.ParsePostEffectOrder(csv);
					for (int i = 0; i < ids.Count; i++)
					{
						csvOrder.Add(ids[i], i);
					}
					orderDirty = true;
				}
			}
			catch
			{
				// ignored: keep previous order on bad options
			}
			for (int i = 0; i < effects.Count; i++)
			{
				try
				{
					if (effects[i] != null)
					{
						effects[i].SyncFromOptions(options);
					}
				}
				catch
				{
					// ignored: one bad effect must not break sync
				}
			}
		}

		/// <summary>
		/// Call before rendering a 3D layer. Returns true when the caller must
		/// render into <see cref="SceneBuffer"/>; false means render directly
		/// to screen (bypass, pixel-identical).
		/// </summary>
		public bool BeginLayer()
		{
			return BeginLayer(false);
		}

		/// <summary>
		/// Cab variant of <see cref="BeginLayer"/>. Sets <see cref="CurrentLayerIsCab"/>
		/// so AO can honour AffectCab3D itself; other effects still run.
		/// </summary>
		public bool BeginCabLayer()
		{
			return BeginLayer(true);
		}

		/// <summary>Shared layer entry; isCabLayer only affects <see cref="CurrentLayerIsCab"/>.</summary>
		public bool BeginLayer(bool isCabLayer)
		{
			if (!IsEnabled)
			{
				return false;
			}
			try
			{
				if (sceneBuffer == null)
				{
					sceneBuffer = new SceneFrameBuffer();
				}
				sceneBuffer.Ensure(width, height);
				if (!sceneBuffer.IsComplete)
				{
					return false;
				}
				currentLayerIsCab = isCabLayer;
				sceneBuffer.Bind();
				GL.Viewport(0, 0, sceneBuffer.Width, sceneBuffer.Height);
				return true;
			}
			catch
			{
				currentLayerIsCab = false;
				fallbackDisabled = true;
				try
				{
					GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
				}
				catch
				{
				}
				return false;
			}
		}

		/// <summary>
		/// Call after a captured layer finished. Composites the chain to the
		/// default framebuffer. No-op unless the matching
		/// <see cref="BeginLayer"/> returned true.
		/// </summary>
		public void EndLayerAndComposite()
		{
			if (!IsEnabled)
			{
				currentLayerIsCab = false;
				return;
			}
			try
			{
				if (sceneBuffer != null)
				{
					sceneBuffer.UnBind();
				}
				RenderChain();
			}
			catch
			{
				fallbackDisabled = true;
				try
				{
					GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
				}
				catch
				{
				}
			}
			finally
			{
				currentLayerIsCab = false;
				try
				{
					GL.Viewport(0, 0, width, height);
				}
				catch
				{
				}
			}
		}

		/// <summary>
		/// Runs enabled effects sorted by effective Order (PostEffectOrder CSV
		/// index when listed, else static Order). One generic src-to-dst loop:
		/// intermediate passes render ping-pong, only the last pass goes to
		/// screen. A single effect renders straight to screen. Effect
		/// exceptions latch bypass via the single boundary catch below.
		/// </summary>
		public void RenderChain()
		{
			try
			{
				EnsureSorted();
				int activeCount = 0;
				for (int i = 0; i < sortedCache.Length; i++)
				{
					IPostEffect effect = sortedCache[i];
					if (effect == null || !effect.Enabled)
					{
						continue;
					}
					if (activeCount == activeCache.Length)
					{
						Array.Resize(ref activeCache, activeCache.Length * 2);
					}
					activeCache[activeCount] = effect;
					activeCount++;
				}
			if (activeCount == 0 || sceneBuffer == null || !sceneBuffer.IsComplete)
			{
				// Nothing to composite (effects disabled mid-frame or no target):
				// blit the captured scene so we never present a cleared/black frame.
				BlitSceneToScreen((sceneBuffer != null ? sceneBuffer.ColorTextureHandle : 0));
				return;
			}
				int input = sceneBuffer.ColorTextureHandle;
				int depth = sceneBuffer.DepthTextureHandle;
				if (input == 0)
				{
					return;
				}
				GL.Viewport(0, 0, width, height);
				bool usePing = activeCount > 1;
				if (usePing)
				{
					// Lazy ping allocation: only when 2+ effects are active.
					if (ping[0] == null)
					{
						ping[0] = new SceneFrameBuffer();
					}
					if (ping[1] == null)
					{
						ping[1] = new SceneFrameBuffer();
					}
					ping[0].Ensure(width, height);
					ping[1].Ensure(width, height);
					usePing = ping[0].IsComplete && ping[1].IsComplete;
				}
			int srcTex = input;
			int write = 0;
			bool screenWritten = false;
			for (int i = 0; i < activeCount; i++)
			{
				bool isLast = i == activeCount - 1;
				int dstFbo = isLast || !usePing ? 0 : ping[write].FboHandle;
				bool ok;
				try
				{
					activeCache[i].Render(srcTex, depth, dstFbo);
					ok = true;
				}
				catch
				{
					ok = false;
					FailEffect(activeCache[i]);
				}
				if (!ok)
				{
					// Keep the previous srcTex: advancing on failure would feed
					// the next pass stale ping contents (ghosting).
					continue;
				}
				if (isLast)
				{
					// The last pass targets the screen (dstFbo == 0), so a
					// visible frame is now guaranteed.
					screenWritten = true;
				}
				else if (usePing)
				{
					int next = ping[write].ColorTextureHandle;
					if (next != 0)
					{
						srcTex = next;
					}
					write ^= 1;
				}
				// Without ping, every pass reads the scene input and composites
				// to screen directly, so srcTex stays at the input.
			}
			if (!screenWritten)
			{
				// The only screen writer failed (or every pass failed): blit
				// the last-good source instead of leaving a cleared/black frame.
				BlitSceneToScreen(srcTex);
			}
			renderer.LastBoundTexture = null;
			renderer.lastVAO = -1;
		}
		catch
		{
			fallbackDisabled = true;
			// Best effort: the scene is already captured in the FBO, so blit it
			// before giving up (never leave a black frame).
			try
			{
				BlitSceneToScreen(sceneBuffer != null ? sceneBuffer.ColorTextureHandle : 0);
			}
			catch
			{
			}
			try
			{
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
			}
			catch
			{
			}
		}
	}

		/// <summary>
	/// Emergency passthrough: copies <paramref name="srcTex"/> (scene or
	/// last-good ping texture) to the default framebuffer so a failed chain
	/// never presents a cleared/black frame. Uses the registered Copy effect
	/// with Enabled forced for the blit; falls back to just binding
	/// framebuffer 0 when Copy is unavailable. Never throws.
	/// </summary>
	private void BlitSceneToScreen(int srcTex)
	{
		try
		{
			if (srcTex == 0)
			{
				try
				{
					GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
				}
				catch
				{
				}
				return;
			}
			try
			{
				GL.Viewport(0, 0, width, height);
			}
			catch
			{
			}
			IPostEffect copy = null;
			for (int i = 0; i < effects.Count; i++)
			{
				if (effects[i] != null && string.Equals(effects[i].Id, "Copy", StringComparison.Ordinal))
				{
					copy = effects[i];
					break;
				}
			}
			if (copy == null)
			{
				try
				{
					GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
				}
				catch
				{
				}
				return;
			}
			bool wasEnabled = copy.Enabled;
			try
			{
				// Copy.Render is a no-op while disabled, so force it for the blit.
				copy.Enabled = true;
				copy.Render(srcTex, 0, 0);
			}
			finally
			{
				try
				{
					copy.Enabled = wasEnabled;
				}
				catch
				{
				}
			}
		}
		catch
		{
			try
			{
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
			}
			catch
			{
			}
		}
	}

		private static void FailEffect(IPostEffect effect)
		{
			try
			{
				if (effect != null)
				{
					effect.Enabled = false;
				}
			}
			catch
			{
				// ignored: disable must not throw
			}
		}

		private void EnsureSorted()
		{
			if (!orderDirty)
			{
				return;
			}
			orderDirty = false;
			List<IPostEffect> sorted = new List<IPostEffect>(effects);
			sorted.Sort((a, b) => EffectiveOrder(a).CompareTo(EffectiveOrder(b)));
			sortedCache = sorted.ToArray();
		}

		private int EffectiveOrder(IPostEffect effect)
		{
			if (effect == null)
			{
				return int.MaxValue;
			}
			int index;
			if (csvOrder.TryGetValue(effect.Id, out index))
			{
				return index;
			}
			return effect.Order;
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			for (int i = 0; i < effects.Count; i++)
			{
				try
				{
					if (effects[i] != null)
					{
						effects[i].Dispose();
					}
				}
				catch
				{
					// ignored
				}
			}
			effects.Clear();
			try
			{
				if (sceneBuffer != null)
				{
					sceneBuffer.Dispose();
				}
			}
			catch
			{
				// ignored
			}
			sceneBuffer = null;
			for (int i = 0; i < ping.Length; i++)
			{
				try
				{
					if (ping[i] != null)
					{
						ping[i].Dispose();
					}
				}
				catch
				{
					// ignored
				}
				ping[i] = null;
			}
			GC.SuppressFinalize(this);
		}
	}
}
