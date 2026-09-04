using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
	///   UseComputePath=false → CPU sphere-vs-tile cull + 3 UBO kecil (GL 4.1)
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

		// --- CPU UBO path (GL 4.1, no compute/SSBO): single uClusterData block matching default.frag ---
		// Layout std140 (harus sama dengan GLSL):
		//  posRange[64]+colorInt[64]+dirCut[64] = 3072B | headers ivec2[128] = 2048B (stride 16)
		//  indices int[2048] = 32768B (scalar stride 16) → TOTAL 37888B < 64KB. Binding 1 (0 = animasi).
		private const int MaxCpuLights = 64;
		private const int CpuTilesX = 16;
		private const int CpuTilesY = 8;
		private const int CpuTileCount = CpuTilesX * CpuTilesY; // 128
		private const int CpuMaxPerTile = 16;
		private const int CpuIndexTotal = CpuTileCount * CpuMaxPerTile; // 2048
		private const int CpuClusterBinding = 1;
		private const int CpuUboSizeBytes = (64 * 16 * 3) + (128 * 16) + (2048 * 16); // 37888

		private int uboClusterData;
		private GpuLight[] cpuLights;   // [64] view-space scratch (full GpuLight incl. Params for cone test)
		private int[] cpuTileCounts;    // scratch [128]
		private int[] cpuTileLists;     // [2048] packed light indices per tile (tight)
		private byte[] cpuUboBytes;     // [37888] staged std140 buffer

		// --- State ---
		/// <summary>Whether CFR is active. False if init failed or disabled by options.</summary>
		public bool Enabled;

		/// <summary>
	/// True if using the SSBO + compute shader path (GL 4.3+ or ARB).
	/// False if using the CPU cull + 3 small UBOs path (GL 4.1, max 64 lights).
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
					// --- CPU path (GL 4.1): single UBO via GL langsung, tanpa wrapper/class baru ---
					cpuLights     = new GpuLight[MaxCpuLights];
					cpuTileCounts = new int[CpuTileCount];
					cpuTileLists  = new int[CpuIndexTotal];
					cpuUboBytes   = new byte[CpuUboSizeBytes];

					GL.GenBuffers(1, out uboClusterData);

					GL.BindBuffer(BufferTarget.UniformBuffer, uboClusterData);
					GL.BufferData(BufferTarget.UniformBuffer,
						new IntPtr(CpuUboSizeBytes),
						IntPtr.Zero, BufferUsageHint.DynamicDraw);
					GL.BindBuffer(BufferTarget.UniformBuffer, 0);

					renderer.fileSystem.AppendToLogFile(
						$"[CFR] Initialized: CPU UBO path, {MaxCpuLights} max lights, {CpuTilesX}x{CpuTilesY}x1 tiles, {CpuMaxPerTile}/tile " +
						$"({CpuUboSizeBytes} bytes @ binding {CpuClusterBinding})");
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
				float near   = (float)System.Math.Max(0.01, renderer.currentOptions.NearClipBase);
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

			if (!UseComputePath)
			{
				// CPU GL4.1 path: cull per-tile di CPU, upload via 3 UBO kecil.
				CpuCullAndUpload(renderer.ActiveSceneLights, viewMatrix);
				return;
			}

			// --- Compute path (stub dipertahankan apa adanya) ---
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
				lightIndexSSBO.Clear(1);
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
		}

		// -------------------------------------------------------------------------
		// CpuCullAndUpload (GL 4.1: CPU sphere-vs-tile cull + 3 UBO kecil)
		// -------------------------------------------------------------------------

		/// <summary>
		/// CPU fallback untuk GL 4.1: transformasi ke view-space (pola sama seperti
		/// CullAndUpload), uji sphere-vs-tile 2D per tile (16x8), cap 16/tile,
		/// lalu upload ke 3 UBO (lights/headers/indices). Lights sudah di-sort
		/// terdekat-dulu oleh BaseRenderer, jadi cap memotong yang terjauh.
		/// </summary>
		private void CpuCullAndUpload(List<SceneLight> lights, Matrix4D viewMatrix)
		{
			int count = System.Math.Min(lights.Count, MaxCpuLights);
			LastFrameLightCount = count;

			Array.Clear(cpuTileCounts, 0, cpuTileCounts.Length);
			Array.Clear(cpuTileLists, 0, cpuTileLists.Length);
			Array.Clear(cpuUboBytes, 0, cpuUboBytes.Length);

			if (count == 0)
			{
				// Tetap upload buffer kosong agar shader membaca count 0 per tile.
				GL.BindBuffer(BufferTarget.UniformBuffer, uboClusterData);
				GL.BufferData(BufferTarget.UniformBuffer,
					new IntPtr(CpuUboSizeBytes),
					cpuUboBytes, BufferUsageHint.DynamicDraw);
				GL.BindBuffer(BufferTarget.UniformBuffer, 0);
				return;
			}

			// --- Convert SceneLight → view-space GpuLight (pola CullAndUpload existing) ---
			// Intensity di-pre-normalisasi penuh seperti Shader.SetDynamicLights agar frag
			// cluster path (yang hanya menerima color*intensity) identik dengan legacy.
			Matrix4D lightViewMatrix = renderer.Camera.TranslationMatrix * viewMatrix;
			for (int i = 0; i < count; i++)
			{
				SceneLight sl = lights[i];

				Vector3 viewPos = sl.Position;
				viewPos.Transform(lightViewMatrix, false);

				Vector3 viewDir = sl.Direction;
				viewDir.Transform(lightViewMatrix, true);
				viewDir.Normalize();

				float intensity = sl.Power * (float)System.Math.Pow(2.0, sl.Exposure);
				float solidAngle = 6.283185307f * (1.0f - sl.SpotCutoff);
				bool normalizeSpot = (sl.Type == SceneLightType.Spot && sl.NormalizeCone);
				intensity /= normalizeSpot ? System.Math.Max(solidAngle, 0.0001f) : 12.566370614f;

				cpuLights[i] = new GpuLight
				{
					PositionAndRange   = new OTKVec4((float)viewPos.X, (float)viewPos.Y, (float)viewPos.Z, sl.Range),
					ColorAndIntensity  = new OTKVec4(sl.Color.R, sl.Color.G, sl.Color.B, intensity),
					DirectionAndCutoff = new OTKVec4((float)viewDir.X, (float)viewDir.Y, (float)viewDir.Z, sl.SpotCutoff),
					Params             = new OTKVec4(
					                         (float)sl.Type,
					                         sl.SoftFalloff ? 1.0f : 0.0f,
					                         sl.Softness,
					                         sl.RangeSquared)
				};
			}

			// --- Sphere-vs-tile cull di view space ---
			float fovY      = (float)renderer.Camera.VerticalViewingAngle;
			float aspect    = (float)renderer.Screen.AspectRatio;
			float tanHalfY  = (float)System.Math.Tan(fovY * 0.5);
			float tanHalfX  = tanHalfY * aspect;
			float nearPlane = (float)System.Math.Max(0.01, renderer.currentOptions.NearClipBase);
			float farPlane  = (float)renderer.currentOptions.ViewingDistance;

			for (int i = 0; i < count; i++)
			{
				float lx = cpuLights[i].PositionAndRange.X;
				float ly = cpuLights[i].PositionAndRange.Y;
				float lz = cpuLights[i].PositionAndRange.Z;
				float range = cpuLights[i].PositionAndRange.W;
				float depth = -lz; // jarak positif di depan kamera

				// Tolak yang sepenuhnya di luar rentang near/far
				if (depth - range >= farPlane || depth + range <= nearPlane)
					continue;

				if (depth <= 0.001f)
				{
					// Cahaya di/behind kamera tapi menyentuh near plane: masuk semua tile (cap 16).
					if (depth + range > nearPlane)
					{
						for (int t = 0; t < CpuTileCount; t++)
						{
							if (cpuTileCounts[t] < CpuMaxPerTile)
								cpuTileLists[t * CpuMaxPerTile + cpuTileCounts[t]++] = i;
						}
					}
					continue;
				}

				// Lebar setengah frustum pada kedalaman cahaya; batas tile di view space.
				// Uji sphere-vs-tile didelegasikan ke ClusterHelper (plus cone check untuk spot).
				float hw = tanHalfX * depth;
				float hh = tanHalfY * depth;
				Vector3 vpos = new Vector3(lx, ly, lz);
				bool isSpot = cpuLights[i].Params.X >= 0.5f;
				Vector3 vdir = new Vector3(
					cpuLights[i].DirectionAndCutoff.X,
					cpuLights[i].DirectionAndCutoff.Y,
					cpuLights[i].DirectionAndCutoff.Z);
				float cutoff = cpuLights[i].DirectionAndCutoff.W;

				for (int ty = 0; ty < CpuTilesY; ty++)
				{
					float yMin = (-1.0f + 2.0f * ty / CpuTilesY) * hh;
					float yMax = (-1.0f + 2.0f * (ty + 1) / CpuTilesY) * hh;

					for (int tx = 0; tx < CpuTilesX; tx++)
					{
						int t = tx + ty * CpuTilesX;
						if (cpuTileCounts[t] >= CpuMaxPerTile)
							continue;

						float xMin = (-1.0f + 2.0f * tx / CpuTilesX) * hw;
						float xMax = (-1.0f + 2.0f * (tx + 1) / CpuTilesX) * hw;

						bool hit = isSpot
							? ClusterHelper.LightAffectsTile(vpos, range, xMin, xMax, yMin, yMax, vdir, cutoff)
							: ClusterHelper.LightAffectsTile(vpos, range, xMin, xMax, yMin, yMax);
						if (hit)
							cpuTileLists[t * CpuMaxPerTile + cpuTileCounts[t]++] = i;
					}
				}
			}

			// --- Stage single uClusterData buffer (std140, little-endian) ---
			// posRange[64] @0, colorInt[64] @1024, dirCut[64] @2048,
			// headers ivec2[128] @3072 (stride 16), indices int[2048] @5120 (stride 16).
			for (int i = 0; i < count; i++)
			{
				int o = i * 16;
				System.Buffer.BlockCopy(BitConverter.GetBytes(cpuLights[i].PositionAndRange.X), 0, cpuUboBytes, o, 4);
				System.Buffer.BlockCopy(BitConverter.GetBytes(cpuLights[i].PositionAndRange.Y), 0, cpuUboBytes, o + 4, 4);
				System.Buffer.BlockCopy(BitConverter.GetBytes(cpuLights[i].PositionAndRange.Z), 0, cpuUboBytes, o + 8, 4);
				System.Buffer.BlockCopy(BitConverter.GetBytes(cpuLights[i].PositionAndRange.W), 0, cpuUboBytes, o + 12, 4);
				o = 1024 + i * 16;
				System.Buffer.BlockCopy(BitConverter.GetBytes(cpuLights[i].ColorAndIntensity.X), 0, cpuUboBytes, o, 4);
				System.Buffer.BlockCopy(BitConverter.GetBytes(cpuLights[i].ColorAndIntensity.Y), 0, cpuUboBytes, o + 4, 4);
				System.Buffer.BlockCopy(BitConverter.GetBytes(cpuLights[i].ColorAndIntensity.Z), 0, cpuUboBytes, o + 8, 4);
				System.Buffer.BlockCopy(BitConverter.GetBytes(cpuLights[i].ColorAndIntensity.W), 0, cpuUboBytes, o + 12, 4);
				o = 2048 + i * 16;
				System.Buffer.BlockCopy(BitConverter.GetBytes(cpuLights[i].DirectionAndCutoff.X), 0, cpuUboBytes, o, 4);
				System.Buffer.BlockCopy(BitConverter.GetBytes(cpuLights[i].DirectionAndCutoff.Y), 0, cpuUboBytes, o + 4, 4);
				System.Buffer.BlockCopy(BitConverter.GetBytes(cpuLights[i].DirectionAndCutoff.Z), 0, cpuUboBytes, o + 8, 4);
				System.Buffer.BlockCopy(BitConverter.GetBytes(cpuLights[i].DirectionAndCutoff.W), 0, cpuUboBytes, o + 12, 4);
			}
			for (int t = 0; t < CpuTileCount; t++)
			{
				int o = 3072 + t * 16;
				System.Buffer.BlockCopy(BitConverter.GetBytes(t * CpuMaxPerTile), 0, cpuUboBytes, o, 4);
				System.Buffer.BlockCopy(BitConverter.GetBytes(cpuTileCounts[t]), 0, cpuUboBytes, o + 4, 4);
				// +8/+12 tetap nol (padding std140 ivec2 stride 16)
			}
			for (int k = 0; k < CpuIndexTotal; k++)
			{
				int o = 5120 + k * 16;
				System.Buffer.BlockCopy(BitConverter.GetBytes(cpuTileLists[k]), 0, cpuUboBytes, o, 4);
				// +4/+8/+12 nol (scalar stride 16)
			}

			// --- Upload single block (full replace = orphaning) ---
			GL.BindBuffer(BufferTarget.UniformBuffer, uboClusterData);
			GL.BufferData(BufferTarget.UniformBuffer,
				new IntPtr(CpuUboSizeBytes),
				cpuUboBytes, BufferUsageHint.DynamicDraw);
			GL.BindBuffer(BufferTarget.UniformBuffer, 0);
		}
		// Fallback lama via SetDynamicLights() tidak lagi dipakai di path CPU:
		// BaseRenderer tetap memanggil SetDynamicLights sendiri per-object.

		// -------------------------------------------------------------------------
		// BindToShader
		// -------------------------------------------------------------------------

		/// <summary>
		/// Binds cluster data to the main scene shader for the frame.
		/// </summary>
		public void BindToShader(Shader shader)
		{
			if (!Enabled || shader == null) return;

			float nearUni = (float)System.Math.Max(0.01, renderer.currentOptions.NearClipBase);
			float farUni = (float)renderer.currentOptions.ViewingDistance;
			if (UseComputePath)
			{
				shader.SetClusteringParams(
					nearUni,
					farUni,
					renderer.Screen.Width,
					renderer.Screen.Height,
					grid.NumX, grid.NumY, grid.NumZ);
				lightGridSSBO.Bind();
				lightIndexSSBO.Bind();
				lightDataSSBO.Bind();
				shader.SetClusteringEnabled(true);
			}
			else
			{
				// CPU UBO path: grid 2D 16x8x1, bind single uClusterData block, clustering aktif
				shader.SetClusteringParams(
					nearUni,
					farUni,
					renderer.Screen.Width,
					renderer.Screen.Height,
					CpuTilesX, CpuTilesY, 1);
				if (uboClusterData != 0)
					GL.BindBufferBase(BufferRangeTarget.UniformBuffer, CpuClusterBinding, uboClusterData);
				shader.LinkClusterBlocks(CpuClusterBinding);
				shader.SetClusteringEnabled(true);
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
			if (uboClusterData != 0) { GL.DeleteBuffer(uboClusterData); uboClusterData = 0; }
			cpuLights     = null;
			cpuTileCounts = null;
			cpuTileLists  = null;
			cpuUboBytes   = null;
			Enabled = false;
		}
	}
}
