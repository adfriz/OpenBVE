using System;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// Scene render target for the post-processing chain.
	/// Extends the existing <see cref="FrameBufferObject"/> wrapper: color is
	/// RGBA8 and depth is a samplable DepthComponent24 texture, so later
	/// passes can sample both.
	/// </summary>
	public class SceneFrameBuffer : FrameBufferObject
	{
		public int Width { get; private set; }
		public int Height { get; private set; }
		public int ColorTextureHandle { get; private set; }
		public int DepthTextureHandle { get; private set; }
		public bool IsComplete { get; private set; }

		private bool disposed;

		/// <summary>
		/// (Re)allocates color + depth textures for the given size.
		/// No-op when the size is unchanged and the buffer is complete.
		/// Failures clear <see cref="IsComplete"/> so the manager falls back
		/// to direct-to-screen rendering.
		/// </summary>
		public void Ensure(int width, int height)
		{
			if (width <= 0 || height <= 0)
			{
				return;
			}
			if (width == Width && height == Height && IsComplete)
			{
				return;
			}
			try
			{
				Bind();
				SetTextureBuffer(TargetBuffer.Color, PixelInternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte, width, height);
				SetTextureBuffer(TargetBuffer.Depth, PixelInternalFormat.DepthComponent24, PixelFormat.DepthComponent, PixelType.UnsignedInt, width, height);
				DrawBuffers(new DrawBuffersEnum[] { DrawBuffersEnum.ColorAttachment0 });
				Width = width;
				Height = height;
				ColorTextureHandle = GetColorTextureHandle();
				DepthTextureHandle = GetDepthTextureHandle();
				IsComplete = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete;
			}
			catch
			{
				IsComplete = false;
			}
			finally
			{
				try
				{
					UnBind();
				}
				catch
				{
				}
			}
		}

		/// <summary>Releases the FBO and its textures. Requires a current GL context.</summary>
		public override void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			ColorTextureHandle = 0;
			DepthTextureHandle = 0;
			Width = 0;
			Height = 0;
			IsComplete = false;
			try
			{
				base.Dispose();
			}
			catch
			{
				// ignored: dispose must not throw
			}
			GC.SuppressFinalize(this);
		}
	}
}
