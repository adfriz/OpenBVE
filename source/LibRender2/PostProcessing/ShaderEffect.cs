using System;
using System.Diagnostics;
using LibRender2.Shaders;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// A post step from two GLSL files, no C# needed to add one.
	/// <remarks>
	/// One line: Post.Insert(Post.Count - 1, new ShaderEffect(renderer, vert, frag));
	/// Keep Tonemap last. Fragment uniforms are all optional:
	/// uniform sampler2D uTexture; // previous step
	/// uniform vec2 uResolution;   // screen size
	/// uniform float uTime;        // seconds since first frame
	/// Vert must use location 0 for position, 1 for uv — just copy tonemap.vert.
	/// </remarks>
	public class ShaderEffect : IPostEffect, IDisposable
	{
		private readonly BaseRenderer renderer;
		private readonly string vertexPath;
		private readonly string fragmentPath;
		private readonly Stopwatch clock = new Stopwatch();

		private AbstractShader program;
		private int uTexture = -1;
		private int uResolution = -1;
		private int uTime = -1;
		private readonly FullscreenQuad quad = new FullscreenQuad();
		private int fbo;
		private int texture;
		private int texWidth = -1;
		private int texHeight = -1;
		private bool offForGood;

		public ShaderEffect(BaseRenderer renderer, string vertexPath, string fragmentPath)
		{
			this.renderer = renderer;
			this.vertexPath = vertexPath;
			this.fragmentPath = fragmentPath;
		}

		// Compile on first use, when a GL context exists. Broken shader? Stay off quietly.
		private bool Ensure()
		{
			if (offForGood)
			{
				return false;
			}
			if (program != null)
			{
				return true;
			}
			try
			{
				program = new AbstractShader(renderer, vertexPath, fragmentPath, false, true);
				uTexture = GL.GetUniformLocation(program.Handle, "uTexture");
				uResolution = GL.GetUniformLocation(program.Handle, "uResolution");
				uTime = GL.GetUniformLocation(program.Handle, "uTime");
				quad.Ensure();
				clock.Start();
				return true;
			}
			catch
			{
				Dispose();
				offForGood = true;
				return false;
			}
		}

		// Draw into our own HDR target so the next step can read it.
		public int Apply(int inputTexture)
		{
			if (inputTexture == 0 || !Ensure())
			{
				return inputTexture;
			}
			int width = Math.Max(renderer.Screen.Width, 1);
			int height = Math.Max(renderer.Screen.Height, 1);
			if (!EnsureTarget(width, height))
			{
				return inputTexture;
			}
			AbstractShader previous = renderer.CurrentShader;
			if (previous != null)
			{
				previous.IsActive = false;
			}
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
			GL.Viewport(0, 0, width, height);
			GL.UseProgram(program.Handle);
			GL.ActiveTexture(TextureUnit.Texture0);
			GL.BindTexture(TextureTarget.Texture2D, inputTexture);
			if (uTexture != -1)
			{
				GL.ProgramUniform1(program.Handle, uTexture, 0);
			}
			if (uResolution != -1)
			{
				GL.ProgramUniform2(program.Handle, uResolution, width, height);
			}
			if (uTime != -1)
			{
				GL.ProgramUniform1(program.Handle, uTime, (float)clock.Elapsed.TotalSeconds);
			}
			GL.Disable(EnableCap.DepthTest);
			GL.Disable(EnableCap.Blend);
			quad.Draw();
			GL.Enable(EnableCap.DepthTest);
			GL.UseProgram(0);
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
			GL.Viewport(0, 0, width, height);
			renderer.LastBoundTexture = null;
			renderer.lastVAO = -1;
			if (previous != null)
			{
				previous.Activate();
			}
			return texture;
		}

		// Own target per effect = chaining just works.
		private bool EnsureTarget(int width, int height)
		{
			if (fbo != 0 && width == texWidth && height == texHeight)
			{
				return true;
			}
			DeleteTarget();
			try
			{
				fbo = GL.GenFramebuffer();
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
				texture = GL.GenTexture();
				GL.BindTexture(TextureTarget.Texture2D, texture);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, width, height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.HalfFloat, IntPtr.Zero);
				GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
				if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
				{
					throw new InvalidOperationException("Post target incomplete.");
				}
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
				texWidth = width;
				texHeight = height;
				return true;
			}
			catch
			{
				DeleteTarget();
				return false;
			}
		}

		private void DeleteTarget()
		{
			if (fbo != 0)
			{
				Quiet(() => GL.DeleteFramebuffer(fbo));
				fbo = 0;
			}
			if (texture != 0)
			{
				Quiet(() => GL.DeleteTexture(texture));
				texture = 0;
			}
			texWidth = -1;
			texHeight = -1;
		}

		public void Dispose()
		{
			if (program != null)
			{
				try
				{
					program.Dispose();
				}
				catch
				{
				}
				program = null;
			}
			quad.Dispose();
			DeleteTarget();
		}

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
