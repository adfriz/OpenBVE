using System;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace LibRender2
{
	/// <summary>
	/// Class that represents an OpenGL/OpenTK vertex buffer object for instance data (matrices)
	/// </summary>
	public class InstanceBufferObject : IDisposable
	{
		private readonly int handle;
		private bool disposed;

		public InstanceBufferObject()
		{
			GL.GenBuffers(1, out handle);
		}

		internal void Bind()
		{
			GL.BindBuffer(BufferTarget.ArrayBuffer, handle);
		}

		/// <summary>Uploads matrix data to the GPU</summary>
		internal void BufferData(Matrix4[] data, BufferUsageHint drawType)
		{
			GL.BufferData(BufferTarget.ArrayBuffer, new IntPtr(data.Length * Vector4.SizeInBytes * 4), data, drawType);
		}

		/// <summary>Updates a portion of the buffer</summary>
		internal void BufferSubData(Matrix4[] data, int count)
		{
			GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, new IntPtr(count * Vector4.SizeInBytes * 4), data);
		}

		public void Dispose()
		{
			if (disposed) return;
			GL.DeleteBuffer(handle);
			GC.SuppressFinalize(this);
			disposed = true;
		}

		~InstanceBufferObject()
		{
			if (disposed) return;
			lock (BaseRenderer.vboToDelete)
			{
				BaseRenderer.vboToDelete.Add(handle);
			}
		}
	}
}
