//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the above disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the above disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
//ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
//ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// A colour render target: an offscreen texture with a dedicated framebuffer
	/// permanently attached to it, so binding only requires <see cref="Bind"/> (no
	/// per-pass framebuffer-texture re-attachment).
	/// </summary>
	internal sealed class RenderTarget
	{
		/// <summary>The colour texture sampled by passes.</summary>
		internal int Texture;

		/// <summary>The framebuffer the texture is attached to as colour attachment 0.</summary>
		internal int Fbo;

		internal int Width;
		internal int Height;
		internal PixelInternalFormat InternalFormat;

		internal RenderTarget(int width, int height, PixelInternalFormat internalFormat, PixelFormat format)
		{
			Width = width;
			Height = height;
			InternalFormat = internalFormat;
			int[] tex = new int[1];
			GL.GenTextures(1, tex);
			Texture = tex[0];
			GL.BindTexture(TextureTarget.Texture2D, Texture);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
			GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, width, height, 0, format, PixelType.UnsignedByte, IntPtr.Zero);

			int[] fbo = new int[1];
			GL.GenFramebuffers(1, fbo);
			Fbo = fbo[0];
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, Texture, 0);
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		}

		/// <summary>Binds this target's framebuffer for drawing.</summary>
		internal void Bind()
		{
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
		}

		internal void Dispose()
		{
			if (Texture != 0)
			{
				GL.DeleteTextures(1, new[] { Texture });
				Texture = 0;
			}
			if (Fbo != 0)
			{
				GL.DeleteFramebuffers(1, new[] { Fbo });
				Fbo = 0;
			}
		}
	}
}
