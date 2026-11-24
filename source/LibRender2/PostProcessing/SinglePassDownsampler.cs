using System;
using LibRender2.Shaders;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// FidelityFX-style single-pass mip chain generation. A GL 4.3 compute shader builds all
	/// mip levels in one dispatch using LDS plus a global atomic counter; every other context
	/// (including the GL 4.1 baseline) uses glGenerateMipmap instead. The compute path is
	/// validated with a canary dispatch on creation and silently retires to the fallback on
	/// any shader or driver failure. Only SDR RGBA8 targets are supported by the compute path.
	/// </summary>
	public sealed class SinglePassDownsampler : IDisposable
	{
		private const int MaxMipLevels = 12;
		private const int CounterUnit = 0;
		private const MemoryBarrierFlags ImageBarriers =
			MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit;

		private readonly BaseRenderer renderer;
		private ComputeShader shader;
		private int counterBuffer;
		private bool available;
		private bool disposed;

		public bool Available => available;
		public bool UsingFallback => !available;

		public SinglePassDownsampler(BaseRenderer renderer)
		{
			this.renderer = renderer;
			if (renderer.Capabilities == null || !renderer.Capabilities.IsAtLeast(4, 3))
			{
				Log("GL 4.3 unavailable; using glGenerateMipmap fallback.");
				return;
			}
			try
			{
				Validate();
				available = true;
				Log("GL 4.3 compute path available.");
			}
			catch (Exception ex)
			{
				GL.GetError();
				Log("Compute validation failed: " + ex.Message + "; using glGenerateMipmap fallback.");
				ReleaseGpu();
			}
		}

		/// <summary>Throws if the compute path cannot run on this driver.</summary>
		private void Validate()
		{
			shader = new ComputeShader("post_spd");
			GL.GenBuffers(1, out counterBuffer);
			GL.BindBuffer(BufferTarget.AtomicCounterBuffer, counterBuffer);
			GL.BufferData(BufferTarget.AtomicCounterBuffer, (IntPtr)4, IntPtr.Zero, BufferUsageHint.DynamicDraw);
			GL.BindBuffer(BufferTarget.AtomicCounterBuffer, 0);

			// 64x64 canary with storage for mips 1..6, so every imageStore has a destination.
			int canary = GL.GenTexture();
			GL.ActiveTexture(TextureUnit.Texture0);
			GL.BindTexture(TextureTarget.Texture2D, canary);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 64, 64, 0,
				OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
			for (int level = 1; level <= 6; level++)
			{
				int size = 64 >> level;
				GL.TexImage2D(TextureTarget.Texture2D, level, PixelInternalFormat.Rgba8, size, size, 0,
					OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
			}
			for (int level = 1; level <= MaxMipLevels; level++)
			{
				GL.BindImageTexture(level, level <= 6 ? canary : 0, level <= 6 ? level : 0,
					false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba8);
			}
			ResetCounter();
			TextureBinder.Bind(0, canary);
			shader.Activate();
			shader.SetInt("uMipCount", 6);
			shader.SetInt("uNumGroups", 1);
			shader.Dispatch(1, 1, 1);
			GL.MemoryBarrier(ImageBarriers);
			ComputeShader.Deactivate();
			UnbindImages();

			ErrorCode error = GL.GetError();
			GL.BindTexture(TextureTarget.Texture2D, 0);
			GL.DeleteTexture(canary);
			if (error != ErrorCode.NoError)
			{
				throw new InvalidOperationException("Canary dispatch reported GL error " + error);
			}
		}

		private void ResetCounter()
		{
			uint zero = 0;
			GL.BindBuffer(BufferTarget.AtomicCounterBuffer, counterBuffer);
			GL.BufferSubData(BufferTarget.AtomicCounterBuffer, IntPtr.Zero, (IntPtr)4, ref zero);
			GL.BindBufferBase(BufferTarget.AtomicCounterBuffer, CounterUnit, counterBuffer);
			GL.BindBuffer(BufferTarget.AtomicCounterBuffer, 0);
		}

		private static void UnbindImages()
		{
			for (int level = 1; level <= MaxMipLevels; level++)
			{
				GL.BindImageTexture(level, 0, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba8);
			}
		}

		private static int MipLevelCount(int width, int height)
		{
			int max = Math.Max(width, height);
			int levels = 1;
			while (max > 1 && levels < MaxMipLevels)
			{
				max >>= 1;
				levels++;
			}
			return levels;
		}

		/// <summary>
		/// Generates the mip chain of an SDR RGBA8 target in one dispatch, or falls back to
		/// glGenerateMipmap. Targets smaller than one 64x64 tile or HDR targets always fall back.
		/// </summary>
		public void Downsample(RenderTarget target)
		{
			if (target == null || !target.Mipmaps || target.ColorTexture == 0)
			{
				return;
			}
			if (!available || target.Hdr || target.Width < 64 || target.Height < 64)
			{
				target.GenerateMipmaps();
				return;
			}
			try
			{
				int levels = MipLevelCount(target.Width, target.Height);
				int groupsX = (target.Width + 63) / 64;
				int groupsY = (target.Height + 63) / 64;
				ResetCounter();
				TextureBinder.Bind(0, target.ColorTexture);
				for (int level = 1; level < levels; level++)
				{
					GL.BindImageTexture(level, target.ColorTexture, level, false, 0,
						TextureAccess.ReadWrite, SizedInternalFormat.Rgba8);
				}
				shader.Activate();
				shader.SetInt("uMipCount", levels - 1);
				shader.SetInt("uNumGroups", groupsX * groupsY);
				shader.Dispatch(groupsX, groupsY, 1);
				GL.MemoryBarrier(ImageBarriers);
				ComputeShader.Deactivate();
				UnbindImages();

				ErrorCode error = GL.GetError();
				if (error != ErrorCode.NoError)
				{
					throw new InvalidOperationException("Downsample dispatch reported GL error " + error);
				}
			}
			catch (Exception ex)
			{
				Log("Compute downsample failed, reverting to GL 4.1: " + ex.Message);
				UnbindImages();
				ReleaseGpu();
				available = false;
				target.GenerateMipmaps();
			}
		}

		private void ReleaseGpu()
		{
			shader?.Dispose();
			shader = null;
			if (counterBuffer != 0)
			{
				GL.DeleteBuffers(1, ref counterBuffer);
				counterBuffer = 0;
			}
		}

		private void Log(string message)
		{
			renderer.fileSystem?.AppendToLogFile("[SPD] " + message);
		}

		public void Dispose()
		{
			if (!disposed)
			{
				ReleaseGpu();
				disposed = true;
				GC.SuppressFinalize(this);
			}
		}
	}
}
