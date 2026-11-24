using System;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>Colour format of an offscreen target.</summary>
	public enum RenderTargetFormat
	{
		/// <summary>8-bit RGBA, the normal scene/intermediate format.</summary>
		Sdr,
		/// <summary>16-bit float RGBA, for internal HDR and effects needing range.</summary>
		Hdr,
		/// <summary>16-bit float RGBA (explicit alias of <see cref="Hdr"/>).</summary>
		Rgba16f,
		/// <summary>16-bit float RG, useful for signed CoC / velocity in separable effects.</summary>
		Rg16f
	}

	/// <summary>
	/// An offscreen colour target with an optional depth buffer, used as an input/output of
	/// post-processing passes. Exposes the raw texture handle so passes can bind it as a sampler.
	/// </summary>
	public sealed class RenderTarget : IDisposable
	{
		public int Framebuffer { get; private set; }
		public int ColorTexture { get; private set; }
		/// <summary>Second colour attachment, used by complex-number blur passes (imaginary part).</summary>
		public int ColorTexture2 { get; private set; }
		public int AttachmentCount { get; private set; }
		/// <summary>Depth attachment as a texture, so effects such as DOF can sample scene depth.</summary>
		public int DepthTexture { get; private set; }
		public int Width { get; private set; }
		public int Height { get; private set; }
		public bool HasDepth { get; private set; }

		public RenderTargetFormat Format { get; private set; }
		public bool Mipmaps { get; private set; }

		/// <summary>True when the colour attachment is a 16-bit float target.</summary>
		public bool Hdr => Format == RenderTargetFormat.Hdr || Format == RenderTargetFormat.Rgba16f || Format == RenderTargetFormat.Rg16f;

		private readonly bool nearest;
		internal string PoolKey { get; set; }
		private bool disposed;

		public RenderTarget(int width, int height, bool withDepth = false, bool hdr = false)
			: this(width, height, withDepth, hdr ? RenderTargetFormat.Hdr : RenderTargetFormat.Sdr, false, false)
		{
		}

		public RenderTarget(int width, int height, bool withDepth, RenderTargetFormat format, bool mipmaps = false, bool nearest = false)
			: this(width, height, withDepth, format, mipmaps, nearest, 1)
		{
		}

		public RenderTarget(int width, int height, bool withDepth, RenderTargetFormat format,
			bool mipmaps, bool nearest, int attachments)
		{
			Width = Math.Max(1, width);
			Height = Math.Max(1, height);
			HasDepth = withDepth;
			Format = format;
			Mipmaps = mipmaps;
			this.nearest = nearest;
			AttachmentCount = Math.Max(1, Math.Min(2, attachments));
			Allocate();
		}

		private void Allocate()
		{
			PixelInternalFormat internalFormat;
			OpenTK.Graphics.OpenGL.PixelFormat pixelFormat;
			PixelType pixelType;
			switch (Format)
			{
				case RenderTargetFormat.Hdr:
				case RenderTargetFormat.Rgba16f:
					internalFormat = PixelInternalFormat.Rgba16f;
					pixelFormat = OpenTK.Graphics.OpenGL.PixelFormat.Rgba;
					pixelType = PixelType.Float;
					break;
				case RenderTargetFormat.Rg16f:
					internalFormat = PixelInternalFormat.Rg16f;
					pixelFormat = OpenTK.Graphics.OpenGL.PixelFormat.Rg;
					pixelType = PixelType.Float;
					break;
				default:
					internalFormat = PixelInternalFormat.Rgba8;
					pixelFormat = OpenTK.Graphics.OpenGL.PixelFormat.Rgba;
					pixelType = PixelType.UnsignedByte;
					break;
			}

			Framebuffer = GL.GenFramebuffer();
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
			// Be explicit: a draw buffer of NONE left over from a shadow FBO would make every
			// full-screen pass a no-op and present an empty (black) target.
			GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
			GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

			ColorTexture = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2D, ColorTexture);
			GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, Width, Height, 0, pixelFormat, pixelType, IntPtr.Zero);
			// Allocate every mip level up front. glGenerateMipmap used to do this implicitly,
			// but the compute downsampler writes levels with imageStore and needs real storage.
			if (Mipmaps)
			{
				AllocateMipStorage(internalFormat, pixelFormat, pixelType);
			}
			int minFilter = Mipmaps
				? (nearest ? (int)TextureMinFilter.NearestMipmapLinear : (int)TextureMinFilter.LinearMipmapLinear)
				: (nearest ? (int)TextureMinFilter.Nearest : (int)TextureMinFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, minFilter);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)(nearest ? TextureMagFilter.Nearest : TextureMagFilter.Linear));
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
				TextureTarget.Texture2D, ColorTexture, 0);

			if (AttachmentCount == 2)
			{
				ColorTexture2 = GL.GenTexture();
				GL.BindTexture(TextureTarget.Texture2D, ColorTexture2);
				GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, Width, Height, 0, pixelFormat, pixelType, IntPtr.Zero);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, minFilter);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)(nearest ? TextureMagFilter.Nearest : TextureMagFilter.Linear));
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
				GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1,
					TextureTarget.Texture2D, ColorTexture2, 0);
				GL.DrawBuffers(2, new[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1 });
			}
			else
			{
				GL.DrawBuffers(1, new[] { DrawBuffersEnum.ColorAttachment0 });
			}

			if (HasDepth)
			{
				// A depth *texture* (rather than a renderbuffer) lets post effects read scene depth.
				DepthTexture = GL.GenTexture();
				GL.BindTexture(TextureTarget.Texture2D, DepthTexture);
				// Linear depth sampling smooths the CoC ramp and removes per-pixel popping
				// when the camera moves. It is still compared as a plain float (no shadow compare).
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.None);
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, Width, Height, 0,
					OpenTK.Graphics.OpenGL.PixelFormat.DepthComponent, PixelType.UnsignedInt, IntPtr.Zero);
				GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
					TextureTarget.Texture2D, DepthTexture, 0);
			}

			GL.BindTexture(TextureTarget.Texture2D, 0);
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		}

		/// <summary>Allocates mip storage level by level; works on the GL 4.1 baseline too.</summary>
		private void AllocateMipStorage(PixelInternalFormat internalFormat,
			OpenTK.Graphics.OpenGL.PixelFormat pixelFormat, PixelType pixelType)
		{
			int width = Width;
			int height = Height;
			int level = 1;
			while (width > 1 || height > 1)
			{
				width = Math.Max(1, width / 2);
				height = Math.Max(1, height / 2);
				GL.TexImage2D(TextureTarget.Texture2D, level, internalFormat, width, height, 0,
					pixelFormat, pixelType, IntPtr.Zero);
				level++;
			}
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, level - 1);
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

		/// <summary>Rebuilds the mip chain after the target has been rendered into.</summary>
		public void GenerateMipmaps()
		{
			if (!Mipmaps || ColorTexture == 0)
			{
				return;
			}
			if (!TextureBinder.DirectStateAccess)
			{
				GL.BindTexture(TextureTarget.Texture2D, ColorTexture);
			}
			TextureBinder.GenerateMipmap(ColorTexture);
			if (!TextureBinder.DirectStateAccess)
			{
				GL.BindTexture(TextureTarget.Texture2D, 0);
			}
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
			if (ColorTexture2 != 0)
			{
				GL.DeleteTexture(ColorTexture2);
				ColorTexture2 = 0;
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
