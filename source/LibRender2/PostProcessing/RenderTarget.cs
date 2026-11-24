using System;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// An offscreen colour target with an optional depth buffer, used as an input/output of
	/// post-processing passes. Exposes the raw texture handle so passes can bind it as a sampler.
	/// </summary>
	public sealed class RenderTarget : IDisposable
	{
		public int Framebuffer { get; private set; }
		public int ColorTexture { get; private set; }
		/// <summary>Depth attachment as a texture, so effects such as DOF can sample scene depth.</summary>
		public int DepthTexture { get; private set; }
		public int Width { get; private set; }
		public int Height { get; private set; }
		public bool HasDepth { get; private set; }

		/// <summary>True when the colour attachment is a 16-bit float (internal HDR) target.</summary>
		public bool Hdr { get; private set; }

		private bool disposed;

		public RenderTarget(int width, int height, bool withDepth = false, bool hdr = false)
		{
			Width = Math.Max(1, width);
			Height = Math.Max(1, height);
			HasDepth = withDepth;
			Hdr = hdr;
			Allocate();
		}

		private void Allocate()
		{
			Framebuffer = GL.GenFramebuffer();
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
			// Be explicit: a draw buffer of NONE left over from a shadow FBO would make every
			// full-screen pass a no-op and present an empty (black) target.
			GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
			GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

			ColorTexture = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2D, ColorTexture);
			if (Hdr)
			{
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, Width, Height, 0,
					OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
			}
			else
			{
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, Width, Height, 0,
					OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
			}
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
				TextureTarget.Texture2D, ColorTexture, 0);

			if (HasDepth)
			{
				// A depth *texture* (rather than a renderbuffer) lets post effects read scene depth.
				DepthTexture = GL.GenTexture();
				GL.BindTexture(TextureTarget.Texture2D, DepthTexture);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.None);
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, Width, Height, 0,
					PixelFormat.DepthComponent, PixelType.UnsignedInt, IntPtr.Zero);
				GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
					TextureTarget.Texture2D, DepthTexture, 0);
			}

			GL.BindTexture(TextureTarget.Texture2D, 0);
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		}

		public bool IsComplete()
		{
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
			bool complete = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete;
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
			return complete;
		}

		public void Bind()
		{
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
			GL.Viewport(0, 0, Width, Height);
		}

		public void Resize(int width, int height)
		{
			width = Math.Max(1, width);
			height = Math.Max(1, height);
			if (width == Width && height == Height)
			{
				return;
			}
			Release();
			Width = width;
			Height = height;
			Allocate();
		}

		private void Release()
		{
			if (ColorTexture != 0)
			{
				GL.DeleteTexture(ColorTexture);
				ColorTexture = 0;
			}
			if (DepthTexture != 0)
			{
				GL.DeleteTexture(DepthTexture);
				DepthTexture = 0;
			}
			if (Framebuffer != 0)
			{
				GL.DeleteFramebuffer(Framebuffer);
				Framebuffer = 0;
			}
		}

		public void Dispose()
		{
			if (!disposed)
			{
				Release();
				disposed = true;
				GC.SuppressFinalize(this);
			}
		}
	}
}
