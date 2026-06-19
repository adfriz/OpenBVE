// Simplified BSD License (BSD-2-Clause)
//
// Ported / extended for the next-gen branch to bring the RealSky atmospheric
// system onto the GL 4.3 compute path. Original RealSky implementation lived
// on the `realsky` branch as a #version 330 fragment shader.
//
// All rights retained by the OpenBVE Project contributors.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Atmosphere
{
	/// <summary>
	/// Wraps the RealSky compute shader program.
	/// Loads from embedded resource "LibRender2.RealSky.comp".
	/// Does NOT inherit <see cref="Shaders.AbstractShader"/> (which is
	/// vert+frag only) — this manages its own GL program handle directly,
	/// exactly like <see cref="Clustering.ClusterCullingShader"/>.
	/// </summary>
	/// <remarks>
	/// Requires GL 4.3+ or the GL_ARB_compute_shader extension. Capability
	/// detection is performed by <see cref="RealSkyPass"/> before this class
	/// is instantiated.
	/// </remarks>
	public class RealSkyComputeShader : IDisposable
	{
		private int programHandle;

		// Cached uniform locations — fetched once at Initialize() to avoid
		// per-frame string lookups.
		private int uSunDirectionLocation;
		private int uCameraPosLocation;
		private int uTimeLocation;
		private int uResolutionLocation;
		private int uInvViewProjLocation;
		private int uExposureLocation;

		private bool disposed;

		private readonly BaseRenderer renderer;

		/// <summary>The GL program handle (0 until Initialize succeeds).</summary>
		public int Handle => programHandle;

		public RealSkyComputeShader(BaseRenderer renderer)
		{
			this.renderer = renderer;
		}

		/// <summary>
		/// Compiles and links the compute shader from embedded resource.
		/// Must be called on the GL render thread.
		/// </summary>
		public void Initialize()
		{
			// Load GLSL source from embedded resource — mirrors the pattern
			// used by ClusterCullingShader for cluster_culling.comp.
			string source;
			Assembly asm = Assembly.GetExecutingAssembly();
			using (Stream stream = asm.GetManifestResourceStream("LibRender2.RealSky.comp"))
			{
				if (stream == null)
				{
					throw new Exception("[RealSky] RealSky.comp not found as embedded resource. " +
					                    "Did you add it to LibRender2.csproj as an EmbeddedResource?");
				}
				using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
				{
					source = reader.ReadToEnd();
				}
			}

			// Compile compute shader
			int shader = GL.CreateShader(ShaderType.ComputeShader);
			GL.ShaderSource(shader, source);
			GL.CompileShader(shader);
			GL.GetShader(shader, ShaderParameter.CompileStatus, out int compileStatus);
			if (compileStatus == 0)
			{
				string log = GL.GetShaderInfoLog(shader);
				GL.DeleteShader(shader);
				throw new Exception($"[RealSky] Compute shader compile error: {log}");
			}

			// Link program
			programHandle = GL.CreateProgram();
			GL.AttachShader(programHandle, shader);
			GL.LinkProgram(programHandle);
			GL.GetProgram(programHandle, GetProgramParameterName.LinkStatus, out int linkStatus);
			if (linkStatus == 0)
			{
				string log = GL.GetProgramInfoLog(programHandle);
				GL.DeleteShader(shader);
				GL.DeleteProgram(programHandle);
				programHandle = 0;
				throw new Exception($"[RealSky] Compute shader link error: {log}");
			}

			GL.DeleteShader(shader); // shader object no longer needed after link

			// Cache uniform locations
			uSunDirectionLocation = GL.GetUniformLocation(programHandle, "uRealSkySunDirection");
			uCameraPosLocation    = GL.GetUniformLocation(programHandle, "uRealSkyCameraPos");
			uTimeLocation         = GL.GetUniformLocation(programHandle, "uRealSkyTime");
			uResolutionLocation   = GL.GetUniformLocation(programHandle, "uRealSkyResolution");
			uInvViewProjLocation  = GL.GetUniformLocation(programHandle, "uRealSkyInvViewProj");
			uExposureLocation     = GL.GetUniformLocation(programHandle, "uRealSkyExposure");
		}

		// ---------------------------------------------------------------------
		// Uniform setters — all use ProgramUniform so the calling code does not
		// have to flip GL state (matches ClusterCullingShader.SetLightCount).
		// ---------------------------------------------------------------------

		public void SetSunDirection(float x, float y, float z)
		{
			if (programHandle == 0 || uSunDirectionLocation < 0) return;
			GL.ProgramUniform3(programHandle, uSunDirectionLocation, x, y, z);
		}

		public void SetCameraPos(float x, float y, float z)
		{
			if (programHandle == 0 || uCameraPosLocation < 0) return;
			GL.ProgramUniform3(programHandle, uCameraPosLocation, x, y, z);
		}

		public void SetTime(float seconds)
		{
			if (programHandle == 0 || uTimeLocation < 0) return;
			GL.ProgramUniform1(programHandle, uTimeLocation, seconds);
		}

		public void SetResolution(int width, int height)
		{
			if (programHandle == 0 || uResolutionLocation < 0) return;
			GL.ProgramUniform2(programHandle, uResolutionLocation, (float)width, (float)height);
		}

		public void SetExposure(float exposure)
		{
			if (programHandle == 0 || uExposureLocation < 0) return;
			GL.ProgramUniform1(programHandle, uExposureLocation, exposure);
		}

		/// <summary>
		/// Sets the inverse(view * projection) matrix used to unproject pixels
		/// back to world-space view directions.
		/// </summary>
		/// <param name="invViewProj">Row-major OpenTK Matrix4 (will be transposed for GL).</param>
		public void SetInvViewProj(ref OpenTK.Matrix4 invViewProj)
		{
			if (programHandle == 0 || uInvViewProjLocation < 0) return;
			// OpenTK Matrix4 is row-major; GLSL expects column-major.
			// ProgramUniformMatrix4 with transpose=true handles this for us.
			GL.ProgramUniformMatrix4(programHandle, uInvViewProjLocation, false, ref invViewProj);
		}

		// ---------------------------------------------------------------------
		// Activate / dispatch
		// ---------------------------------------------------------------------

		/// <summary>
		/// Activates the compute program.
		/// Note: compute shaders don't go through the normal Activate() pipeline —
		/// we call UseProgram directly and don't touch renderer.CurrentShader,
		/// matching the ClusterCullingShader convention.
		/// </summary>
		public void Activate()
		{
			GL.UseProgram(programHandle);
		}

		/// <summary>
		/// Dispatches the compute shader with work group counts sized for the
		/// current screen. One workgroup covers an 8x8 pixel tile.
		/// </summary>
		public void Dispatch(int screenW, int screenH)
		{
			int groupsX = (screenW + 7) / 8;
			int groupsY = (screenH + 7) / 8;
			GL.DispatchCompute(groupsX, groupsY, 1);
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			if (disposed) return;
			if (programHandle != 0)
			{
				GL.DeleteProgram(programHandle);
				programHandle = 0;
			}
			GC.SuppressFinalize(this);
			disposed = true;
		}
	}
}
