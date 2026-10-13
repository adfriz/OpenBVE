//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, Christopher Lees, S520, Aditiya Afrizal, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
//ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
//ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using LibRender2.Fogs;
using OpenTK.Graphics.OpenGL;
using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace LibRender2.Shaders
{
	public class AbstractShader : IDisposable
	{
		/// <summary>The OpenGL handle to the complete shader</summary>
		internal readonly int Handle;
		/// <summary>The OpenGL handle to the vertex shader component</summary>
		internal int VertexShader;
		/// <summary>The OpenGL handle to the fragment shader component</summary>
		internal int FragmentShader;
		/// <summary>Holds a reference to the base renderer</summary>
		internal readonly BaseRenderer Renderer;

		internal bool IsActive;
		public AbstractShader(BaseRenderer renderer, string vertexShaderName, string fragmentShaderName, bool isFromStream, bool fragColor)
		{
			Handle = GL.CreateProgram();
			Renderer = renderer;
			if (isFromStream)
			{
				Assembly thisAssembly = Assembly.GetExecutingAssembly();
				using (Stream stream = thisAssembly.GetManifestResourceStream($"LibRender2.{vertexShaderName}.vert"))
				{
					if (stream != null)
					{
						using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
						{
							LoadShader(ResolveIncludes(reader.ReadToEnd(), null), ShaderType.VertexShader);
						}
					}
				}
				using (Stream stream = thisAssembly.GetManifestResourceStream($"LibRender2.{fragmentShaderName}.frag"))
				{
					if (stream != null)
					{
						using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
						{
							LoadShader(ResolveIncludes(reader.ReadToEnd(), null), ShaderType.FragmentShader);
						}
					}
				}
			}
			else
			{
				LoadShader(ResolveIncludes(File.ReadAllText(vertexShaderName, Encoding.UTF8), Path.GetDirectoryName(vertexShaderName)), ShaderType.VertexShader);
				LoadShader(ResolveIncludes(File.ReadAllText(fragmentShaderName, Encoding.UTF8), Path.GetDirectoryName(fragmentShaderName)), ShaderType.FragmentShader);
			}
			GL.AttachShader(Handle, VertexShader);
			GL.AttachShader(Handle, FragmentShader);

			GL.DeleteShader(VertexShader);
			GL.DeleteShader(FragmentShader);
			if (fragColor)
			{
				GL.BindFragDataLocation(Handle, 0, "fragColor");
			}
			GL.LinkProgram(Handle);

			GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int success);
			if (success == 0)
			{
				string infoLog = GL.GetProgramInfoLog(Handle);
				throw new Exception($"Shader Link error: {infoLog}");
			}
		}

		/// <summary>
		/// Expands `#include "file"` directives, which OpenGL GLSL has no preprocessor for.
		/// An include is resolved either as a file next to the parent shader (when
		/// <paramref name="baseDirectory"/> is given) or as an embedded resource in this assembly
		/// under the name LibRender2.[.sub.]file, so the same source works in both the
		/// packaged and the file-based paths.
		/// </summary>
		/// <param name="source">Shader source code, possibly containing include directives</param>
		/// <param name="baseDirectory">Directory the parent shader lives in, or null for embedded resources</param>
		/// <returns>The source with all includes expanded in place</returns>
		private static string ResolveIncludes(string source, string baseDirectory)
		{
			return ResolveIncludes(source, baseDirectory, 0);
		}

		private static string ResolveIncludes(string source, string baseDirectory, int depth)
		{
			if (depth > 8)
			{
				throw new ApplicationException("Shader includes nested too deeply, or form a cycle");
			}
			const string directive = "#include";
			int index = 0;
			while (true)
			{
				index = source.IndexOf(directive, index, StringComparison.Ordinal);
				if (index < 0)
				{
					return source;
				}
				int start = source.IndexOf('"', index);
				if (start < 0)
				{
					return source;
				}
				int end = source.IndexOf('"', start + 1);
				if (end < 0)
				{
					return source;
				}
				// Anything trailing on the line after the path is dropped
				int lineEnd = source.IndexOf('\n', end);
				string fileName = source.Substring(start + 1, end - start - 1);
				string included = ReadInclude(fileName, baseDirectory);
				source = source.Substring(0, index) + included +
					(lineEnd < 0 ? string.Empty : source.Substring(lineEnd));
				// Keep scanning past the text just inserted; depth guards against a cycle
				index += included.Length;
			}
		}

		/// <summary>Reads a single include, from disk or from the embedded resources</summary>
		private static string ReadInclude(string fileName, string baseDirectory)
		{
			if (baseDirectory != null && File.Exists(Path.Combine(baseDirectory, fileName.Replace('/', Path.DirectorySeparatorChar))))
			{
				return File.ReadAllText(Path.Combine(baseDirectory, fileName.Replace('/', Path.DirectorySeparatorChar)), Encoding.UTF8);
			}
			Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"LibRender2.{fileName.Replace('\\', '.')}");
			if (stream == null)
			{
				throw new ApplicationException($"Shader include not found: {fileName}");
			}
			using (stream)
			{
				using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
				{
					return reader.ReadToEnd();
				}
			}
		}

		/// <summary>Loads the shader source and compiles the shader</summary>
		/// <param name="shaderSource">Shader source code string</param>
		/// <param name="shaderType">type of shader VertexShader or FragmentShader</param>
		internal void LoadShader(string shaderSource, ShaderType shaderType)
		{
			int status;

			switch (shaderType)
			{
				case ShaderType.VertexShader:
					VertexShader = GL.CreateShader(shaderType);
					GL.ShaderSource(VertexShader, shaderSource);
					GL.CompileShader(VertexShader);
					GL.GetShader(VertexShader, ShaderParameter.CompileStatus, out status);
					if (status == 0)
					{
						throw new ApplicationException(GL.GetShaderInfoLog(VertexShader));
					}
					break;
				case ShaderType.FragmentShader:

					FragmentShader = GL.CreateShader(shaderType);
					GL.ShaderSource(FragmentShader, shaderSource);
					GL.CompileShader(FragmentShader);
					GL.GetShader(FragmentShader, ShaderParameter.CompileStatus, out status);

					if (status == 0)
					{
						throw new ApplicationException(GL.GetShaderInfoLog(FragmentShader));
					}
					break;
				default:
					throw new InvalidOperationException("Attempted to load an unknown shader type");
			}
		}


		/// <summary>Activates the shader program for use</summary>
		public virtual void Activate()
		{
			if (IsActive)
			{
				return;
			}

			if (Renderer.CurrentShader != null)
			{
				Renderer.CurrentShader.IsActive = false;
			}
			GL.UseProgram(Handle);
			IsActive = true;
			Renderer.lastVAO = -1;
			Renderer.CurrentShader = this;
			Renderer.RestoreAlphaFunc();
		}

		/// <summary>Deactivates the shader</summary>
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

		/// <summary>Specifies the OpenGL alpha function to perform</summary>
		/// <param name="alphaFunction">The comparison to use</param>
		/// <param name="alphaComparison">The value to compare</param>
		public virtual void SetAlphaFunction(AlphaFunction alphaFunction, float alphaComparison)
		{

		}

		/// <summary>Sets whether AlphaTesting is enabled</summary>
		public virtual void SetAlphaTest(bool enabled)
		{

		}

		/// <summary>Sets whether Fog is enabled</summary>
		public virtual void SetFog(bool enabled)
		{
		}

		/// <summary>Sets the current fog</summary>
		public virtual void SetFog(Fog Fog)
		{
		}

		private bool disposed;

		/// <summary>Cleans up, releasing the underlying openTK/OpenGL shader program</summary>
		public void Dispose()
		{
			if (!disposed)
			{
				GL.DeleteProgram(Handle);
				GC.SuppressFinalize(this);
				disposed = true;
			}
		}
	}
}
