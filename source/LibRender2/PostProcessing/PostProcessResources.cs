//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
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
using LibRender2.Shaders;
using OpenBveApi.Math;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// Shared GL resources used by every post-processing pass.
	/// </summary>
	/// <remarks>
	/// Holds the fullscreen-triangle geometry and small texture/FBO helpers so individual
	/// passes do not each reallocate a VAO or reimplement the same boilerplate. A single
	/// instance is owned by the <see cref="PostProcessPipeline"/> and passed to passes.
	/// It also exposes the pipeline-owned scene-capture textures and the camera matrices
	/// needed by effects such as depth-reprojected motion blur.
	/// </remarks>
	public sealed class PostProcessResources : IDisposable
	{
		private readonly BaseRenderer renderer;

		/// <summary>The fullscreen triangle VAO (verts: -1,-1 / 3,-1 / -1,3).</summary>
		internal VertexArrayObject FullscreenVao;

		/// <summary>Pass-through copy shader used to blit a texture to the screen.</summary>
		internal Shader BlitShader;

		/// <summary>Cached uniform location for the blit shader's texture sampler.</summary>
		internal int BlitTextureLocation;

		/// <summary>The pipeline-owned scene colour texture (RGBA8) captured each frame.</summary>
		internal int SceneColorTexture;

		/// <summary>The pipeline-owned scene depth texture (DepthComponent24) captured each frame.</summary>
		internal int SceneDepthTexture;

		/// <summary>The current frame's view * projection matrix (column-major, for GLSL).</summary>
		internal Matrix4 CurrentViewProj;

		/// <summary>The previous frame's view * projection matrix (column-major, for GLSL).</summary>
		internal Matrix4 PreviousViewProj;

		/// <summary>Inverse of the current view * projection matrix (precomputed on CPU to avoid per-pixel inverse() in shaders).</summary>
		internal Matrix4 InverseViewProj;

		/// <summary>Centralized pool of reusable render targets shared by every pass.</summary>
		internal RenderTargetPool TargetPool;

		private int fullscreenBuffer;

		internal PostProcessResources(BaseRenderer renderer)
		{
			this.renderer = renderer;
		}

		/// <summary>Creates the shared fullscreen triangle geometry.</summary>
		internal void Initialize()
		{
			if (FullscreenVao != null)
			{
				return;
			}

			renderer.LastBoundTexture = null;
			FullscreenVao = new VertexArrayObject();
			FullscreenVao.Bind();
			float[] verts = { -1f, -1f, 3f, -1f, -1f, 3f };
			int[] buf = new int[1];
			GL.GenBuffers(1, buf);
			fullscreenBuffer = buf[0];
			GL.BindBuffer(BufferTarget.ArrayBuffer, fullscreenBuffer);
			GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);
			GL.EnableVertexAttribArray(0);
			GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 0, 0);
			FullscreenVao.UnBind();

			if (BlitShader == null)
			{
				BlitShader = new Shader(renderer, "post", "post_copy", true);
				BlitTextureLocation = GL.GetUniformLocation(BlitShader.Handle, "uTexture");
			}
		}

		/// <summary>Binds the fullscreen triangle for drawing.</summary>
		internal void BindFullscreen()
		{
			Initialize();
			FullscreenVao.Bind();
		}

		/// <summary>Draws the fullscreen triangle (VAO must already be bound).</summary>
		internal static void DrawFullscreen()
		{
			GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
		}

		/// <summary>Binds a texture to the given unit without disturbing other state.</summary>
		internal static void BindTextureUnit(int unit, int texture)
		{
			GL.ActiveTexture(TextureUnit.Texture0 + unit);
			GL.BindTexture(TextureTarget.Texture2D, texture);
			GL.ActiveTexture(TextureUnit.Texture0);
		}

		/// <summary>Creates (or recreates) a 2D colour texture with linear/clamp sampling.</summary>
		internal static void CreateTexture(ref int handle, int width, int height, PixelInternalFormat internalFormat, PixelFormat format)
		{
			if (handle != 0)
			{
				GL.DeleteTextures(1, new[] { handle });
			}

			int[] a = new int[1];
			GL.GenTextures(1, a);
			GL.BindTexture(TextureTarget.Texture2D, a[0]);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
			GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, width, height, 0, format, PixelType.UnsignedByte, IntPtr.Zero);
			handle = a[0];
		}

		private bool disposed;

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}

			if (FullscreenVao != null)
			{
				FullscreenVao.Dispose();
				FullscreenVao = null;
			}

			BlitShader?.Dispose();
			BlitShader = null;

			TargetPool?.Dispose();
			TargetPool = null;

			if (fullscreenBuffer != 0)
			{
				GL.DeleteBuffers(1, new[] { fullscreenBuffer });
				fullscreenBuffer = 0;
			}

			disposed = true;
			GC.SuppressFinalize(this);
		}
	}
}
