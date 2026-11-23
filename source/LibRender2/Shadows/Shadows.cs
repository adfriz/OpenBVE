using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using LibRender2.Objects;
using LibRender2.Shaders;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Textures;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.ShadowMapping
{
	/// <summary>
	/// Manages the Cascaded Shadow Mapping (CSM) system, including depth passes and shader binding.
	/// </summary>
	public class Shadows
	{
		private readonly BaseRenderer renderer;

		/// <summary>The shadow map textures and framebuffers.</summary>
		internal CascadedShadowMap Map;
		/// <summary>The math engine for computing cascade frustums and matrices.</summary>
		internal CascadedShadowCaster Caster;
		/// <summary>The shader used for rendering the shadow depth pass.</summary>
		internal ShadowDepthShader DepthShader;

		/// <summary>Optional GL 4.3 compute shader used to blur caster depth once per cascade.</summary>
		private ComputeShader filterShader;
		private bool computeValidated;
		private bool computeAvailable;
		private const int FilterImageUnit = 0;
		private const int FilterRadius = 1;

		/// <summary>
		/// Manual PCF on a plain depth texture can only average binary comparisons, which gives
		/// visibly quantized (coverage-like) edges. Hardware sampler2DShadow filters the comparison
		/// results bilinearly, so it stays the shadow test until a prefilterable method (e.g. VSM)
		/// is implemented. The compute path is kept here for that future work.
		/// </summary>
		private const bool UseComputeFilter = false;

		/// <summary>Whether shadows are currently active.</summary>
		public bool Enabled;
		/// <summary>The current darkness of the shadows (0.0 to 1.0).</summary>
		public float Strength;

		// Reference tuning for ComputeEffectiveFilterRadius: Medium preset (300m / 3 cascades) with 2048px maps.
		private const double ReferenceDistPerCascade = 100.0;
		private const double ReferenceResolution = 2048.0;
		private const double MinDistFactor = 0.7;
		private const double MaxDistFactor = 1.8;
		private const double MinResFactor = 0.7;
		private const double MaxResFactor = 1.4;
		private const double MinFilterRadius = 0.5;
		private const double MaxFilterRadius = 3.0;
		private const float MinNormalBiasTexels = 0.0f;
		private const float MaxNormalBiasTexels = 4.0f;

		public Shadows(BaseRenderer renderer)
		{
			this.renderer = renderer;
		}

		/// <summary>
		/// Initializes (or reinitializes) shadow mapping from current options.
		/// </summary>
		public void Initialize()
		{
			var opts = renderer.currentOptions;

			if (opts.ShadowResolution == ShadowMapResolution.Off)
			{
				Dispose();
				Enabled = false;
				renderer.fileSystem.AppendToLogFile("[CSM] Shadows disabled by user setting.");
				return;
			}

			int resolution = Math.Max(1, (int)opts.ShadowResolution);
			int cascadeCount = (int)opts.ShadowCascades;
			bool autoResolution = opts.ShadowAutoCascadeResolution;
			double shadowDistance = opts.ShadowDrawDistance == ShadowDistance.ViewingDistance ? opts.ViewingDistance : (double)(int)opts.ShadowDrawDistance;
			shadowDistance = Math.Max(1.0, shadowDistance);
			Strength = (float)opts.ShadowStrength;

			try
			{
				if (Map == null)
				{
					Map = new CascadedShadowMap(cascadeCount, resolution, autoResolution);
				}
				else
				{
					Map.Resize(cascadeCount, resolution, autoResolution);
				}

				if (Caster == null || cascadeCount != Caster.CascadeCount)
				{
					Caster = new CascadedShadowCaster(cascadeCount);
				}

				Caster.ShadowDistance = shadowDistance;
				Caster.Resolution = resolution;
				Caster.Resolutions = Map.Resolutions;
				Caster.SplitLambda = 0.75;
				Caster.DepthMargin = 40.0;

				if (DepthShader == null)
				{
					DepthShader = new ShadowDepthShader(renderer, "shadow_depth", "shadow_depth", true);
				}

				// Hardware PCF is the shadow test until a prefilterable method is validated.
				bool supportsCompute = UseComputeFilter && renderer.Capabilities != null && renderer.Capabilities.IsAtLeast(4, 3);
				if (!computeValidated)
				{
					computeAvailable = supportsCompute && ValidateComputePath();
					computeValidated = true;
				}
				else
				{
					// A runtime failure may have disposed the compute program; keep it disabled
					// for the session, but restore it when shadows are simply toggled back on.
					computeAvailable = supportsCompute && filterShader != null;
				}

				if (computeAvailable)
				{
					Map.SetShadowCompareMode(false);
					Map.EnableFiltering();
					renderer.fileSystem.AppendToLogFile("[CSM] Using GL 4.3 compute depth filter.");
				}
				else
				{
					Map.SetShadowCompareMode(true);
					Map.DisableFiltering();
					renderer.fileSystem.AppendToLogFile("[CSM] Using GL 4.1 hardware PCF path.");
				}

				Enabled = true;
				string cascadeResolutions = string.Join(", ", Map.Resolutions);
				renderer.fileSystem.AppendToLogFile($"[CSM] Initialized: {cascadeCount} cascades [{cascadeResolutions}] (auto={autoResolution}), distance={shadowDistance}m, strength={Strength:P0}");
			}
			catch (Exception ex)
			{
				renderer.fileSystem.AppendToLogFile($"[CSM] Init failed: {ex.Message}");
				Enabled = false;
				GL.GetError();
			}
		}

		/// <summary>
		/// Compiles and dispatches the compute shader once against a 1x1 texture before it is used
		/// in production. Drivers that report 4.3 but fail here are silently routed to 4.1.
		/// </summary>
		[HandleProcessCorruptedStateExceptions]
		private bool ValidateComputePath()
		{
			try
			{
				ComputeShader shader = new ComputeShader("shadow_filter");
				int canary = GL.GenTexture();
				GL.ActiveTexture(TextureUnit.Texture0);
				GL.BindTexture(TextureTarget.Texture2D, canary);
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, 1, 1, 0,
					OpenTK.Graphics.OpenGL.PixelFormat.Red, PixelType.Float, IntPtr.Zero);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
				GL.BindImageTexture(FilterImageUnit, canary, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.R32f);

				shader.Activate();
				shader.SetInt("uRadius", FilterRadius);
				shader.Dispatch(1, 1, 1);
				GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
				ComputeShader.Deactivate();

				GL.BindImageTexture(FilterImageUnit, 0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.R32f);
				GL.BindTexture(TextureTarget.Texture2D, 0);
				ErrorCode error = GL.GetError();
				GL.DeleteTexture(canary);
				if (error != ErrorCode.NoError)
				{
					shader.Dispose();
					renderer.fileSystem.AppendToLogFile("[CSM] Compute canary failed with GL error: " + error);
					return false;
				}
				filterShader = shader;
				return true;
			}
			catch (Exception ex)
			{
				GL.GetError();
				renderer.fileSystem.AppendToLogFile("[CSM] Compute canary unavailable: " + ex.Message);
				return false;
			}
		}

		/// <summary>Blurs each cascade's raw depth on the GPU after the depth pass.</summary>
		private void RunComputeFilter()
		{
			try
			{
				renderer.dummyVao?.Bind();
				filterShader.Activate();
				// Radius 0: copy raw caster depth into the float target. Blurring depth here and
				// comparing once is invalid (average(d<z) != average(d)<z) and produced blobby,
				// displaced edges. The fragment shader now filters the comparison results instead.
				filterShader.SetInt("uRadius", 0);
				int groups = (Map.Resolution + 7) / 8;

				for (int i = 0; i < Map.CascadeCount; i++)
				{
					// uDepth samples unit 0; BindTextureUnit avoids disturbing the active unit.
					TextureBinder.Bind(0, Map.DepthTextures[i]);
					Map.BindFilteredForWriting(i, FilterImageUnit);
					filterShader.Dispatch(groups, groups, 1);
				}
				GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
				GL.BindImageTexture(FilterImageUnit, 0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.R32f);
				ComputeShader.Deactivate();

				ErrorCode error = GL.GetError();
				if (error != ErrorCode.NoError)
				{
					throw new InvalidOperationException("Compute filter reported GL error " + error);
				}
			}
			catch (Exception ex)
			{
				renderer.fileSystem.AppendToLogFile("[CSM] Compute filter failed, reverting to GL 4.1: " + ex.Message);
				DisableComputePath();
			}
		}

		/// <summary>Permanently disables compute for this session and restores hardware PCF state.</summary>
		private void DisableComputePath()
		{
			computeAvailable = false;
			Map?.SetShadowCompareMode(true);
			Map?.DisableFiltering();
			filterShader?.Dispose();
			filterShader = null;
		}

		/// <summary>
		/// Performs the shadow depth pass for all cascades.
		/// </summary>
		public void RenderPass()
		{
			renderer.ShadowDrawCalls = 0;
			renderer.ShadowFallbackDrawCalls = 0;
			renderer.ShadowInstancedDrawCalls = 0;

			if (!Enabled || Map == null || Caster == null || DepthShader == null)
			{
				return;
			}

			// 1. Get light direction pointing FROM the sun TOWARD the scene
			// Sun position is in OpenBVE coordinates (X: right, Y: up, Z: backward)
			// Light direction needs to be negated for some components to match the shadow math.
			Vector3 lightDir = new Vector3(
				-renderer.Lighting.OptionLightPosition.X,
				-renderer.Lighting.OptionLightPosition.Y,
				renderer.Lighting.OptionLightPosition.Z
			);

			if (lightDir.IsNullVector())
			{
				return;
			}

			// 2. Update cascade matrices
			// NOTE: We pass renderer.CurrentViewMatrix here which reflects the camera's rotation.
			// The Caster will use this to align the shadow frustums with the view direction.
			Caster.Resolutions = Map.Resolutions;
			if (renderer.currentOptions.ShadowDrawDistance == ShadowDistance.ViewingDistance)
			{
				Caster.ShadowDistance = renderer.currentOptions.ViewingDistance;
			}
			Caster.Update(lightDir, renderer.CurrentViewMatrix, renderer.CurrentProjectionMatrix, 0.1, renderer.Camera.VerticalViewingAngle, renderer.Screen.AspectRatio);

			// 3. Setup state for depth pass
			renderer.CurrentShader?.Deactivate();
			DepthShader.Activate();
			GL.Enable(EnableCap.DepthTest);
			GL.DepthFunc(DepthFunction.Less);
			if (renderer.OptionBackFaceCulling)
			{
				GL.Enable(EnableCap.CullFace);
				GL.CullFace(CullFaceMode.Front); // OpenBVE culls Front faces by default
			}
			else
			{
				GL.Disable(EnableCap.CullFace);
			}
			GL.DepthMask(true);
			DepthShader.SetTexture(0);

			for (int i = 0; i < Caster.CascadeCount; i++)
			{
				Map.BindCascadeForWriting(i);
				GL.Clear(ClearBufferMask.DepthBufferBit);
				DepthShader.SetLightSpaceMatrix(Caster.LightSpaceMatrices[i]);

				lock (renderer.VisibleObjects.LockObject)
				{
					int lastVAO = -1;
					/*
					 * Culling Per-Cascade:
					 * Distant objects don't need to be rendered into near-field high-res shadow maps.
					 * We use a safety margin (150m) to catch long shadows from tall objects.
					 */
					double maxDistance = renderer.currentOptions.ShadowFilterCascades ? Caster.SplitDistances[i] + 150.0 : double.MaxValue;
					double maxDistanceSquared = maxDistance * maxDistance;

					renderer.RenderShadowFaces(DepthShader, renderer.VisibleObjects.OpaqueFaces, maxDistanceSquared, ref lastVAO);
					renderer.RenderShadowFaces(DepthShader, renderer.VisibleObjects.AlphaFaces, maxDistanceSquared, ref lastVAO);
				}
				Map.Unbind();
			}

			// The GL 4.3 path blurs raw caster depth once per cascade, so the main shader can
			// compare with a single fetch instead of a multi-tap PCF loop for every fragment.
			if (computeAvailable && Map.FilteringEnabled && filterShader != null)
			{
				RunComputeFilter();
			}

			// 4. Restore state
			GL.DepthFunc(DepthFunction.Lequal);
			GL.CullFace(CullFaceMode.Front);
			GL.Viewport(0, 0, renderer.Screen.Width, renderer.Screen.Height);
			// Shadow pass corrupts the GL texture state, so ensure we null it out
			// so the next render pass re-binds what it needs.
			renderer.LastBoundTexture = null;
		}

		/// <summary>
		/// Renders a collection of faces while minimizing VAO and texture state switches.
		/// </summary>
		/// <param name="faces">The faces to render.</param>
		/// <param name="lastVAO">A reference to the last bound VAO handle, to avoid redundant binds.</param>
		private void RenderFaces(IEnumerable<FaceState> faces, ref int lastVAO)
		{
			RenderFacesFiltered(faces, ref lastVAO, double.MaxValue);
		}

		/// <summary>
		/// Renders a collection of faces filtered by distance to optimize per-cascade draw calls.
		/// </summary>
		/// <param name="faces">The faces to render.</param>
		/// <param name="lastVAO">A reference to the last bound VAO handle, to avoid redundant binds.</param>
		/// <param name="maxDistanceSquared">The squared maximum distance from camera for an object to cast shadows into this cascade.</param>
		private void RenderFacesFiltered(IEnumerable<FaceState> faces, ref int lastVAO, double maxDistanceSquared)
		{
			Vector3 cameraPos = renderer.Camera.AbsolutePosition;

			foreach (var face in faces)
			{
				if (face.Object.Prototype.Mesh.VAO == null || face.Object.DisableShadowCasting)
				{
					continue;
				}

				ObjectState state = face.Object;

				// Per-cascade distance culling
				if (maxDistanceSquared < double.MaxValue)
				{
					double dx = state.WorldPosition.X - cameraPos.X;
					double dy = state.WorldPosition.Y - cameraPos.Y;
					double dz = state.WorldPosition.Z - cameraPos.Z;

					if (dx * dx + dy * dy + dz * dz > maxDistanceSquared)
					{
						continue;
					}
				}

				DepthShader.SetModelMatrix(state.ModelMatrix * renderer.Camera.TranslationMatrix);
				DepthShader.SetTextureMatrix(state.TextureTranslation);

				var material = face.Object.Prototype.Mesh.Materials[face.Face.Material];
				if ((material.Flags & MaterialFlags.NoShadow) != 0 || material.BlendMode == MeshMaterialBlendMode.Additive)
				{
					continue;
				}
				if (material.DaytimeTexture != null && renderer.currentHost.LoadTexture(ref material.DaytimeTexture, (OpenGlTextureWrapMode)(material.WrapMode ?? OpenGlTextureWrapMode.ClampClamp)))
				{
					TextureBinder.Bind(0, material.DaytimeTexture.OpenGlTextures[(int)(material.WrapMode ?? OpenGlTextureWrapMode.ClampClamp)].Name);
					DepthShader.SetHasTexture(true);
				}
				else
				{
					DepthShader.SetHasTexture(false);
				}

				DepthShader.SetAlphaCutoff(0.5f);
				DepthShader.SetMaterialAlpha(material.Color.A / 255.0f);
				DepthShader.SetMaterialFlags(material.Flags);
				
				if (state.Matricies != null && state.Matricies.Length > 0)
				{
					DepthShader.SetCurrentAnimationMatricies(state);
					GL.BindBufferBase(BufferTarget.UniformBuffer, 0, state.MatrixBufferIndex);
				}

				VertexArrayObject vao = (VertexArrayObject)face.Object.Prototype.Mesh.VAO;
				if (vao.handle != lastVAO)
				{
					vao.Bind();
					lastVAO = vao.handle;
				}
				if (renderer.OptionBackFaceCulling)
				{
					if ((face.Face.Flags & FaceFlags.Face2Mask) != 0)
					{
						// Double-sided faces (Face2) must not be culled to ensure they cast shadows from both sides
						GL.Disable(EnableCap.CullFace);
					}
					else
					{
						GL.Enable(EnableCap.CullFace);
					}
				}
				PrimitiveType drawMode = renderer.GetPrimitiveType(face.Face.Flags);
				vao.Draw(drawMode, face.Face.IboStartIndex, face.Face.Vertices.Length);
			}
		}

		/// <summary>
		/// Binds shadow data to the main scene shader.
		/// </summary>
		public void Bind(Shader shader)
		{
			if (!Enabled || Map == null || Caster == null)
			{
				shader.SetShadowEnabled(false);
				shader.SetShadowFiltered(false);
				// To satisfy strict OpenGL drivers, always bind something to the shadow map units
				// even if shadow is disabled, to avoid "sampler collision" errors.
				for (int i = 0; i < 4; i++)
				{
					TextureBinder.Bind(4 + i, renderer.nullDepthMap);
					TextureBinder.Bind(8 + i, 0);
				}
				TextureBinder.ResetActiveUnit();
				return;
			}

			shader.Activate();
			shader.SetShadowEnabled(true);
			shader.SetShadowStrength((float)renderer.currentOptions.ShadowStrength);
			shader.SetShadowSmooth(renderer.currentOptions.ShadowSmooth);
			shader.SetShadowFilterRadius(ComputeEffectiveFilterRadius());
			shader.SetCurrentViewMatrix(renderer.CurrentViewMatrix);

			if (computeAvailable && Map.FilteringEnabled)
			{
				// The depth textures have compare mode disabled for raw reads in the compute pass,
				// so the legacy sampler2DShadow uniforms must not reference them.
				for (int i = 0; i < 4; i++)
				{
					TextureBinder.Bind(4 + i, renderer.nullDepthMap);
				}
				TextureBinder.ResetActiveUnit();
				shader.SetShadowFiltered(true);
				Map.BindFilteredForReading(TextureUnit.Texture8);
			}
			else
			{
				Map.BindAllCascadesForReading(TextureUnit.Texture4);
				shader.SetShadowFiltered(false);
				for (int i = 0; i < 4; i++)
				{
					TextureBinder.Bind(8 + i, 0);
				}
				TextureBinder.ResetActiveUnit();
			}

			int cascadeCount = Caster.CascadeCount;
			// Normal bias is in Unity-style texel units (typical 0.3-1.0). Clamp so a stale
			// config (old 2.0x multiplier default) can't detach shadows catastrophically.
			float normalBiasTexels = Math.Max(MinNormalBiasTexels, Math.Min(MaxNormalBiasTexels, (float)renderer.currentOptions.ShadowNormalBias));
			for (int i = 0; i < cascadeCount; i++)
			{
				shader.SetCascadeLightSpaceMatrix(i, Caster.LightSpaceMatrices[i]);
				shader.SetCascadeShadowMapUnit(i, 4 + i);
				// Split distance = the view-space Z where this cascade ends.
				shader.SetShadowSplitDistance(i, (float)Caster.SplitDistances[i]);
				shader.SetCascadeBias(i, Caster.CascadeBiases[i] + (float)renderer.currentOptions.ShadowBias);
				shader.SetNormalBias(i, normalBiasTexels);
				shader.SetTexelWorldSize(i, Caster.TexelWorldSizes[i]);
			}

			for (int i = cascadeCount; i < 4; i++)
			{
				shader.SetShadowSplitDistance(i, 0.0f);
				shader.SetTexelWorldSize(i, cascadeCount > 0 ? Caster.TexelWorldSizes[cascadeCount - 1] : 0.05f);
			}
			shader.SetShadowCascadeCount(cascadeCount);
		}

		/// <summary>
		/// Auto-scales the filter radius by cascade count, shadow distance and resolution
		/// so softness stays perceptually consistent: far distance / few cascades / high res → slightly larger texel radius.
		/// </summary>
		private float ComputeEffectiveFilterRadius()
		{
			double baseRadius = renderer.currentOptions.ShadowFilterRadius;

			double shadowDist = renderer.currentOptions.ShadowDrawDistance == ShadowDistance.ViewingDistance
				? renderer.currentOptions.ViewingDistance
				: (double)(int)renderer.currentOptions.ShadowDrawDistance;
			int optCascadeCount = Math.Max(1, (int)renderer.currentOptions.ShadowCascades);
			int res = Math.Max(512, (int)renderer.currentOptions.ShadowResolution);

			double actualDistPerCascade = shadowDist / optCascadeCount;
			double distFactor = Math.Sqrt(actualDistPerCascade / ReferenceDistPerCascade);
			distFactor = Math.Max(MinDistFactor, Math.Min(MaxDistFactor, distFactor));

			double resFactor = Math.Sqrt((double)res / ReferenceResolution);
			resFactor = Math.Max(MinResFactor, Math.Min(MaxResFactor, resFactor));

			double effectiveRadius = baseRadius * distFactor * resFactor;
			effectiveRadius = Math.Max(MinFilterRadius, Math.Min(MaxFilterRadius, effectiveRadius));
			return (float)effectiveRadius;
		}

		/// <summary>
		/// Disposes shadow resources.
		/// </summary>
		public void Dispose()
		{
			filterShader?.Dispose();
			filterShader = null;
			computeAvailable = false;
			Map?.Dispose();
			Map = null;
			DepthShader?.Dispose();
			DepthShader = null;
			Caster = null;
			Enabled = false;
		}
	}
}
