//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2026, The OpenBVE Project
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Shaders
{
	/// <summary>
	/// Standalone GL 4.3 compute program. Kept separate from AbstractShader so the existing
	/// vertex/fragment shader path is untouched.
	/// </summary>
	public class ComputeShader : IDisposable
	{
		private readonly int handle;
		private readonly Dictionary<string, int> uniformLocations = new Dictionary<string, int>();
		private bool disposed;

		public ComputeShader(string shaderName)
		{
			handle = GL.CreateProgram();
			int shader = GL.CreateShader(ShaderType.ComputeShader);
			try
			{
				Assembly assembly = Assembly.GetExecutingAssembly();
				using (Stream stream = assembly.GetManifestResourceStream("LibRender2." + shaderName + ".comp"))
				{
					if (stream == null)
					{
						throw new FileNotFoundException("Embedded compute shader not found: " + shaderName);
					}
					using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
					{
						GL.ShaderSource(shader, reader.ReadToEnd());
					}
				}
				GL.CompileShader(shader);
				int status;
				GL.GetShader(shader, ShaderParameter.CompileStatus, out status);
				if (status == 0)
				{
					throw new ApplicationException("Compute shader compile error: " + GL.GetShaderInfoLog(shader));
				}
				GL.AttachShader(handle, shader);
				GL.LinkProgram(handle);
				GL.GetProgram(handle, GetProgramParameterName.LinkStatus, out status);
				if (status == 0)
				{
					throw new ApplicationException("Compute shader link error: " + GL.GetProgramInfoLog(handle));
				}
			}
			catch
			{
				GL.DeleteShader(shader);
				GL.DeleteProgram(handle);
				throw;
			}
			GL.DeleteShader(shader);
		}

		private int Location(string name)
		{
			int location;
			if (!uniformLocations.TryGetValue(name, out location))
			{
				location = GL.GetUniformLocation(handle, name);
				uniformLocations.Add(name, location);
			}
			return location;
		}

		public void Activate()
		{
			GL.UseProgram(handle);
		}

		public static void Deactivate()
		{
			GL.UseProgram(0);
		}

		public void SetInt(string name, int value)
		{
			GL.ProgramUniform1(handle, Location(name), value);
		}

		public void SetFloat(string name, float value)
		{
			GL.ProgramUniform1(handle, Location(name), value);
		}

		public void Dispatch(int groupsX, int groupsY, int groupsZ)
		{
			GL.DispatchCompute(groupsX, groupsY, groupsZ);
		}

		public void Dispose()
		{
			if (!disposed)
			{
				GL.DeleteProgram(handle);
				disposed = true;
				GC.SuppressFinalize(this);
			}
		}
	}
}
