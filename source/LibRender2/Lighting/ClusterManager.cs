using System;
using System.Collections.Generic;
using LibRender2.Shaders;
using LibRender2.ShadowMapping;
using OpenBveApi.Math;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Lightings
{
	/// <summary>Builds the cluster grid and assigns lights on the GPU.</summary>
	public class ClusterManager
	{
		private readonly BaseRenderer renderer;

		private ComputeShader clusterProgram;
		private ComputeShader cullProgram;
		private int clusterBuffer;
		private int lightBuffer;
		private bool permanentlyDisabled;
		// Just looked up once, asking the driver every frame wastes GL calls.
		private int uZNear = -1;
		private int uZFar = -1;
		private int uInverseProjection = -1;
		private int uGridSize = -1;
		private int uScreenDimensions = -1;
		private int uViewMatrix = -1;

		/// <summary>True when this frame's dispatch succeeded and the shading pass may read the buffers.</summary>
		public bool HasValidClusters { get; private set; }

		/// <summary>One Cluster is 16+16+4+400 bytes, padded to 448 by std430.</summary>
		public const int ClusterStride = 448;

		private const int CullLocalSize = 128;

		internal ClusterManager(BaseRenderer renderer)
		{
			this.renderer = renderer;
		}

		// Compile once on first use. If anything fails, stay on the CPU path for good.
		private bool EnsureInitialized()
		{
			if (permanentlyDisabled)
			{
				return false;
			}
			if (clusterProgram != null)
			{
				return true;
			}
			try
			{
				clusterProgram = new ComputeShader("light_clusters");
				cullProgram = new ComputeShader("light_cull");
				uZNear = clusterProgram.GetUniformLocation("zNear");
				uZFar = clusterProgram.GetUniformLocation("zFar");
				uInverseProjection = clusterProgram.GetUniformLocation("inverseProjection");
				uGridSize = clusterProgram.GetUniformLocation("gridSize");
				uScreenDimensions = clusterProgram.GetUniformLocation("screenDimensions");
				uViewMatrix = cullProgram.GetUniformLocation("viewMatrix");
				clusterBuffer = GL.GenBuffer();
				GL.BindBuffer(BufferTarget.ShaderStorageBuffer, clusterBuffer);
				GL.BufferData(BufferTarget.ShaderStorageBuffer, (IntPtr)(ClusterStride * ClusterGrid.ClusterCount), IntPtr.Zero, BufferUsageHint.DynamicDraw);
				GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);
				lightBuffer = GL.GenBuffer();
			}
			catch
			{
				DisposeGl();
				permanentlyDisabled = true;
				return false;
			}
			return true;
		}

		/// <summary>Culls this frame's lights on the GPU. False means nothing happened: no compute, no lights, or GL said no.</summary>
		public bool Dispatch()
		{
			HasValidClusters = false;
			if (!LightCapabilities.CanUseCompute(renderer))
			{
				return false;
			}
			List<SceneLight> lights = renderer.LightRegistry.DynamicLights;
			if (lights.Count == 0)
			{
				return false;
			}
			if (!EnsureInitialized())
			{
				return false;
			}
			Matrix4D viewMatrix = renderer.CurrentViewMatrix;
			Matrix4D projection = renderer.CurrentProjectionMatrix;
			FrustumPlane[] frustum = FrustumUtils.GetFrustumPlanesWorldSpace(viewMatrix * projection);
			List<ClusterLightData> packed = new List<ClusterLightData>(lights.Count);
			for (int i = 0; i < lights.Count; i++)
			{
				// Outside the frustum no cluster can see it: keep the SSBO (and the cull shader) small.
				if (!FrustumUtils.SphereVisible(frustum, lights[i].Position, lights[i].Range))
				{
					continue;
				}
				ClusterLightData data;
				if (ClusterLightData.TryPack(lights[i], out data))
				{
					packed.Add(data);
				}
			}
			if (packed.Count == 0)
			{
				return false;
			}
			AbstractShader previous = renderer.CurrentShader;
			if (previous != null)
			{
				previous.IsActive = false;
			}
			try
			{
				ClusterLightData[] lightArray = packed.ToArray();
				GL.BindBuffer(BufferTarget.ShaderStorageBuffer, lightBuffer);
				GL.BufferData(BufferTarget.ShaderStorageBuffer, (IntPtr)(ClusterLightData.Stride * lightArray.Length), lightArray, BufferUsageHint.DynamicDraw);

				Matrix4D inverseProjection = Matrix4D.Inverse(projection);
				uint screenWidth = (uint)Math.Max(renderer.Screen.Width, 1);
				uint screenHeight = (uint)Math.Max(renderer.Screen.Height, 1);

				clusterProgram.Use();
				clusterProgram.SetFloat(uZNear, (float)renderer.CurrentNearPlane);
				clusterProgram.SetFloat(uZFar, (float)renderer.CurrentFarPlane);
				clusterProgram.SetMatrix(uInverseProjection, inverseProjection);
				clusterProgram.SetUInt3(uGridSize, ClusterGrid.GridX, ClusterGrid.GridY, ClusterGrid.GridZ);
				clusterProgram.SetUInt2(uScreenDimensions, screenWidth, screenHeight);
				GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1, clusterBuffer);
				GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2, lightBuffer);
				clusterProgram.Dispatch(ClusterGrid.GridX, ClusterGrid.GridY, ClusterGrid.GridZ);
				GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);

				cullProgram.Use();
				cullProgram.SetMatrix(uViewMatrix, viewMatrix);
				cullProgram.Dispatch((ClusterGrid.ClusterCount + CullLocalSize - 1) / CullLocalSize, 1, 1);
				GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
			}
			catch
			{
				RestoreShader(previous);
				return false;
			}
			RestoreShader(previous);
			HasValidClusters = true;
			return true;
		}

		// Hands the GL context back to whoever was drawing.
		private void RestoreShader(AbstractShader previous)
		{
			GL.UseProgram(0);
			UnbindShading();
			if (previous != null)
			{
				previous.Activate();
			}
		}

		/// <summary>Lets the shading pass read the buffers. Does nothing without valid clusters.</summary>
		public void BindForShading()
		{
			if (!HasValidClusters)
			{
				return;
			}
			GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1, clusterBuffer);
			GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2, lightBuffer);
		}

		public void UnbindShading()
		{
			GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1, 0);
			GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2, 0);
		}

		private void DisposeGl()
		{
			if (clusterProgram != null)
			{
				clusterProgram.Dispose();
				clusterProgram = null;
			}
			if (cullProgram != null)
			{
				cullProgram.Dispose();
				cullProgram = null;
			}
			if (clusterBuffer != 0)
			{
				GL.DeleteBuffer(clusterBuffer);
				clusterBuffer = 0;
			}
			if (lightBuffer != 0)
			{
				GL.DeleteBuffer(lightBuffer);
				lightBuffer = 0;
			}
		}

		public void Dispose()
		{
			try
			{
				DisposeGl();
			}
			catch
			{
				// Best effort during shutdown; the context may already be gone.
			}
		}
	}
}
