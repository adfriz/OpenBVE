using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.openGL
{
	/// <summary>
	/// Generic Shader Storage Buffer Object (SSBO) wrapper.
	/// Mirrors the <see cref="VertexBufferObject"/> pattern.
	/// Requires GL 4.3+ or GL_ARB_shader_storage_buffer_object extension.
	/// </summary>
	public class ShaderStorageBufferObject<T> : IDisposable where T : struct
	{
		/// <summary>The OpenGL buffer handle</summary>
		internal readonly int Handle;

		/// <summary>The SSBO binding point index (layout binding = N in GLSL)</summary>
		private readonly int bindingPoint;

		/// <summary>Cached size of T in bytes</summary>
		private readonly int elementSize;

		private bool disposed;

		/// <summary>
		/// Creates a new SSBO bound to the specified binding point.
		/// Must be called on the GL render thread.
		/// </summary>
		/// <param name="BindingPoint">The SSBO binding point (layout binding = N)</param>
		public ShaderStorageBufferObject(int BindingPoint)
		{
			bindingPoint = BindingPoint;
			elementSize = Marshal.SizeOf(typeof(T));
			GL.GenBuffers(1, out Handle);
		}

		/// <summary>
		/// Uploads data to the SSBO using GL.BufferData (full replace).
		/// Must be called on the GL render thread.
		/// </summary>
		/// <param name="data">Array of structs to upload</param>
		public void Upload(T[] data)
		{
			if (data == null || data.Length == 0) return;
			int sizeBytes = data.Length * elementSize;
			GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
			try
			{
				GL.BindBuffer(BufferTarget.ShaderStorageBuffer, Handle);
				GL.BufferData(BufferTarget.ShaderStorageBuffer,
					new IntPtr(sizeBytes),
					pin.AddrOfPinnedObject(),
					BufferUsageHint.DynamicDraw);
				GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);
			}
			finally
			{
				pin.Free();
			}
		}

		/// <summary>
		/// Allocates GPU storage without uploading data (pre-allocate for compute writes).
		/// </summary>
		/// <param name="elementCount">Number of T elements to reserve</param>
		public void Allocate(int elementCount)
		{
			int sizeBytes = elementCount * elementSize;
			GL.BindBuffer(BufferTarget.ShaderStorageBuffer, Handle);
			GL.BufferData(BufferTarget.ShaderStorageBuffer,
				new IntPtr(sizeBytes),
				IntPtr.Zero,
				BufferUsageHint.DynamicDraw);
			GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);
		}

		/// <summary>
		/// Binds the SSBO to its binding point for shader use.
		/// </summary>
		public void Bind()
		{
			GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, bindingPoint, Handle);
		}

		/// <summary>
		/// Clears the SSBO content to zero (useful for resetting LightIndex counter each frame).
		/// </summary>
		public void Clear(int elementCount)
		{
			// Use BufferSubData with zeroed array — avoids full realloc
			int sizeBytes = elementCount * elementSize;
			GL.BindBuffer(BufferTarget.ShaderStorageBuffer, Handle);
			GL.ClearBufferSubData(BufferTarget.ShaderStorageBuffer,
				PixelInternalFormat.R32ui,
				IntPtr.Zero, new IntPtr(sizeBytes),
				PixelFormat.RedInteger, PixelType.UnsignedInt,
				IntPtr.Zero);
			GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);
		}

		/// <summary>Unbinds the binding point</summary>
		public void Unbind()
		{
			GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, bindingPoint, 0);
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			if (disposed) return;
			GL.DeleteBuffer(Handle);
			GC.SuppressFinalize(this);
			disposed = true;
		}

		~ShaderStorageBufferObject()
		{
			if (disposed) return;
			// Queue for GL-thread deletion (mirrors VBO finalizer pattern)
			lock (BaseRenderer.vboToDelete)
			{
				BaseRenderer.vboToDelete.Add(Handle);
			}
		}
	}
}
