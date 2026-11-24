using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenBveApi.Objects;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace LibRender2
{
	/// <summary>
	/// AZDO-style uniform buffer: on GL 4.4+ the store is allocated with BufferStorage and mapped
	/// persistently once, so per-frame animation matrix updates are a memcpy with no buffer bind
	/// or reallocation. On GL 4.1 it falls back to BufferData/BufferSubData with identical behaviour.
	/// </summary>
	public sealed class PersistentUniformBuffer : IDisposable
	{
		private readonly BufferTarget target;
		private IntPtr mapped;
		private bool persistent;
		private bool disposed;

		public int Handle { get; private set; }
		public int Capacity { get; private set; }
		public bool IsPersistent => persistent;

		internal PersistentUniformBuffer(GlCapabilities capabilities, int capacityBytes, BufferTarget target)
		{
			this.target = target;
			Capacity = Math.Max(1, capacityBytes);
			Handle = GL.GenBuffer();

			if (capabilities != null && capabilities.IsAtLeast(4, 4))
			{
				GL.BindBuffer(target, Handle);
				GL.BufferStorage(target, Capacity, IntPtr.Zero,
					BufferStorageFlags.MapWriteBit | BufferStorageFlags.MapPersistentBit |
					BufferStorageFlags.MapCoherentBit | BufferStorageFlags.DynamicStorageBit);
				mapped = GL.MapBufferRange(target, IntPtr.Zero, Capacity,
					BufferAccessMask.MapWriteBit | BufferAccessMask.MapPersistentBit | BufferAccessMask.MapCoherentBit);
				GL.BindBuffer(target, 0);
				if (mapped == IntPtr.Zero)
				{
					// Driver refused the mapping - undo the immutable store and use the GL 4.1 path.
					GL.DeleteBuffer(Handle);
					Handle = GL.GenBuffer();
				}
				else
				{
					persistent = true;
				}
			}

			if (!persistent)
			{
				GL.BindBuffer(target, Handle);
				GL.BufferData(target, Capacity, IntPtr.Zero, BufferUsageHint.DynamicDraw);
				GL.BindBuffer(target, 0);
			}
		}

		public unsafe void Upload(Matrix4[] matrices)
		{
			if (matrices == null || matrices.Length == 0)
			{
				return;
			}
			int bytes = matrices.Length * Marshal.SizeOf(typeof(Matrix4));
			if (bytes > Capacity)
			{
				throw new InvalidOperationException("Animation matrix buffer is too small.");
			}
			if (persistent)
			{
				fixed (Matrix4* source = matrices)
				{
					System.Buffer.MemoryCopy(source, (void*)mapped, bytes, bytes);
				}
				return;
			}
			GL.BindBuffer(target, Handle);
			GL.BufferSubData(target, IntPtr.Zero, bytes, matrices);
			GL.BindBuffer(target, 0);
		}

		/// <summary>Uploads raw floats, used by the PBR material block (one memcpy on GL 4.4+).</summary>
		public unsafe void Upload(float[] values)
		{
			if (values == null || values.Length == 0)
			{
				return;
			}
			int bytes = values.Length * sizeof(float);
			if (bytes > Capacity)
			{
				throw new InvalidOperationException("Uniform buffer is too small for the supplied data.");
			}
			if (persistent)
			{
				fixed (float* source = values)
				{
					System.Buffer.MemoryCopy(source, (void*)mapped, bytes, bytes);
				}
				return;
			}
			GL.BindBuffer(target, Handle);
			GL.BufferSubData(target, IntPtr.Zero, bytes, values);
			GL.BindBuffer(target, 0);
		}

		public void Dispose()
		{
			if (!disposed)
			{
				if (persistent)
				{
					GL.BindBuffer(target, Handle);
					GL.UnmapBuffer(target);
					GL.BindBuffer(target, 0);
				}
				GL.DeleteBuffer(Handle);
				disposed = true;
				GC.SuppressFinalize(this);
			}
		}
	}

	/// <summary>Keeps one buffer per animation matrix handle (shared by both shader passes).</summary>
	public static class UniformBufferStore
	{
		private static readonly Dictionary<int, PersistentUniformBuffer> buffers = new Dictionary<int, PersistentUniformBuffer>();

		public static PersistentUniformBuffer Upload(BaseRenderer renderer, ObjectState state, Matrix4[] matrices)
		{
			int bytes = matrices.Length * Marshal.SizeOf(typeof(Matrix4));
			PersistentUniformBuffer buffer = null;
			if (state.MatrixBufferIndex == 0 || !buffers.TryGetValue(state.MatrixBufferIndex, out buffer) || buffer.Capacity < bytes)
			{
				if (buffer != null)
				{
					buffers.Remove(state.MatrixBufferIndex);
					buffer.Dispose();
				}
				buffer = new PersistentUniformBuffer(renderer.Capabilities, bytes, BufferTarget.UniformBuffer);
				state.MatrixBufferIndex = buffer.Handle;
				buffers[buffer.Handle] = buffer;
			}
			buffer.Upload(matrices);
			return buffer;
		}

		public static void DisposeAll()
		{
			foreach (PersistentUniformBuffer buffer in buffers.Values)
			{
				buffer.Dispose();
			}
			buffers.Clear();
		}
	}
}
