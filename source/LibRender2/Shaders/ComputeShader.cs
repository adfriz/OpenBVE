using System;
using System.IO;
using System.Reflection;
using System.Text;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Shaders
{
	public class ComputeShader : IDisposable
	{
		public readonly int Handle;
		private readonly BaseRenderer Renderer;
		public bool IsActive { get; private set; }

		public ComputeShader(BaseRenderer renderer, string computeShaderName, bool isFromStream = false)
		{
			Renderer = renderer;
			Handle = GL.CreateProgram();

			int shader = GL.CreateShader(ShaderType.ComputeShader);
			string source;

			if (isFromStream)
			{
				Assembly thisAssembly = Assembly.GetExecutingAssembly();
				using (Stream stream = thisAssembly.GetManifestResourceStream($"LibRender2.{computeShaderName}.comp"))
				{
					if (stream == null)
					{
						throw new Exception($"Embedded resource LibRender2.{computeShaderName}.comp not found.");
					}
					using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
					{
						source = reader.ReadToEnd();
					}
				}
			}
			else
			{
				source = File.ReadAllText(computeShaderName, Encoding.UTF8);
			}

			GL.ShaderSource(shader, source);
			GL.CompileShader(shader);
			GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
			if (status == 0)
			{
				string infoLog = GL.GetShaderInfoLog(shader);
				throw new Exception($"Compute Shader Compile error ({computeShaderName}): {infoLog}");
			}

			GL.AttachShader(Handle, shader);
			GL.LinkProgram(Handle);
			GL.DeleteShader(shader);

			GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int success);
			if (success == 0)
			{
				string infoLog = GL.GetProgramInfoLog(Handle);
				throw new Exception($"Compute Shader Link error ({computeShaderName}): {infoLog}");
			}
		}

		public void Activate()
		{
			if (IsActive)
			{
				return;
			}

			if (Renderer.CurrentShader != null)
			{
				Renderer.CurrentShader.Deactivate();
			}
			GL.UseProgram(Handle);
			IsActive = true;
			Renderer.lastVAO = -1;
		}

		public void Deactivate()
		{
			if (!IsActive)
			{
				return;
			}
			IsActive = false;
			GL.UseProgram(0);
			Renderer.lastVAO = -1;
		}

		public void Dispose()
		{
			GL.DeleteProgram(Handle);
		}
	}
}
