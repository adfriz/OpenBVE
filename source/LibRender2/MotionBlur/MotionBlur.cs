using System;
using OpenBveApi.Colors;
using OpenBveApi.Math;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.MotionBlurs
{
	public class MotionBlur
	{
		private readonly RendererCore renderer;

		/// <summary>The pixel buffer used for rendering the motion blur</summary>
		/// <remarks>Must be static to avoid re-allocating the array memory every frame</remarks>
		private byte[] PixelBuffer;
		/// <summary>The OpenGL texture index from which the blurred image is rendered</summary>
		private int PixelBufferOpenGlTextureIndex;

		internal MotionBlur(RendererCore renderer)
		{
			this.renderer = renderer;
		}

		/// <summary>Initializes motion blur</summary>
		public void Initialize(MotionBlurMode mode)
		{
			if (mode == MotionBlurMode.None)
			{
				return;
			}
			renderer.LastBoundTexture = null;
			if (PixelBufferOpenGlTextureIndex != 0)
			{
				GL.DeleteTextures(1, new[] { PixelBufferOpenGlTextureIndex });
				PixelBufferOpenGlTextureIndex = 0;
			}

			PixelBuffer = new byte[4 * renderer.Screen.Width * renderer.Screen.Height];
			int[] a = new int[1];

			GL.GenTextures(1, a);
			GL.BindTexture(TextureTarget.Texture2D, a[0]);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Linear);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, renderer.Screen.Width, renderer.Screen.Height, 0, PixelFormat.Rgb, PixelType.UnsignedByte, PixelBuffer);
			PixelBufferOpenGlTextureIndex = a[0];
			GL.CopyTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgb, 0, 0, renderer.Screen.Width, renderer.Screen.Height, 0);
		}

		/// <summary>This function renderers full-screen motion blur if selected</summary>
		public void RenderFullscreen(MotionBlurMode mode, double frameRate, double speed)
		{
			if (renderer.Screen.Minimized || renderer.currentOptions.ForceForwardsCompatibleContext)
			{
				/*
		         * HACK:
		         * This breaks if minimized or using a forwards compatible context, even if we don't reset the W / H values
		         */
				return;
			}
			renderer.LastBoundTexture = null;
			GL.Enable(EnableCap.Texture2D);

			// render
			if (PixelBufferOpenGlTextureIndex >= 0)
			{
				double strength;

				switch (mode)
				{
					case MotionBlurMode.Low: strength = 0.0025; break;
					case MotionBlurMode.Medium: strength = 0.0040; break;
					case MotionBlurMode.High: strength = 0.0064; break;
					default: strength = 0.0040; break;
				}

				double denominator = strength * frameRate * Math.Sqrt(speed);
				float factor;

				if (denominator > 0.001)
				{
					factor = (float)Math.Exp(-1.0 / denominator);
				}
				else
				{
					factor = 0.0f;
				}

				// initialize
				renderer.SetBlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

				// render
				renderer.Rectangle.Draw(PixelBufferOpenGlTextureIndex, Vector2.Null, new Vector2(renderer.Screen.Width, renderer.Screen.Height), new Color128(1.0f, 1.0f, 1.0f, factor));
			}

			// retrieve buffer
			{
				GL.BindTexture(TextureTarget.Texture2D, PixelBufferOpenGlTextureIndex);
				GL.CopyTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgb8, 0, 0, renderer.Screen.Width, renderer.Screen.Height, 0);
			}
			GL.Disable(EnableCap.Texture2D);
		}
	}
}
