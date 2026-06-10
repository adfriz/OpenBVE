using System;
using System.IO;
using System.Reflection;
using System.Text;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Clustering
{
	/// <summary>
	/// Wraps the cluster_culling compute shader program.
	/// Loads from embedded resource "LibRender2.cluster_culling.comp".
	/// Does NOT inherit AbstractShader (which is vert+frag only) —
	/// this manages its own GL program handle directly.
	/// </summary>
	public class ClusterCullingShader : IDisposable
	{
		private int programHandle;
		private int uLightCountLocation;
		private bool disposed;

		private readonly BaseRenderer renderer;

		public ClusterCullingShader(BaseRenderer renderer)
		{
			this.renderer = renderer;
		}

		/// <summary>
		/// Compiles and links the compute shader from embedded resource.
		/// Must be called on the GL render thread.
		/// </summary>
		public void Initialize()
		{
			// Load GLSL source from embedded resource
			string source;
			Assembly asm = Assembly.GetExecutingAssembly();
			using (Stream stream = asm.GetManifestResourceStream("LibRender2.cluster_culling.comp"))
			{
				if (stream == null)
					throw new Exception("[CFR] cluster_culling.comp not found as embedded resource.");
				using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
					source = reader.ReadToEnd();
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
				throw new Exception($"[CFR] Compute shader compile error: {log}");
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
				throw new Exception($"[CFR] Compute shader link error: {log}");
			}

			GL.DeleteShader(shader); // shader object no longer needed after link
			uLightCountLocation = GL.GetUniformLocation(programHandle, "uLightCount");
		}

		/// <summary>Sets the uLightCount uniform before dispatch.</summary>
		public void SetLightCount(int count)
		{
			GL.ProgramUniform1(programHandle, uLightCountLocation, count);
		}

		/// <summary>
		/// Activates the compute program.
		/// Note: compute shaders don't go through the normal Activate() pipeline —
		/// we call UseProgram directly and don't set renderer.CurrentShader.
		/// </summary>
		public void Activate()
		{
			GL.UseProgram(programHandle);
		}

		/// <summary>
		/// Dispatches the compute shader with the given work group counts.
		/// One work group per cluster (16×8×24 = 3072 groups total).
		/// </summary>
		public void Dispatch(int groupsX, int groupsY, int groupsZ)
		{
			GL.DispatchCompute(groupsX, groupsY, groupsZ);
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
