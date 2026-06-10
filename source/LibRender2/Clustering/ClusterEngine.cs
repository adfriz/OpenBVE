using System;
using LibRender2.Clustering;
using LibRender2.openGL;
using LibRender2.Shaders;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OTKVec3 = OpenTK.Vector3;
using OTKVec4 = OpenTK.Vector4;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Clustering
{
	/// <summary>
	/// Orchestrates Clustered Forward Rendering (CFR).
	/// Mirrors the Shadows.cs lifecycle: Initialize / CullAndUpload / BindToShader / Dispose.
	///
	/// Opsi B — Single class, runtime GL capability detection:
	///   UseComputePath=true  → SSBO + compute shader (GL 4.3+ or ARB extension)
	///   UseComputePath=false → CPU sphere-AABB cull + existing SetDynamicLights() UBO path
	/// </summary>
	public class ClusterEngine
	{
		private readonly BaseRenderer renderer;
		private ClusterGrid grid;

		// --- Compute path (GL 4.3+ or ARB) ---
		private ClusterCullingShader cullingShader;                       // null if !UseComputePath
		private ShaderStorageBufferObject<GpuLight> lightDataSSBO;        // binding 2: all lights
		private ShaderStorageBufferObject<GpuCluster> lightGridSSBO;      // binding 0: offset+count per cluster
		private ShaderStorageBufferObject<uint> lightIndexSSBO;           // binding 1: flat light index list
		private ShaderStorageBufferObject<GpuClusterAABB> clusterAABBSSBO;// binding 3: precomputed cluster AABBs

		// --- Cached GpuLight conversion array (avoids per-frame heap alloc) ---
		private GpuLight[] gpuLightCache;

		// --- State ---
		/// <summary>Whether CFR is active. False if init failed or disabled by options.</summary>
		public bool Enabled;

		/// <summary>
		/// True if using the SSBO + compute shader path (GL 4.3+ or ARB).
		/// False if using the CPU cull + existing SetDynamicLights() UBO path (max 128 lights).
		/// Set by Initialize() after GL capability detection.
		/// </summary>
		public bool UseComputePath;

		/// <summary>Number of lights submitted in the last CullAndUpload() call (debug HUD).</summary>
		public int LastFrameLightCount;

		private const int MaxLightsCompute = 1024;
		private const int MaxLightIndexes  = 3072 * 64; // clusters × max lights per cluster

		public ClusterEngine(BaseRenderer renderer)
		{
			this.renderer = renderer;
		}

		// -------------------------------------------------------------------------
		// Initialize
		// -------------------------------------------------------------------------

		/// <summary>
		/// Detects GL capabilities and initializes the appropriate code path.
		/// Must be called after the GL context is fully created.
		/// </summary>
		public void Initialize()
		{
			// Guard: legacy renderer or disabled by user option
			if (!renderer.AvailableNewRenderer)
			{
				renderer.fileSystem.AppendToLogFile("[CFR] Skipped: legacy renderer active.");
				Enabled = false;
				return;
			}

			if (renderer.currentOptions.DynamicLightLimit <= 0)
			{
				renderer.fileSystem.AppendToLogFile("[CFR] Skipped: DynamicLightLimit is 0.");
				Enabled = false;
				return;
			}

			try
			{
				// --- Runtime GL capability detection (Opsi B) ---
				string extensions = GL.GetString(StringName.Extensions) ?? string.Empty;
				int major = GL.GetInteger(GetPName.MajorVersion);
				int minor = GL.GetInteger(GetPName.MinorVersion);
				bool gl43Plus = major > 4 || (major == 4 && minor >= 3);

				bool hasSSBO = gl43Plus
					|| extensions.Contains("GL_ARB_shader_storage_buffer_object");
				bool hasCompute = gl43Plus
					|| extensions.Contains("GL_ARB_compute_shader");

				UseComputePath = hasSSBO && hasCompute;

				renderer.fileSystem.AppendToLogFile(
					$"[CFR] GL {major}.{minor} | SSBO={hasSSBO} Compute={hasCompute} → UseComputePath={UseComputePath}");

				grid = new ClusterGrid();
				gpuLightCache = new GpuLight[MaxLightsCompute];

				if (UseComputePath)
				{
					// --- Full compute path: allocate SSBOs ---
					lightDataSSBO        = new ShaderStorageBufferObject<GpuLight>(2);
					lightGridSSBO        = new ShaderStorageBufferObject<GpuCluster>(0);
					lightIndexSSBO       = new ShaderStorageBufferObject<uint>(1);
					clusterAABBSSBO      = new ShaderStorageBufferObject<GpuClusterAABB>(3);

					// Pre-allocate GPU storage for write targets
					lightGridSSBO.Allocate(grid.TotalClusters);
					lightIndexSSBO.Allocate(MaxLightIndexes);

					cullingShader = new ClusterCullingShader(renderer);
					cullingShader.Initialize();

					renderer.fileSystem.AppendToLogFile(
						$"[CFR] Initialized: {grid.NumX}x{grid.NumY}x{grid.NumZ} grid, {MaxLightsCompute} max lights (Compute path)");
				}
				else
				{
					// --- Fallback: CPU cull + existing SetDynamicLights() UBO ---
					// No new GPU resources needed — reuses existing uniform path.
					renderer.fileSystem.AppendToLogFile(
						"[CFR] Initialized: CPU cull fallback, using existing SetDynamicLights() (UBO path)");
				}

				Enabled = true;
			}
			catch (Exception ex)
			{
				renderer.fileSystem.AppendToLogFile($"[CFR] Init failed: {ex.Message}");
				Enabled = false;
				GL.GetError(); // clear GL error state
			}
		}

		// -------------------------------------------------------------------------
		// RebuildClusters
		// -------------------------------------------------------------------------

		/// <summary>
		/// Recomputes cluster AABBs from the current camera projection parameters.
		/// Call when projection matrix changes (FOV, resolution, near/far change).
		/// </summary>
		public void RebuildClusters()
		{
			if (!Enabled || grid == null) return;

			try
			{
				float near   = (float)renderer.currentOptions.NearClipScenery;
				float far    = (float)renderer.currentOptions.ViewingDistance;
				float fovY   = (float)renderer.Camera.VerticalViewingAngle;
				float aspect = (float)renderer.Screen.AspectRatio;

				grid.RebuildAABBs(near, far, fovY, aspect);

				if (UseComputePath && clusterAABBSSBO != null)
				{
					clusterAABBSSBO.Upload(grid.ClusterAABBs);
				}
			}
			catch (Exception ex)
			{
				renderer.fileSystem.AppendToLogFile($"[CFR] RebuildClusters failed: {ex.Message}");
			}
		}

		// -------------------------------------------------------------------------
		// CullAndUpload
		// -------------------------------------------------------------------------

		/// <summary>
		/// Converts ActiveSceneLights to GPU format, culls, and uploads.
		/// Call once per frame before BindToShader().
		/// </summary>
		/// <param name="viewMatrix">Current camera view matrix for light position transform.</param>
		public void CullAndUpload(Matrix4D viewMatrix)
		{
			if (!Enabled) return;

			var lights = renderer.ActiveSceneLights;
			int count  = System.Math.Min(lights.Count, MaxLightsCompute);
			LastFrameLightCount = count;

			if (count == 0) return;

			// --- Warn if cap exceeded ---
			if (lights.Count > MaxLightsCompute)
			{
				renderer.fileSystem.AppendToLogFile(
					$"[CFR] WARNING: Light cap ({MaxLightsCompute}) reached. {lights.Count - MaxLightsCompute} lights dropped.");
			}

			// --- Convert SceneLight → GpuLight (view-space transform) ---
			Matrix4D lightViewMatrix = renderer.Camera.TranslationMatrix * viewMatrix;
			for (int i = 0; i < count; i++)
			{
				SceneLight sl = lights[i];

				// Transform position to view space
				Vector3 viewPos = sl.Position;
				viewPos.Transform(lightViewMatrix, false);

				// Transform spot direction to view space
				Vector3 viewDir = sl.Direction;
				viewDir.Transform(lightViewMatrix, true);
				viewDir.Normalize();

				gpuLightCache[i] = new GpuLight
				{
					PositionAndRange   = new OTKVec4((float)viewPos.X, (float)viewPos.Y, (float)viewPos.Z, sl.Range),
					ColorAndIntensity  = new OTKVec4(sl.Color.R, sl.Color.G, sl.Color.B,
					                                 sl.Power * (float)System.Math.Pow(2.0, sl.Exposure)),
					DirectionAndCutoff = new OTKVec4((float)viewDir.X, (float)viewDir.Y, (float)viewDir.Z, sl.SpotCutoff),
					Params             = new OTKVec4(
					                         (float)sl.Type,
					                         sl.SoftFalloff ? 1.0f : 0.0f,
					                         sl.Softness,
					                         sl.RangeSquared)
				};
			}

			if (UseComputePath)
			{
				// Upload light data → SSBO binding 2
				// Slice array to actual count to avoid uploading garbage
				GpuLight[] slice = new GpuLight[count];
				Array.Copy(gpuLightCache, slice, count);
				lightDataSSBO.Upload(slice);

				// Dispatch compute shader to fill lightGridSSBO + lightIndexSSBO
				lightDataSSBO.Bind();
				lightGridSSBO.Bind();
				lightIndexSSBO.Bind();
				clusterAABBSSBO.Bind();

				cullingShader.Activate();
				cullingShader.SetLightCount(count);
				cullingShader.Dispatch(grid.NumX, grid.NumY, grid.NumZ);

				// Ensure SSBO writes are visible to fragment shader
				GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
			}
			// Fallback: SetDynamicLights() is called separately by BaseRenderer per-object.
			// No additional work needed here — existing UBO path handles it.
		}

		// -------------------------------------------------------------------------
		// BindToShader
		// -------------------------------------------------------------------------

		/// <summary>
		/// Binds cluster data to the main scene shader for the frame.
		/// </summary>
		public void BindToShader(Shader shader)
		{
			if (!Enabled || shader == null) return;

			shader.SetClusteringParams(
				(float)renderer.currentOptions.NearClipScenery,
				(float)renderer.currentOptions.ViewingDistance,
				renderer.Screen.Width,
				renderer.Screen.Height,
				grid.NumX, grid.NumY, grid.NumZ);

			if (UseComputePath)
			{
				lightGridSSBO.Bind();
				lightIndexSSBO.Bind();
				lightDataSSBO.Bind();
				shader.SetClusteringEnabled(true);
			}
			else
			{
				// UBO path: clustering disabled in shader; existing dynamic light uniforms handle it
				shader.SetClusteringEnabled(false);
			}
		}

		// -------------------------------------------------------------------------
		// Dispose
		// -------------------------------------------------------------------------

		/// <summary>Releases all GPU resources.</summary>
		public void Dispose()
		{
			lightDataSSBO?.Dispose();
			lightGridSSBO?.Dispose();
			lightIndexSSBO?.Dispose();
			clusterAABBSSBO?.Dispose();
			cullingShader?.Dispose();
			lightDataSSBO   = null;
			lightGridSSBO   = null;
			lightIndexSSBO  = null;
			clusterAABBSSBO = null;
			cullingShader   = null;
			Enabled = false;
		}
	}
}
