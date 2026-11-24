using System;
using System.Collections.Generic;
using LibRender2.Shaders;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// Base class for one full-screen post-processing step. Passes read the compositor's current
	/// source texture and write to the framebuffer the compositor has bound for them. Adding a new
	/// effect means adding one of these - the compositor and the render hooks do not change.
	/// </summary>
	public abstract class PostProcessPass : IDisposable
	{
		public abstract string Name { get; }

		/// <summary>Friendly label shown in options UI. Override for a nicer name than the shader id.</summary>
		public virtual string DisplayName => Name;

		public bool Enabled { get; set; } = true;
		public abstract void Execute(BaseRenderer renderer, RenderTarget source);
		public virtual void Resize(int width, int height)
		{
		}
		public virtual void Dispose()
		{
		}

		/// <summary>User-tuneable parameters exposed by this pass in the options UI. Null means none.</summary>
		public virtual IList<PostProcessTuning> Tunings => null;
	}

	/// <summary>One user-tuneable parameter of a post-processing pass.</summary>
	public sealed class PostProcessTuning
	{
		public string Name { get; }
		public float Min { get; }
		public float Max { get; }
		public Func<float> Get { get; }
		public Action<float> Set { get; }
		/// <summary>True when the UI should show a checkbox instead of a numeric slider.</summary>
		public bool IsBoolean { get; }
		/// <summary>When set, the UI shows a dropdown of these names instead of a numeric slider.</summary>
		public string[] Options { get; }

		public PostProcessTuning(string name, float min, float max, Func<float> get, Action<float> set)
			: this(name, min, max, get, set, false, null)
		{
		}

		public PostProcessTuning(string name, Func<bool> get, Action<bool> set)
			: this(name, 0.0f, 1.0f, () => get() ? 1.0f : 0.0f, v => set(v > 0.5f), true, null)
		{
		}

		public PostProcessTuning(string name, string[] options, Func<float> get, Action<float> set)
			: this(name, 0.0f, Math.Max(0, options.Length - 1), get, set, false, options)
		{
		}

		private PostProcessTuning(string name, float min, float max, Func<float> get, Action<float> set,
			bool isBoolean, string[] options)
		{
			Name = name;
			Min = min;
			Max = max;
			Get = get;
			Set = set;
			IsBoolean = isBoolean;
			Options = options;
		}
	}

	/// <summary>A single post-processing program using the shared full-screen vertex shader.</summary>
	internal sealed class PostMaterial : IDisposable
	{
		private readonly AbstractShader shader;
		private readonly Dictionary<string, int> extraLocations = new Dictionary<string, int>();
		private int sourceLocation = -1;
		private int strengthLocation = -1;
		private int exposureLocation = -1;
		private bool disposed;

		private int Location(string name)
		{
			if (!extraLocations.TryGetValue(name, out int location))
			{
				location = GL.GetUniformLocation(shader.Handle, name);
				extraLocations.Add(name, location);
			}
			return location;
		}

		/// <summary>Sets an extra float uniform on the material's program.</summary>
		internal void SetFloat(string name, float value)
		{
			int location = Location(name);
			if (location != -1)
			{
				GL.ProgramUniform1(shader.Handle, location, value);
			}
		}

		/// <summary>Sets an extra integer uniform (e.g. a sampler unit) on the material's program.</summary>
		internal void SetInt(string name, int value)
		{
			int location = Location(name);
			if (location != -1)
			{
				GL.ProgramUniform1(shader.Handle, location, value);
			}
		}

		internal void SetFloatArray(string name, float[] values)
		{
			int location = Location(name);
			if (location != -1 && values != null && values.Length > 0)
			{
				GL.ProgramUniform1(shader.Handle, location, values.Length, values);
			}
		}

		internal void SetVec2(string name, Vector2 value)
		{
			int location = Location(name);
			if (location != -1)
			{
				GL.ProgramUniform2(shader.Handle, location, value.X, value.Y);
			}
		}

		internal void SetVec3(string name, Vector3 value)
		{
			int location = Location(name);
			if (location != -1)
			{
				GL.ProgramUniform3(shader.Handle, location, value.X, value.Y, value.Z);
			}
		}

		internal void SetVec4(string name, Vector4 value)
		{
			int location = Location(name);
			if (location != -1)
			{
				GL.ProgramUniform4(shader.Handle, location, value.X, value.Y, value.Z, value.W);
			}
		}

		/// <summary>Sets a mat4 uniform, e.g. a previous-frame view-projection for temporal effects.</summary>
		internal void SetMatrix4(string name, ref Matrix4 value)
		{
			int location = Location(name);
			if (location != -1)
			{
				GL.ProgramUniformMatrix4(shader.Handle, location, false, ref value);
			}
		}

		internal PostMaterial(BaseRenderer renderer, string fragmentName)
		{
			shader = new AbstractShader(renderer, "post", fragmentName, true, true);
			sourceLocation = GL.GetUniformLocation(shader.Handle, "uSource");
			strengthLocation = GL.GetUniformLocation(shader.Handle, "uStrength");
			exposureLocation = GL.GetUniformLocation(shader.Handle, "uExposure");
		}

		internal void Activate(int sourceTexture, float strength, float exposure = 1.0f)
		{
			GL.UseProgram(shader.Handle);
			TextureBinder.Bind(0, sourceTexture);
			if (sourceLocation != -1)
			{
				GL.Uniform1(sourceLocation, 0);
			}
			if (strengthLocation != -1)
			{
				GL.Uniform1(strengthLocation, strength);
			}
			if (exposureLocation != -1)
			{
				GL.Uniform1(exposureLocation, exposure);
			}
		}

		public void Dispose()
		{
			if (!disposed)
			{
				shader.Dispose();
				disposed = true;
				GC.SuppressFinalize(this);
			}
		}
	}

	/// <summary>A buffer-free full-screen triangle generated from gl_VertexID.</summary>
	internal sealed class FullScreenTriangle : IDisposable
	{
		private int vao;
		private bool disposed;

		internal FullScreenTriangle()
		{
			GL.GenVertexArrays(1, out vao);
		}

		internal void Draw()
		{
			GL.BindVertexArray(vao);
			GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
		}

		public void Dispose()
		{
			if (!disposed)
			{
				if (vao != 0)
				{
					GL.DeleteVertexArrays(1, ref vao);
					vao = 0;
				}
				disposed = true;
				GC.SuppressFinalize(this);
			}
		}
	}
}
