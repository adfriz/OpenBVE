using LibRender2.Shaders;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// Last post step: HDR texture to screen. Real curves live here later.
	public class Tonemap : IPostEffect, System.IDisposable
	{
		private readonly BaseRenderer renderer;

		/// 1 = copy as-is.
		public float Exposure = 1.0f;

		private AbstractShader program;
		private int uTexture = -1;
		private int uExposure = -1;
		private readonly FullscreenQuad quad = new FullscreenQuad();
		private bool offForGood;

		internal Tonemap(BaseRenderer renderer)
		{
			this.renderer = renderer;
		}

		// Compile once. Fail quiet and keep showing the last frame.
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
				program = new AbstractShader(renderer, "tonemap", "tonemap", true, true);
				uTexture = GL.GetUniformLocation(program.Handle, "uHdrBuffer");
				uExposure = GL.GetUniformLocation(program.Handle, "uExposure");
				quad.Ensure();
				return true;
			}
			catch
			{
				Dispose();
				offForGood = true;
				return false;
			}
		}

		// 0 means HDR is off, nothing to draw.
		public int Apply(int inputTexture)
		{
			if (inputTexture == 0 || !Ensure())
			{
				return inputTexture;
			}
			AbstractShader previous = renderer.CurrentShader;
			if (previous != null)
			{
				previous.IsActive = false;
			}
			GL.UseProgram(program.Handle);
			GL.ActiveTexture(TextureUnit.Texture0);
			GL.BindTexture(TextureTarget.Texture2D, inputTexture);
			GL.ProgramUniform1(program.Handle, uTexture, 0);
			GL.ProgramUniform1(program.Handle, uExposure, Exposure);
			GL.Disable(EnableCap.DepthTest);
			GL.Disable(EnableCap.Blend);
			quad.Draw();
			GL.Enable(EnableCap.DepthTest);
			GL.UseProgram(0);
			renderer.LastBoundTexture = null;
			renderer.lastVAO = -1;
			if (previous != null)
			{
				previous.Activate();
			}
			return inputTexture;
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
		}
	}
}
