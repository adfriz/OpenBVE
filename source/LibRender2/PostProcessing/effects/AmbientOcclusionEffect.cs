using System;
using OpenBveApi;
using OpenBveApi.Math;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing.Effects
{
	/// <summary>
	/// Single AO slot in the post chain (SAO performance mode + GTAO quality mode).
	/// Scenery-only, half-res, compute GL 4.3 (Windows/Linux); macOS / old GPUs = bypass.
	///
	/// Per-layer (scenery) pipeline, full-res depth in:
	/// linearize (R16F half) + normal-from-depth 4-tap cross (RGBA16F half)
	/// -> SAO spiral volumetric half-res OR GTAO horizon half-res (R16F aoRaw)
	/// -> separable bilateral H (half ping) + V + upsample half-&gt;full (R16F denoised full)
	/// -> in-place composite: color.rgb *= mix(1,ao,Intensity) + ShadowClamp +
	///    DistanceFade + debug AO-only.
	///
	/// Bypass contract (default OFF = pixel-identical): Mode Off, !SupportsCompute,
	/// failed smoke test, failed kernels, or depthTex==0 all fall back to
	/// passthrough (CopyEffect) without breaking the frame. A failed per-frame
	/// dispatch bypasses only that frame (no permanent latch).
	/// </summary>
	public class AmbientOcclusionEffect : IPostEffect
	{
		/// <inheritdoc/>
		public string Id => "AmbientOcclusion";
		/// <inheritdoc/>
		public string Name => "Ambient Occlusion";
		/// <inheritdoc/>
		public bool Enabled { get; set; }
		/// <summary>Early in the chain (AO runs before color-grading style effects).</summary>
		public int Order => 10;
		/// <inheritdoc/>
		public float Intensity { get; set; }

		/// <summary>AO mode, default Off so existing output is untouched.</summary>
		public OpenBveApi.Interface.AmbientOcclusionMode Mode { get; set; }

		/// <summary>AO radius in meters.</summary>
		public float Radius { get; set; }

		/// <summary>AO contrast curve exponent. Range 0.5-3. Cheap uniform.</summary>
		public float Power { get; set; }

		/// <summary>AO depth bias factor (x radius). RETIRED by CACAO port (no uniform reads it); kept for cfg compat.</summary>
		public float Bias { get; set; }

		/// <summary>AO render resolution scale. One of 0.25 / 0.5 / 1.0.</summary>
		public float ResolutionScale { get; set; }

		/// <summary>Whether AO also affects the 3D cab layer (2D cab/HUD always sterile).</summary>
		public bool AffectCab3D { get; set; }

		/// <summary>AO debug view. 0 = composite, 1 = AO-only.</summary>
		public int DebugView { get; set; }

		/// <summary>SAO pattern tap count (each = 2 mirrored samples). Fast 3 / Balanced 5 / Quality 12 via uniform, no recompile.</summary>
		public int SaoSamples { get; set; }

		/// <summary>SAO spiral turns. RETIRED by CACAO port (fixed rotated pattern); kept for cfg compat.</summary>
		public int SaoSpiralTurns { get; set; }

		/// <summary>SAO horizon-angle threshold (CACAO). Range 0-0.2.</summary>
		public float HorizonThreshold { get; set; }

		/// <summary>SAO detail-AO strength from immediate neighbors (CACAO). Range 0-5.</summary>
		public float DetailStrength { get; set; }

		/// <summary>GTAO slice count. Fast 3 / Balanced 4 / Quality 6 via uniform, no recompile. Default Balanced 4.</summary>
		public int GtaoSlices { get; set; }

		/// <summary>GTAO steps per side. Fast 2 / Balanced 3 / Quality 4 via uniform, no recompile. Default Balanced 3.</summary>
		public int GtaoSteps { get; set; }

		/// <summary>GTAO falloff start distance in meters (full kill at 5x). Default 0.4.</summary>
		public float GtaoFalloffRange { get; set; }

		/// <summary>AO bilateral blur radius. Default 2.</summary>
		public int BlurRadius { get; set; }

		/// <summary>AO bilateral depth sharpness. Default 0.01.</summary>
		public float BlurSharpness { get; set; }

		/// <summary>True when the smoke kernel compiled and dispatched.</summary>
		public bool ComputeAvailable => computeReady && smokeShader != null && smokeShader.IsValid;

		/// <summary>True when all four SAO kernels (depth/sao/bilateral/composite) are linked.</summary>
		public bool SaoAvailable => saoReady && ComputeAvailable
			&& depthShader != null && depthShader.IsValid
			&& saoShader != null && saoShader.IsValid
			&& bilateralShader != null && bilateralShader.IsValid
			&& compositeShader != null && compositeShader.IsValid;

		/// <summary>True when all four GTAO kernels (depth/gtao/bilateral/composite) are linked.</summary>
		public bool GtaoAvailable => gtaoReady && ComputeAvailable
			&& depthShader != null && depthShader.IsValid
			&& gtaoShader != null && gtaoShader.IsValid
			&& bilateralShader != null && bilateralShader.IsValid
			&& compositeShader != null && compositeShader.IsValid;

		/// <summary>Half-res AO raw texture handle, 0 when unallocated.</summary>
		public int AoTexture => aoTexture;

	// Composite tuning (no options keys yet; cheap uniforms).
	private const float ShadowClampDefault = 0.3f;
		private const float FadeNearFactor = 0.3f;
		private const float FadeFarFactor = 0.8f;

		private readonly BaseRenderer renderer;
		private ComputeShader smokeShader;
		private ComputeShader depthShader;
		private ComputeShader saoShader;
		private ComputeShader gtaoShader;
		private ComputeShader bilateralShader;
		private ComputeShader compositeShader;
		private CopyEffect copy;
		private int aoTexture;
		private int aoWidth;
		private int aoHeight;
		private SizedInternalFormat aoFormat = SizedInternalFormat.R16f;
		private int linearDepthTexture;
		private int linearWidth;
		private int linearHeight;
		private SizedInternalFormat linearFormat = SizedInternalFormat.R16f;
		private int normalTexture;
		private int normalWidth;
		private int normalHeight;
		private SizedInternalFormat normalFormat = SizedInternalFormat.Rgba16f;
		private int edgesTexture;
		private int edgesWidth;
		private int edgesHeight;
		private SizedInternalFormat edgesFormat = SizedInternalFormat.R8;
		private int aoPingTexture;
		private int aoPingWidth;
		private int aoPingHeight;
		private SizedInternalFormat aoPingFormat = SizedInternalFormat.R16f;
		private int aoDenoisedTexture;
		private int aoDenoisedWidth;
		private int aoDenoisedHeight;
		private SizedInternalFormat aoDenoisedFormat = SizedInternalFormat.R16f;
		private int width;
		private int height;
		private bool computeReady;
		private bool saoReady;
		private bool gtaoReady;
		private bool initialized;
		private bool disposed;

		public AmbientOcclusionEffect(BaseRenderer renderer)
		{
			this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
			Enabled = false;
			Intensity = 1.0f;
		Mode = OpenBveApi.Interface.AmbientOcclusionMode.Off;
		Radius = 1.2f;
		Power = 1.5f;
			Bias = 0.05f;
			ResolutionScale = 0.5f;
			AffectCab3D = true;
			DebugView = 0;
			SaoSamples = 5;
			SaoSpiralTurns = 7;
			HorizonThreshold = 0.06f;
			DetailStrength = 0.5f;
			GtaoSlices = 4;
			GtaoSteps = 3;
			GtaoFalloffRange = 0.4f;
			BlurRadius = 3;
			BlurSharpness = 0.01f;
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
			try
			{
				copy = new CopyEffect(renderer) { Enabled = true };
				copy.Initialize(width, height);
			}
			catch (Exception ex)
			{
				Log($"[AO] Passthrough setup failed, effect disabled: {ex.Message}");
				Enabled = false;
				DisposeAll();
				return;
			}
			initialized = true;
			try
			{
				if (!renderer.Capabilities.SupportsCompute)
				{
					computeReady = false;
					saoReady = false;
					gtaoReady = false;
					return;
				}
				smokeShader = TryLoadKernel("ao_smoke");
				ComputeHalfSize(out int halfW, out int halfH);
				EnsureTexture(ref aoTexture, ref aoWidth, ref aoHeight, ref aoFormat,
					halfW, halfH, SizedInternalFormat.R16f, SizedInternalFormat.R8,
					TextureMinFilter.Nearest, TextureMagFilter.Nearest);
				computeReady = RunSmokeTest() && smokeShader != null && smokeShader.IsValid && aoTexture != 0;
				Log(computeReady
					? "[AO] Compute smoke test passed (ao_smoke.comp dispatch verified)."
					: "[AO] Compute smoke test failed, falling back to bypass.");
				if (!computeReady)
				{
					saoReady = false;
					gtaoReady = false;
					DisposeKernels();
					return;
				}
				depthShader = TryLoadKernel("ao_depth");
				saoShader = TryLoadKernel("ao_sao");
				bilateralShader = TryLoadKernel("bilateral");
				compositeShader = TryLoadKernel("ao_composite");
				EnsureP3Textures();
				bool p3FormatsOk = P3FormatsSupportCompute();
				if (!p3FormatsOk)
				{
					Log("[AO] Texture float fallback in use, compute path unavailable (passthrough).");
				}
				saoReady = depthShader != null && depthShader.IsValid
					&& saoShader != null && saoShader.IsValid
					&& bilateralShader != null && bilateralShader.IsValid
					&& compositeShader != null && compositeShader.IsValid
					&& linearDepthTexture != 0 && normalTexture != 0 && edgesTexture != 0
					&& aoTexture != 0 && aoPingTexture != 0 && aoDenoisedTexture != 0
					&& p3FormatsOk;
				Log(saoReady
					? "[AO] SAO kernels loaded (ao_depth/sao/bilateral/composite, half-res scenery-only)."
					: "[AO] SAO kernel setup incomplete, SAO bypasses to passthrough.");
				if (!saoReady)
				{
				// SAO failed: dispose only the SAO kernel; the shared
				// depth/bilateral/composite kernels stay alive for GTAO.
					SafeDispose(ref saoShader);
				}
				gtaoShader = TryLoadKernel("ao_gtao");
				gtaoReady = gtaoShader != null && gtaoShader.IsValid
					&& depthShader != null && depthShader.IsValid
					&& bilateralShader != null && bilateralShader.IsValid
					&& compositeShader != null && compositeShader.IsValid
					&& linearDepthTexture != 0 && normalTexture != 0 && edgesTexture != 0
					&& aoTexture != 0 && aoPingTexture != 0 && aoDenoisedTexture != 0
					&& p3FormatsOk;
				Log(gtaoReady
					? "[AO] GTAO kernels loaded (ao_depth/gtao/bilateral/composite, half-res scenery-only)."
					: "[AO] GTAO kernel setup incomplete, GTAO bypasses to passthrough.");
				if (!gtaoReady)
				{
					// GTAO failed: dispose only the GTAO kernel; shared SAO stays alive.
					SafeDispose(ref gtaoShader);
				}
			}
			catch (EntryPointNotFoundException ex)
			{
				Log($"[AO] Compute entry point missing, falling back to bypass: {ex.Message}");
				computeReady = false;
				saoReady = false;
				gtaoReady = false;
				DisposeKernels();
			}
			catch (Exception ex)
			{
				Log($"[AO] Compute setup failed, falling back to bypass: {ex.Message}");
				computeReady = false;
				saoReady = false;
				gtaoReady = false;
				DisposeKernels();
			}
		}

		/// <inheritdoc/>
		public void Resize(int width, int height)
		{
			this.width = width;
			this.height = height;
			if (copy != null)
			{
				try
				{
					copy.Resize(width, height);
				}
				catch
				{
					// ignored
				}
			}
			if (!initialized || disposed)
			{
				return;
			}
			try
			{
				EnsureP3Textures();
			}
			catch
			{
				// ignored: stale AO texture just disables the compute path
			}
		}

		/// <inheritdoc/>
		public void Render(int inputTex, int depthTex, int targetFboOrZero)
		{
			if (!Enabled || disposed || inputTex == 0 || copy == null)
			{
				return;
			}
			try
			{
				if (!AffectCab3D && renderer != null && renderer.PostProcessor != null && renderer.PostProcessor.CurrentLayerIsCab)
				{
					PassthroughCopy(inputTex, targetFboOrZero);
					return;
				}
			}
			catch
			{
				// ignored: cab check must not break the frame
			}
			bool isSao = Mode == OpenBveApi.Interface.AmbientOcclusionMode.SAO;
			bool isGtao = Mode == OpenBveApi.Interface.AmbientOcclusionMode.GTAO;
			if ((!isSao && !isGtao) || depthTex == 0)
			{
				PassthroughCopy(inputTex, targetFboOrZero);
				return;
			}
			if (isSao && !SaoAvailable)
			{
				PassthroughCopy(inputTex, targetFboOrZero);
				return;
			}
			if (isGtao && !GtaoAvailable)
			{
				PassthroughCopy(inputTex, targetFboOrZero);
				return;
			}
			try
			{
				ResolveCamera(out Matrix4D proj, out Matrix4D invProj, out float near, out float far);
				ComputeHalfSize(out int halfW, out int halfH);
				EnsureP3Textures();
				if (linearDepthTexture == 0 || normalTexture == 0 || aoTexture == 0
					|| aoPingTexture == 0 || aoDenoisedTexture == 0 || edgesTexture == 0
					|| !P3FormatsSupportCompute())
				{
					PassthroughCopy(inputTex, targetFboOrZero);
					return;
				}
				// Make the previously written scenery depth visible to compute (before the first depth dispatch).
				GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
				DispatchDepth(depthTex, invProj, near, far, halfW, halfH);
				DispatchOcclusion(isGtao, proj, invProj, near, far, halfW, halfH);
				DispatchDenoise(true, halfW, halfH);
				DispatchDenoise(false, halfW, halfH);
				DispatchComposite(inputTex, depthTex, near, far);
			}
			catch (Exception ex)
			{
				// Never break the frame: bypass only this frame (no permanent latch).
				Log($"[AO] {(isGtao ? "GTAO" : "SAO")} dispatch failed, falling back to bypass: {ex.Message}");
				try
				{
					PassthroughCopy(inputTex, targetFboOrZero);
				}
				catch
				{
					// ignored
				}
				return;
			}
			try
			{
				PassthroughCopy(inputTex, targetFboOrZero);
			}
			catch (Exception ex)
			{
				Log($"[AO] Composite blit failed: {ex.Message}");
			}
		}

		/// <inheritdoc/>
		public void SyncFromOptions(BaseOptions options)
		{
			if (options == null || disposed)
			{
				return;
			}
			try
			{
				Mode = options.AoMode;
				Radius = AoLimits.Clamp(options.AoRadius, 0.1f, 5.0f);
				Intensity = AoLimits.Clamp(options.AoIntensity, 0.0f, 2.0f);
				Power = AoLimits.Clamp(options.AoPower, 0.5f, 3.0f);
				Bias = AoLimits.Clamp(options.AoBias, 0.0f, 1.0f);
				ResolutionScale = AoLimits.SnapScale(options.AoResolutionScale);
				AffectCab3D = options.AoAffectCab3D;
				DebugView = options.AoDebugView != 0 ? 1 : 0;
				SaoSamples = AoLimits.Clamp(options.SaoSamples, 1, 32);
				SaoSpiralTurns = AoLimits.Clamp(options.SaoSpiralTurns, 1, 16);
				HorizonThreshold = AoLimits.Clamp(options.AoHorizonThreshold, 0.0f, 0.2f);
				DetailStrength = AoLimits.Clamp(options.AoDetailStrength, 0.0f, 5.0f);
				GtaoSlices = AoLimits.Clamp(options.GtaoSlices, 1, 8);
				GtaoSteps = AoLimits.Clamp(options.GtaoSteps, 1, 8);
				GtaoFalloffRange = AoLimits.Clamp(options.GtaoFalloffRange, 0.05f, 2.0f);
				BlurRadius = AoLimits.Clamp(options.AoBlurRadius, 0, 8);
				BlurSharpness = AoLimits.Clamp(options.AoBlurSharpness, 0.0f, 1.0f);
				Enabled = options.EnablePostProcessing
					&& (Mode == OpenBveApi.Interface.AmbientOcclusionMode.SAO
						|| Mode == OpenBveApi.Interface.AmbientOcclusionMode.GTAO);
			}
			catch
			{
				// ignored: keep previous state on bad options
			}
		}

		private void ComputeHalfSize(out int halfW, out int halfH)
		{
			float scale = ResolutionScale;
			if (scale <= 0.0f) scale = 0.5f;
			halfW = Math.Max(1, (int)(width * scale + 0.5f));
			halfH = Math.Max(1, (int)(height * scale + 0.5f));
		}

		private ComputeShader TryLoadKernel(string name)
		{
			try
			{
				return new ComputeShader(renderer, name);
			}
			catch (Exception ex)
			{
				Log($"[AO] Kernel load failed ({name}): {ex.Message}");
				return null;
			}
		}

		private void EnsureP3Textures()
		{
			ComputeHalfSize(out int halfW, out int halfH);
			EnsureTexture(ref aoTexture, ref aoWidth, ref aoHeight, ref aoFormat,
				halfW, halfH, SizedInternalFormat.R16f, SizedInternalFormat.R8,
				TextureMinFilter.Nearest, TextureMagFilter.Nearest);
			EnsureTexture(ref linearDepthTexture, ref linearWidth, ref linearHeight, ref linearFormat,
				halfW, halfH, SizedInternalFormat.R16f, SizedInternalFormat.R32f,
				TextureMinFilter.Nearest, TextureMagFilter.Nearest);
			EnsureTexture(ref normalTexture, ref normalWidth, ref normalHeight, ref normalFormat,
				halfW, halfH, SizedInternalFormat.Rgba16f, SizedInternalFormat.Rgba8,
				TextureMinFilter.Nearest, TextureMagFilter.Nearest);
			EnsureTexture(ref edgesTexture, ref edgesWidth, ref edgesHeight, ref edgesFormat,
				halfW, halfH, SizedInternalFormat.R8, SizedInternalFormat.R8,
				TextureMinFilter.Nearest, TextureMagFilter.Nearest);
			EnsureTexture(ref aoPingTexture, ref aoPingWidth, ref aoPingHeight, ref aoPingFormat,
				halfW, halfH, SizedInternalFormat.R16f, SizedInternalFormat.R8,
				TextureMinFilter.Linear, TextureMagFilter.Linear);
			EnsureTexture(ref aoDenoisedTexture, ref aoDenoisedWidth, ref aoDenoisedHeight, ref aoDenoisedFormat,
				Math.Max(1, width), Math.Max(1, height), SizedInternalFormat.R16f, SizedInternalFormat.R8,
				TextureMinFilter.Linear, TextureMagFilter.Linear);
		}

		/// <summary>True when no float-texture fallback is in use (compute image layouts need exact formats).</summary>
		private bool P3FormatsSupportCompute()
		{
			return linearFormat == SizedInternalFormat.R16f
				&& normalFormat == SizedInternalFormat.Rgba16f
				&& aoFormat == SizedInternalFormat.R16f
				&& edgesFormat == SizedInternalFormat.R8;
		}

		private void EnsureTexture(ref int handle, ref int curW, ref int curH, ref SizedInternalFormat format,
			int wantW, int wantH, SizedInternalFormat tryFormat, SizedInternalFormat fallbackFormat,
			TextureMinFilter minFilter, TextureMagFilter magFilter)
		{
			wantW = Math.Max(1, wantW);
			wantH = Math.Max(1, wantH);
			if (handle != 0 && wantW == curW && wantH == curH)
			{
				return;
			}
			SafeDeleteTexture(ref handle);
			curW = 0;
			curH = 0;
			int created = AllocateTexture(wantW, wantH, tryFormat, fallbackFormat, minFilter, magFilter, out SizedInternalFormat actual);
			if (created != 0)
			{
				handle = created;
				curW = wantW;
				curH = wantH;
				format = actual;
			}
		}

		private int AllocateTexture(int w, int h, SizedInternalFormat tryFormat, SizedInternalFormat fallbackFormat,
			TextureMinFilter minFilter, TextureMagFilter magFilter, out SizedInternalFormat actualFormat)
		{
			actualFormat = tryFormat;
			int handle = 0;
			try
			{
				handle = GL.GenTexture();
				GL.BindTexture(TextureTarget.Texture2D, handle);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)minFilter);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)magFilter);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
				try
				{
					GL.TexStorage2D(TextureTarget2d.Texture2D, 1, tryFormat, w, h);
					actualFormat = tryFormat;
				}
				catch
				{
					GL.TexStorage2D(TextureTarget2d.Texture2D, 1, fallbackFormat, w, h);
					actualFormat = fallbackFormat;
				}
				GL.BindTexture(TextureTarget.Texture2D, 0);
				return handle;
			}
			catch (Exception ex)
			{
				Log($"[AO] Texture allocation failed ({w}x{h} {tryFormat}): {ex.Message}");
				try
				{
					GL.BindTexture(TextureTarget.Texture2D, 0);
				}
				catch
				{
					// ignored
				}
				if (handle != 0)
				{
					SafeDeleteTexture(ref handle);
				}
				actualFormat = tryFormat;
				return 0;
			}
		}

		private void DispatchCommon(ComputeShader shader, int w, int h, Action bind)
		{
			shader.Use();
			try
			{
				bind?.Invoke();
				ComputeAoDispatcher.GroupsFor(w, h, out int gx, out int gy);
				shader.Dispatch(gx, gy, 1);
			}
			finally
			{
				ComputeAoDispatcher.UnbindImage(ComputeAoDispatcher.AoUnit);
				ComputeAoDispatcher.UnbindImage(ComputeAoDispatcher.NormalImageUnit);
				try
				{
					GL.BindImageTexture(ComputeAoDispatcher.ColorUnit, 0, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba8);
				}
				catch
				{
					// ignored
				}
				ComputeAoDispatcher.UnbindSampler(ComputeAoDispatcher.DepthUnit);
				ComputeAoDispatcher.UnbindSampler(ComputeAoDispatcher.LinearSamplerUnit);
				ComputeAoDispatcher.UnbindSampler(ComputeAoDispatcher.NormalSamplerUnit);
				ComputeAoDispatcher.UnbindSampler(ComputeAoDispatcher.AoSamplerUnit);
				ComputeAoDispatcher.UnbindSampler(ComputeAoDispatcher.EdgesSamplerUnit);
				try
				{
					shader.StopUse();
				}
				catch
				{
					// ignored
				}
				try
				{
					GL.ActiveTexture(TextureUnit.Texture0);
				}
				catch
				{
					// ignored
				}
			}
		}

		private void DispatchDepth(int depthTex, Matrix4D invProj, float near, float far, int halfW, int halfH)
		{
			DispatchCommon(depthShader, halfW, halfH, () =>
			{
				ComputeAoDispatcher.BindSampler(depthShader, "uDepthTex", depthTex, ComputeAoDispatcher.DepthUnit);
				GL.BindImageTexture(ComputeAoDispatcher.AoUnit, linearDepthTexture, 0, false, 0, TextureAccess.WriteOnly, linearFormat);
				GL.BindImageTexture(ComputeAoDispatcher.NormalImageUnit, normalTexture, 0, false, 0, TextureAccess.WriteOnly,
					normalFormat);
				GL.BindImageTexture(ComputeAoDispatcher.EdgesImageUnit, edgesTexture, 0, false, 0, TextureAccess.WriteOnly,
					edgesFormat);
				depthShader.SetFloat("uNear", near);
				depthShader.SetFloat("uFar", far);
				depthShader.SetMatrix4(depthShader.GetCachedLocation("uInvProj"), invProj);
				depthShader.SetVector2(depthShader.GetCachedLocation("uResolution"), halfW, halfH);
			});
		}

		private void DispatchOcclusion(bool isGtao, Matrix4D proj, Matrix4D invProj, float near, float far, int halfW, int halfH)
		{
			ComputeShader shader = isGtao ? gtaoShader : saoShader;
			DispatchCommon(shader, halfW, halfH, () =>
			{
				ComputeAoDispatcher.BindSampler(shader, "uLinearDepthTex", linearDepthTexture, ComputeAoDispatcher.LinearSamplerUnit);
				ComputeAoDispatcher.BindSampler(shader, "uNormalTex", normalTexture, ComputeAoDispatcher.NormalSamplerUnit);
				GL.BindImageTexture(ComputeAoDispatcher.AoUnit, aoTexture, 0, false, 0, TextureAccess.WriteOnly, aoFormat);
				shader.SetFloat("uNear", near);
				shader.SetFloat("uFar", far);
				shader.SetFloat("uRadius", Radius);
				shader.SetFloat("uPower", Power);
				shader.SetMatrix4(shader.GetCachedLocation("uProj"), proj);
				shader.SetMatrix4(shader.GetCachedLocation("uInvProj"), invProj);
				shader.SetVector2(shader.GetCachedLocation("uResolution"), halfW, halfH);
				if (isGtao)
				{
					shader.SetInt("uSlices", GtaoSlices);
					shader.SetInt("uSteps", GtaoSteps);
					shader.SetFloat("uFalloffRange", GtaoFalloffRange);
				}
				else
				{
					shader.SetInt("uSampleCount", SaoSamples);
					shader.SetFloat("uHorizonThreshold", HorizonThreshold);
					shader.SetFloat("uDetailStrength", DetailStrength);
				}
			});
		}

		private void DispatchDenoise(bool horizontal, int halfW, int halfH)
		{
			int inputTex = horizontal ? aoTexture : aoPingTexture;
			int outputTex = horizontal ? aoPingTexture : aoDenoisedTexture;
			SizedInternalFormat outputFormat = horizontal ? aoPingFormat : aoDenoisedFormat;
			int outW = horizontal ? halfW : Math.Max(1, width);
			int outH = horizontal ? halfH : Math.Max(1, height);
			DispatchCommon(bilateralShader, outW, outH, () =>
			{
				ComputeAoDispatcher.BindSampler(bilateralShader, "uValueTex", inputTex, ComputeAoDispatcher.AoSamplerUnit);
				ComputeAoDispatcher.BindSampler(bilateralShader, "uEdgesTex", edgesTexture, ComputeAoDispatcher.EdgesSamplerUnit);
				GL.BindImageTexture(ComputeAoDispatcher.AoUnit, outputTex, 0, false, 0, TextureAccess.WriteOnly, outputFormat);
				bilateralShader.SetVector2(bilateralShader.GetCachedLocation("uResolution"), outW, outH);
				bilateralShader.SetInt("uDirection", horizontal ? 0 : 1);
				bilateralShader.SetInt("uRadius", BlurRadius);
			});
		}

		private void DispatchComposite(int colorTex, int depthTex, float near, float far)
		{
			int fullW = Math.Max(1, width);
			int fullH = Math.Max(1, height);
			DispatchCommon(compositeShader, fullW, fullH, () =>
			{
				GL.BindImageTexture(ComputeAoDispatcher.ColorUnit, colorTex, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba8);
				ComputeAoDispatcher.BindSampler(compositeShader, "uAoTex", aoDenoisedTexture, ComputeAoDispatcher.AoSamplerUnit);
				ComputeAoDispatcher.BindSampler(compositeShader, "uDepthTex", depthTex, ComputeAoDispatcher.DepthUnit);
				compositeShader.SetFloat("uIntensity", Intensity);
				compositeShader.SetFloat("uShadowClamp", ShadowClampDefault);
				compositeShader.SetFloat("uFadeNear", far * FadeNearFactor);
				compositeShader.SetFloat("uFadeFar", far * FadeFarFactor);
				compositeShader.SetFloat("uNear", near);
				compositeShader.SetFloat("uFar", far);
				compositeShader.SetVector2(compositeShader.GetCachedLocation("uResolution"), fullW, fullH);
				compositeShader.SetInt("uDebugView", DebugView);
			});
		}

		private bool RunSmokeTest()
		{
			const int testW = 8;
			const int testH = 8;
			int srcTex = 0;
			int dstTex = 0;
			try
			{
				if (smokeShader == null || !smokeShader.IsValid)
				{
					return false;
				}
				srcTex = GL.GenTexture();
				GL.BindTexture(TextureTarget.Texture2D, srcTex);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
				byte[] white = new byte[testW * testH * 4];
				for (int i = 0; i < white.Length; i++)
				{
					white[i] = 255;
				}
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, testW, testH, 0, PixelFormat.Rgba, PixelType.UnsignedByte, white);
				dstTex = GL.GenTexture();
				GL.BindTexture(TextureTarget.Texture2D, dstTex);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
				GL.TexStorage2D(TextureTarget2d.Texture2D, 1, SizedInternalFormat.R16f, testW, testH);
				GL.BindTexture(TextureTarget.Texture2D, 0);
				DispatchCommon(smokeShader, testW, testH, () =>
				{
					GL.BindImageTexture(ComputeAoDispatcher.ColorUnit, srcTex, 0, false, 0, TextureAccess.ReadOnly, SizedInternalFormat.Rgba8);
					GL.BindImageTexture(ComputeAoDispatcher.AoUnit, dstTex, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.R16f);
				});
				return true;
			}
			catch (Exception ex)
			{
				Log($"[AO] Smoke test exception: {ex.Message}");
				return false;
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
				if (srcTex != 0)
				{
					SafeDeleteTexture(ref srcTex);
				}
				if (dstTex != 0)
				{
					SafeDeleteTexture(ref dstTex);
				}
			}
		}

		private void ResolveCamera(out Matrix4D proj, out Matrix4D invProj, out float near, out float far)
		{
			proj = Matrix4D.Identity;
			invProj = Matrix4D.Identity;
			near = 0.5f;
			far = 600.0f;
			try
			{
				proj = renderer.CurrentProjectionMatrix;
				try
				{
					invProj = Matrix4D.Invert(proj);
				}
				catch
				{
					invProj = Matrix4D.Identity;
				}
				near = (float)renderer.currentOptions.NearClipScenery;
				far = renderer.currentOptions.ViewingDistance;
				try
				{
					// Cab-3D layer uses its own clip range (far 50m, near ~0.025):
					// scenery values would corrupt linearize/radius/fade there.
					if (renderer.PostProcessor != null && renderer.PostProcessor.CurrentLayerIsCab)
					{
						near = (float)renderer.currentOptions.NearClipCab;
						far = 50.0f;
					}
				}
				catch
				{
					// ignored: keep scenery values on bad layer state
				}
			}
			catch
			{
				// ignored: defaults above keep the dispatch valid
			}
		}

		/// <summary>Pixel-identical copy via the shared <see cref="CopyEffect"/>.</summary>
		private void PassthroughCopy(int inputTex, int targetFboOrZero)
		{
			if (copy == null || inputTex == 0)
			{
				return;
			}
			try
			{
				copy.Render(inputTex, 0, targetFboOrZero);
			}
			catch
			{
				// ignored: blit must not throw
			}
		}

		private void Log(string text)
		{
			try
			{
				if (renderer != null && renderer.fileSystem != null)
				{
					renderer.fileSystem.AppendToLogFile(text);
				}
			}
			catch
			{
				// ignored: logging must not throw
			}
		}

		private void SafeDeleteTexture(ref int handle)
		{
			if (handle != 0)
			{
				try
				{
					GL.DeleteTexture(handle);
				}
				catch
				{
					// ignored
				}
				handle = 0;
			}
		}

		private static void SafeDispose<T>(ref T obj) where T : class, IDisposable
		{
			if (obj != null)
			{
				try
				{
					obj.Dispose();
				}
				catch
				{
					// ignored
				}
				obj = null;
			}
		}

		private void DisposeSharedKernels()
		{
			SafeDispose(ref depthShader);
			SafeDispose(ref saoShader);
			SafeDispose(ref bilateralShader);
			SafeDispose(ref compositeShader);
		}

		private void DisposeKernels()
		{
			DisposeSharedKernels();
			SafeDispose(ref gtaoShader);
			SafeDispose(ref smokeShader);
		}

		private void DisposeAll()
		{
			SafeDeleteTexture(ref aoTexture);
			aoWidth = 0;
			aoHeight = 0;
			SafeDeleteTexture(ref linearDepthTexture);
			linearWidth = 0;
			linearHeight = 0;
			SafeDeleteTexture(ref normalTexture);
			normalWidth = 0;
			normalHeight = 0;
			SafeDeleteTexture(ref edgesTexture);
			edgesWidth = 0;
			edgesHeight = 0;
			SafeDeleteTexture(ref aoPingTexture);
			aoPingWidth = 0;
			aoPingHeight = 0;
			SafeDeleteTexture(ref aoDenoisedTexture);
			aoDenoisedWidth = 0;
			aoDenoisedHeight = 0;
			DisposeKernels();
			SafeDispose(ref copy);
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			DisposeAll();
			initialized = false;
			computeReady = false;
			saoReady = false;
			gtaoReady = false;
			GC.SuppressFinalize(this);
		}
	}
}
