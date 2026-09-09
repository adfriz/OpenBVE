using System;
using OpenBveApi.Math;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// A single fullscreen triangle (3 vertices) shared by all fragment-based
	/// post effects. Built only from the existing
	/// <see cref="VertexArrayObject"/> / <see cref="VertexBufferObject"/>
	/// wrappers: no new GL code, drawing goes through
	/// <see cref="VertexArrayObject.DrawArrays"/>.
	/// Matches post.vert layout(location = 0) iPosition, so only the
	/// Position attribute is enabled.
	/// </summary>
	public class FullscreenTriangle : IDisposable
	{
		private VertexArrayObject vao;
		private bool initialized;
		private bool disposed;

		/// <summary>Uploads the 3 triangle vertices. Requires a current GL context.</summary>
		public void Initialize()
		{
			if (initialized)
			{
				return;
			}
			LibRenderVertex[] verts = new LibRenderVertex[]
			{
				new LibRenderVertex(new Vector3f(-1.0f, -1.0f, 0.0f)),
				new LibRenderVertex(new Vector3f(3.0f, -1.0f, 0.0f)),
				new LibRenderVertex(new Vector3f(-1.0f, 3.0f, 0.0f)),
			};
			vao = new VertexArrayObject();
			vao.Bind();
			vao.SetVBO(new VertexBufferObject(verts, BufferUsageHint.StaticDraw));
			vao.SetAttributes(new VertexLayout { Position = 0 });
			vao.UnBind();
			initialized = true;
		}

		/// <summary>Draws the triangle with the currently active shader. No-op before <see cref="Initialize"/>.</summary>
		public void Draw()
		{
			if (!initialized || vao == null || disposed)
			{
				return;
			}
			vao.Bind();
			try
			{
				vao.DrawArrays(PrimitiveType.Triangles, 0, 3);
			}
			finally
			{
				vao.UnBind();
			}
		}

		/// <summary>Releases the VAO/VBO. Requires a current GL context.</summary>
		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			initialized = false;
			try
			{
				if (vao != null)
				{
					vao.Dispose();
				}
			}
			catch
			{
				// ignored: dispose must not throw
			}
			vao = null;
			GC.SuppressFinalize(this);
		}
	}
}
