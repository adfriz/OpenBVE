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

using OpenTK.Graphics.OpenGL;
using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace LibRender2.Shaders
{
	/// <summary>Runs one .comp program.</summary>
	public class ComputeShader : IDisposable
	{
		internal readonly int Handle;

		public ComputeShader(string computeShaderName, bool isFromStream = true)
		{
			Handle = GL.CreateProgram();
			string source;
			if (isFromStream)
			{
				Assembly assembly = Assembly.GetExecutingAssembly();
				using (Stream stream = assembly.GetManifestResourceStream("LibRender2." + computeShaderName + ".comp"))
				{
					if (stream == null)
					{
						throw new InvalidOperationException("Compute shader resource not found: " + computeShaderName);
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
			int shader = GL.CreateShader(ShaderType.ComputeShader);
			GL.ShaderSource(shader, source);
			GL.CompileShader(shader);
			GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
			if (status == 0)
			{
				string log = GL.GetShaderInfoLog(shader);
				throw new ApplicationException(log);
			}
			GL.AttachShader(Handle, shader);
			GL.DeleteShader(shader);
			GL.LinkProgram(Handle);
			GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int linked);
			if (linked == 0)
			{
				throw new Exception("Compute shader link error: " + GL.GetProgramInfoLog(Handle));
			}
		}

		public void Use()
		{
			GL.UseProgram(Handle);
		}

		public int GetUniformLocation(string name)
		{
			return GL.GetUniformLocation(Handle, name);
		}

		public void SetFloat(int location, float value)
		{
			if (location != -1)
			{
				GL.ProgramUniform1(Handle, location, value);
			}
		}

		public void SetUInt2(int location, uint x, uint y)
		{
			if (location != -1)
			{
				GL.ProgramUniform2(Handle, location, x, y);
			}
		}

		public void SetUInt3(int location, uint x, uint y, uint z)
		{
			if (location != -1)
			{
				GL.ProgramUniform3(Handle, location, x, y, z);
			}
		}

		public void SetMatrix(int location, OpenBveApi.Math.Matrix4D mat)
		{
			if (location == -1)
			{
				return;
			}
			OpenTK.Matrix4 matrix = new OpenTK.Matrix4(
				(float)mat.Row0.X, (float)mat.Row0.Y, (float)mat.Row0.Z, (float)mat.Row0.W,
				(float)mat.Row1.X, (float)mat.Row1.Y, (float)mat.Row1.Z, (float)mat.Row1.W,
				(float)mat.Row2.X, (float)mat.Row2.Y, (float)mat.Row2.Z, (float)mat.Row2.W,
				(float)mat.Row3.X, (float)mat.Row3.Y, (float)mat.Row3.Z, (float)mat.Row3.W);
			GL.ProgramUniformMatrix4(Handle, location, false, ref matrix);
		}

		public void Dispatch(int groupsX, int groupsY, int groupsZ)
		{
			GL.DispatchCompute(groupsX, groupsY, groupsZ);
		}

		private bool disposed;

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
