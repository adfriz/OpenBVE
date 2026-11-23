using System;
using LibRender2.Shaders;
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
		public bool Enabled { get; set; } = true;
		public abstract void Execute(BaseRenderer renderer, RenderTarget source);
		public virtual void Resize(int width, int height)
		{
		}
		public virtual void Dispose()
		{
		}
	}

	/// <summary>A single post-processing program using the shared full-screen vertex shader.</summary>
	internal sealed class PostMaterial : IDisposable
	{
		private readonly AbstractShader shader;
		private int sourceLocation = -1;
		private int strengthLocation = -1;
		private int exposureLocation = -1;
		private bool disposed;

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
			GL.ActiveTexture(TextureUnit.Texture0);
			GL.BindTexture(TextureTarget.Texture2D, sourceTexture);
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
	internal sealed class FullScreenQuad : IDisposable
	{
		private int vao;
		private bool disposed;

		internal FullScreenQuad()
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
