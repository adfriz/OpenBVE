using System;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// One big triangle every post pass draws with.
	public class FullscreenQuad : IDisposable
	{
		private int vao;
		private int vbo;

		// Build once, reuse forever.
		public void Ensure()
		{
			if (vao != 0)
			{
				return;
			}
			float[] verts = { -1.0f, -1.0f, 0.0f, 0.0f, 3.0f, -1.0f, 2.0f, 0.0f, -1.0f, 3.0f, 0.0f, 2.0f };
			GL.GenVertexArrays(1, out vao);
			GL.GenBuffers(1, out vbo);
			GL.BindVertexArray(vao);
			GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
			GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(verts.Length * sizeof(float)), verts, BufferUsageHint.StaticDraw);
			GL.EnableVertexAttribArray(0);
			GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
			GL.EnableVertexAttribArray(1);
			GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
			GL.BindVertexArray(0);
		}

		public void Draw()
		{
			GL.BindVertexArray(vao);
			GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
			GL.BindVertexArray(0);
		}

		public void Dispose()
		{
			Quiet(() => GL.DeleteVertexArray(vao));
			vao = 0;
			Quiet(() => GL.DeleteBuffer(vbo));
			vbo = 0;
		}

		// Shutdown may have no context left; just drop the handle.
		private static void Quiet(Action run)
		{
			try
			{
				run();
			}
			catch
			{
			}
		}
	}
}
