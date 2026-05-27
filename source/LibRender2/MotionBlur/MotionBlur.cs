using System;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.MotionBlurs
{
	public class MotionBlur
	{
		private readonly BaseRenderer renderer;

		/// <summary>The pixel buffer used for rendering the motion blur</summary>
		/// <remarks>Must be static to avoid re-allocating the array memory every frame</remarks>
		private byte[] PixelBuffer;
		/// <summary>The OpenGL texture index from which the blurred image is rendered</summary>
		private int PixelBufferOpenGlTextureIndex;
		private int PixelBufferOpenGlDepthTextureIndex;
		private Shaders.MotionBlurShader motionBlurShader;
		private OpenBveApi.Math.Matrix4D previousViewProjection;
		private OpenBveApi.Math.Vector3 previousCameraPosition;
		private bool hasPrevMatrices;
		private int tempFbo;
		private int bufferWidth;
		private int bufferHeight;

		internal MotionBlur(BaseRenderer renderer)
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
			if (PixelBufferOpenGlDepthTextureIndex != 0)
			{
				GL.DeleteTextures(1, new[] { PixelBufferOpenGlDepthTextureIndex });
				PixelBufferOpenGlDepthTextureIndex = 0;
			}
			if (tempFbo != 0)
			{
				GL.DeleteFramebuffer(tempFbo);
				tempFbo = 0;
			}

			bufferWidth = renderer.Screen.Width;
			bufferHeight = renderer.Screen.Height;

			if (renderer.AvailableNewRenderer)
			{
				if (motionBlurShader == null)
				{
					motionBlurShader = new Shaders.MotionBlurShader(renderer, "motion_blur", "motion_blur", true);
				}

				// Color texture — resolved from MSAA backbuffer
				int[] a = new int[1];
				GL.GenTextures(1, a);
				PixelBufferOpenGlTextureIndex = a[0];
				GL.BindTexture(TextureTarget.Texture2D, PixelBufferOpenGlTextureIndex);
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, renderer.Screen.Width, renderer.Screen.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Linear);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

				// Depth texture — resolved from MSAA depth buffer
				GL.GenTextures(1, a);
				PixelBufferOpenGlDepthTextureIndex = a[0];
				GL.BindTexture(TextureTarget.Texture2D, PixelBufferOpenGlDepthTextureIndex);
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, renderer.Screen.Width, renderer.Screen.Height, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, IntPtr.Zero);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Nearest);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

				// Framebuffer resolving MSAA color and depth to single-sampled textures
				GL.GenFramebuffers(1, out tempFbo);
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, tempFbo);
				GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, PixelBufferOpenGlTextureIndex, 0);
				GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, PixelBufferOpenGlDepthTextureIndex, 0);
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

				hasPrevMatrices = false;
			}
			else
			{
				PixelBuffer = new byte[4 * renderer.Screen.Width * renderer.Screen.Height];
				int[] a = new int[1];

				GL.GenTextures(1, a);
				GL.BindTexture(TextureTarget.Texture2D, a[0]);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Linear);
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, renderer.Screen.Width, renderer.Screen.Height, 0, PixelFormat.Rgb, PixelType.UnsignedByte, PixelBuffer);
				PixelBufferOpenGlTextureIndex = a[0];
				GL.CopyTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgb, 0, 0, renderer.Screen.Width, renderer.Screen.Height, 0);
			}
		}

		/// <summary>This function renderers full-screen motion blur if selected</summary>
		public void RenderFullscreen(MotionBlurMode mode, double frameRate, double speed)
		{
			if (renderer.Screen.Minimized)
			{
				return;
			}

			if (bufferWidth != renderer.Screen.Width || bufferHeight != renderer.Screen.Height)
			{
				Initialize(mode);
			}

			if (renderer.AvailableNewRenderer)
			{
				RenderNewRenderer(mode, frameRate, speed);
			}
			else
			{
				if (renderer.currentOptions.ForceForwardsCompatibleContext)
				{
					return;
				}
				RenderLegacy(mode, frameRate, speed);
			}
		}

		private void RenderNewRenderer(MotionBlurMode mode, double frameRate, double speed)
		{
			if (motionBlurShader == null)
			{
				motionBlurShader = new Shaders.MotionBlurShader(renderer, "motion_blur", "motion_blur", true);
			}

			// Blit color and depth to FBO (resolving MSAA color & depth)
			GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
			GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, tempFbo);
			GL.BlitFramebuffer(0, 0, renderer.Screen.Width, renderer.Screen.Height, 0, 0, renderer.Screen.Width, renderer.Screen.Height, ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

			OpenBveApi.Math.Matrix4D currentViewProjection = renderer.CurrentViewMatrix * renderer.CurrentProjectionMatrix;
			OpenBveApi.Math.Matrix4D currentViewProjectionInverse = OpenBveApi.Math.Matrix4D.Invert(currentViewProjection);
			OpenBveApi.Math.Vector3 currentCameraPosition = renderer.Camera.AbsolutePosition;
			OpenBveApi.Math.Vector3 cameraDisplacement = OpenBveApi.Math.Vector3.Zero;

			if (!hasPrevMatrices)
			{
				previousViewProjection = currentViewProjection;
				previousCameraPosition = currentCameraPosition;
				hasPrevMatrices = true;
				return; // No blur on first frame
			}
			else
			{
				// Sign inversion matching Camera.TranslationMatrix: TranslationMatrix = (-value.X, -value.Y, value.Z)
				cameraDisplacement = new OpenBveApi.Math.Vector3(
					-(currentCameraPosition.X - previousCameraPosition.X),
					-(currentCameraPosition.Y - previousCameraPosition.Y),
					currentCameraPosition.Z - previousCameraPosition.Z
				);
			}

			// Quality settings
			int numSamples;
			float baseStrength;
			switch (mode)
			{
				case MotionBlurMode.Low:
					numSamples = 8;
					baseStrength = 0.40f;
					break;
				case MotionBlurMode.Medium:
					numSamples = 12;
					baseStrength = 0.70f;
					break;
				case MotionBlurMode.High:
					numSamples = 16;
					baseStrength = 1.10f;
					break;
				default:
					numSamples = 12;
					baseStrength = 0.70f;
					break;
			}

			double fps = frameRate > 0.0 ? frameRate : 60.0;
			float strength = (float)(baseStrength * (60.0 / fps));

			renderer.LastBoundTexture = null;
			GL.Disable(EnableCap.DepthTest);
			GL.Disable(EnableCap.Blend);
			GL.Disable(EnableCap.CullFace);

			motionBlurShader.Activate();

			GL.ActiveTexture(TextureUnit.Texture0);
			GL.BindTexture(TextureTarget.Texture2D, PixelBufferOpenGlTextureIndex);
			motionBlurShader.SetColorTexture(0);

			GL.ActiveTexture(TextureUnit.Texture1);
			GL.BindTexture(TextureTarget.Texture2D, PixelBufferOpenGlDepthTextureIndex);
			motionBlurShader.SetDepthTexture(1);

			motionBlurShader.SetCurrentViewProjectionInverse(currentViewProjectionInverse);
			motionBlurShader.SetPreviousViewProjection(previousViewProjection);
			motionBlurShader.SetCameraOffset(cameraDisplacement);
			motionBlurShader.SetNumSamples(numSamples);
			motionBlurShader.SetStrength(strength);

			renderer.dummyVao.Bind();
			GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
			renderer.dummyVao.UnBind();

			motionBlurShader.Deactivate();

			GL.ActiveTexture(TextureUnit.Texture1);
			GL.BindTexture(TextureTarget.Texture2D, 0);
			GL.ActiveTexture(TextureUnit.Texture0);
			GL.BindTexture(TextureTarget.Texture2D, 0);
			
			GL.Enable(EnableCap.Blend);
			GL.Enable(EnableCap.CullFace);

			previousViewProjection = currentViewProjection;
			previousCameraPosition = currentCameraPosition;
		}

		private void RenderLegacy(MotionBlurMode mode, double frameRate, double speed)
		{
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

				GL.MatrixMode(MatrixMode.Projection);
				GL.PushMatrix();
				GL.LoadIdentity();
				GL.Ortho(0.0f, renderer.Screen.Width, 0.0f, renderer.Screen.Height, -1.0f, 1.0f);

				GL.MatrixMode(MatrixMode.Modelview);
				GL.PushMatrix();
				GL.LoadIdentity();

				// render
				GL.BindTexture(TextureTarget.Texture2D, PixelBufferOpenGlTextureIndex);
				GL.Color4(1.0f, 1.0f, 1.0f, factor);
				GL.Begin(PrimitiveType.Polygon);
				GL.TexCoord2(0.0f, 0.0f);
				GL.Vertex2(0.0f, 0.0f);
				GL.TexCoord2(0.0f, 1.0f);
				GL.Vertex2(0.0f, renderer.Screen.Height);
				GL.TexCoord2(1.0f, 1.0f);
				GL.Vertex2(renderer.Screen.Width, renderer.Screen.Height);
				GL.TexCoord2(1.0f, 0.0f);
				GL.Vertex2(renderer.Screen.Width, 0.0f);
				GL.End();

				// finalize
				GL.PopMatrix();

				GL.MatrixMode(MatrixMode.Projection);
				GL.PopMatrix();
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
