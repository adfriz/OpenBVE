using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using OpenBveApi.Math;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// Thin compute-shader wrapper.
	/// Loads a <c>.comp</c> source from EmbeddedResource (same
	/// <c>LibRender2.{name}.comp</c> convention as
	/// <see cref="Shaders.AbstractShader"/>), compiles and links it, then
	/// exposes <see cref="Dispatch"/> plus int-location <c>Set*</c> helpers.
	/// <see cref="Use"/>/<see cref="StopUse"/> follow the
	/// AbstractShader Activate/Deactivate pattern (bind / bind 0).
	/// </summary>
	public class ComputeShader : IDisposable
	{
		/// <summary>The linked compute program handle, 0 when linking failed.</summary>
		public readonly int Handle;

		private readonly BaseRenderer renderer;
		private readonly Dictionary<string, int> locationCache = new Dictionary<string, int>(StringComparer.Ordinal);
		private bool active;
		private bool disposed;

		/// <summary>True while the program is linked and not disposed.</summary>
		public bool IsValid => Handle != 0 && !disposed;

		/// <param name="renderer">A reference to the base renderer (for log output).</param>
		/// <param name="computeShaderName">Resource name without extension, e.g. "ao_smoke".</param>
		/// <exception cref="InvalidOperationException">Thrown when the resource is missing or compile/link fails.</exception>
		public ComputeShader(BaseRenderer renderer, string computeShaderName)
		{
			this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
			if (string.IsNullOrEmpty(computeShaderName))
			{
				throw new ArgumentException("Compute shader name must not be empty.", nameof(computeShaderName));
			}
			string source = Shaders.ShaderInclude.ExpandIncludes(LoadResource(computeShaderName));
			int shader = GL.CreateShader(ShaderType.ComputeShader);
			try
			{
				GL.ShaderSource(shader, source);
				GL.CompileShader(shader);
				GL.GetShader(shader, ShaderParameter.CompileStatus, out int compileStatus);
				if (compileStatus == 0)
				{
					string infoLog = LogSafe(() => GL.GetShaderInfoLog(shader));
					LogSafe($"[Compute] Compile failed ({computeShaderName}.comp): {infoLog}");
					throw new InvalidOperationException($"Compute shader compile failed ({computeShaderName}): {infoLog}");
				}
				Handle = GL.CreateProgram();
				GL.AttachShader(Handle, shader);
				GL.DeleteShader(shader);
				shader = 0;
				GL.LinkProgram(Handle);
				GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int linkStatus);
				if (linkStatus == 0)
				{
					string infoLog = LogSafe(() => GL.GetProgramInfoLog(Handle));
					LogSafe($"[Compute] Link failed ({computeShaderName}.comp): {infoLog}");
					try
					{
						GL.DeleteProgram(Handle);
					}
					catch
					{
						// ignored
					}
				throw new InvalidOperationException($"Compute program link failed ({computeShaderName}): {infoLog}");
			}
			LogSafe($"[Compute] Loaded {computeShaderName}.comp (src {SourceHash(source):x8}, {source.Length} chars).");
		}
		catch
			{
				if (shader != 0)
				{
					try
					{
						GL.DeleteShader(shader);
					}
					catch
					{
						// ignored
					}
				}
				throw;
			}
		}

		private static string LoadResource(string name)
		{
			var asm = Assembly.GetExecutingAssembly();
			using (var s = asm.GetManifestResourceStream("LibRender2." + name + ".comp"))
			using (var r = new StreamReader(s ?? throw new InvalidOperationException("Compute shader resource not found: LibRender2." + name + ".comp"), Encoding.UTF8))
				return r.ReadToEnd();
		}

		/// <summary>Binds the compute program (AbstractShader.Activate pattern).</summary>
		public void Use()
		{
			try
			{
				if (renderer != null)
				{
					if (renderer.CurrentShader != null)
					{
						renderer.CurrentShader.IsActive = false;
					}
					renderer.lastVAO = -1;
				}
			}
			catch
			{
				// ignored: state reset must not throw
			}
			GL.UseProgram(Handle);
			active = true;
		}

		/// <summary>Unbinds the compute program (AbstractShader.Deactivate pattern).</summary>
		public void StopUse()
		{
			try
			{
				if (active)
				{
					GL.UseProgram(0);
				}
			}
			catch
			{
				// ignored: restore must not throw
			}
			active = false;
			try
			{
				if (renderer != null)
				{
					renderer.lastVAO = -1;
				}
			}
			catch
			{
				// ignored: state reset must not throw
			}
		}

		/// <summary>Dispatches the given number of work groups and issues the image-access barrier.</summary>
		public void Dispatch(int numGroupsX, int numGroupsY, int numGroupsZ)
		{
			GL.DispatchCompute(numGroupsX, numGroupsY, numGroupsZ);
			GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
		}

		/// <summary>Resolves a uniform location, -1 when missing (optimized out).</summary>
		public int GetUniformLocation(string name)
		{
			try
			{
				return GL.GetUniformLocation(Handle, name);
			}
			catch
			{
				return -1;
			}
		}

		/// <summary>Cached uniform lookup for the per-frame hot path (single GL query per name).</summary>
		public int GetCachedLocation(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return -1;
			}
			if (locationCache.TryGetValue(name, out int cached))
			{
				return cached;
			}
			int location = GetUniformLocation(name);
			locationCache[name] = location;
			return location;
		}

		/// <summary>Uploads an int uniform. No-op for negative locations. Call after <see cref="Use"/>.</summary>
		public void SetInt(int location, int value)
		{
			if (location >= 0)
			{
				GL.Uniform1(location, value);
			}
		}

		/// <summary>Uploads a float uniform. No-op for negative locations. Call after <see cref="Use"/>.</summary>
		public void SetFloat(int location, float value)
		{
			if (location >= 0)
			{
				GL.Uniform1(location, value);
			}
		}

		/// <summary>Uploads a vec2 uniform. No-op for negative locations. Call after <see cref="Use"/>.</summary>
		public void SetVector2(int location, float x, float y)
		{
			if (location >= 0)
			{
				GL.Uniform2(location, x, y);
			}
		}

		/// <summary>Uploads a vec3 uniform. No-op for negative locations. Call after <see cref="Use"/>.</summary>
		public void SetVector3(int location, float x, float y, float z)
		{
			if (location >= 0)
			{
				GL.Uniform3(location, x, y, z);
			}
		}

		/// <summary>Uploads a vec4 uniform. No-op for negative locations. Call after <see cref="Use"/>.</summary>
		public void SetVector4(int location, float x, float y, float z, float w)
		{
			if (location >= 0)
			{
				GL.Uniform4(location, x, y, z, w);
			}
		}

		/// <summary>Uploads a mat4 uniform from a <see cref="Matrix4D"/>. No-op for negative locations. Call after <see cref="Use"/>.</summary>
		public void SetMatrix4(int location, Matrix4D value)
		{
			if (location < 0)
			{
				return;
			}
			Matrix4 matrix = ConvertToMatrix4(value);
			GL.UniformMatrix4(location, false, ref matrix);
		}

		/// <summary>Thin name lookup for int uniforms (cached GL query).</summary>
		public void SetInt(string name, int value)
		{
			SetInt(GetCachedLocation(name), value);
		}

		/// <summary>Thin name lookup for float uniforms (cached GL query).</summary>
		public void SetFloat(string name, float value)
		{
			SetFloat(GetCachedLocation(name), value);
		}

		private static Matrix4 ConvertToMatrix4(Matrix4D mat)
		{
			return new Matrix4(
				(float)mat.Row0.X, (float)mat.Row0.Y, (float)mat.Row0.Z, (float)mat.Row0.W,
				(float)mat.Row1.X, (float)mat.Row1.Y, (float)mat.Row1.Z, (float)mat.Row1.W,
				(float)mat.Row2.X, (float)mat.Row2.Y, (float)mat.Row2.Z, (float)mat.Row2.W,
				(float)mat.Row3.X, (float)mat.Row3.Y, (float)mat.Row3.Z, (float)mat.Row3.W
			);
		}

		private static uint SourceHash(string text)
		{
			// FNV-1a 32-bit over the expanded source: proves WHICH shader code
			// is actually running (kills "my change had no effect" ghosts).
			unchecked
			{
				uint h = 2166136261u;
				if (!string.IsNullOrEmpty(text))
				{
					for (int i = 0; i < text.Length; i++)
					{
						h ^= text[i];
						h *= 16777619u;
					}
				}
				return h;
			}
		}

		private string LogSafe(Func<string> fetch)
		{
			try
			{
				return fetch();
			}
			catch
			{
				return "unavailable";
			}
		}

		private void LogSafe(string text)
		{
			try
			{
				if (renderer != null && renderer.fileSystem != null)
				{
					renderer.fileSystem.AppendToLogFile(text);
				}
			}
			catch
			{
				// ignored: logging must not throw
			}
		}

		/// <summary>Deletes the compute program. Requires a current GL context.</summary>
		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			try
			{
				if (Handle != 0)
				{
					GL.DeleteProgram(Handle);
				}
			}
			catch
			{
				// ignored: dispose must not throw
			}
			GC.SuppressFinalize(this);
		}
	}
}
