using System;
using OpenTK.Graphics.OpenGL;
using LibRender2.Shaders;

namespace LibRender2.PostProcessing
{
	public class PostProcessRenderer : IDisposable
	{
		private readonly BaseRenderer Renderer;
		private int sceneFBO, sceneColorTex, sceneDepthTex;
		private int ssaoFBO, ssaoTex; // Fallback for GL 4.1
		private int width, height;
		public int Width => width;
		public int Height => height;

		public bool IsComputeSupported { get; private set; }
		public ComputeShader SSAOComputeShader { get; private set; }
		public Shader SSAOFragmentShader { get; private set; }
		public Shader CombineShader { get; private set; }

		public PostProcessRenderer(BaseRenderer renderer, int width, int height)
		{
			this.Renderer = renderer;
			this.width = width;
			this.height = height;

			// Detect OpenGL version
			string version = GL.GetString(StringName.Version);
			IsComputeSupported = version.StartsWith("4.3") || version.StartsWith("4.4") ||
			                     version.StartsWith("4.5") || version.StartsWith("4.6");

			InitializeBuffers();
			LoadShaders();
		}

		private void InitializeBuffers()
		{
			// 1. Scene FBO (Capture Color + Depth from opaque pass)
			GL.GenFramebuffers(1, out sceneFBO);
			GL.GenTextures(1, out sceneColorTex);
			GL.GenTextures(1, out sceneDepthTex);

			GL.BindFramebuffer(FramebufferTarget.Framebuffer, sceneFBO);

			// Color Attachment
			GL.BindTexture(TextureTarget.Texture2D, sceneColorTex);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, width, height, 0,
				OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
				TextureTarget.Texture2D, sceneColorTex, 0);

			// Depth Attachment
			GL.BindTexture(TextureTarget.Texture2D, sceneDepthTex);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent32f, width, height, 0,
				OpenTK.Graphics.OpenGL.PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
				TextureTarget.Texture2D, sceneDepthTex, 0);

			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

			// 2. SSAO FBO (Fallback GL 4.1)
			if (!IsComputeSupported)
			{
				GL.GenFramebuffers(1, out ssaoFBO);
				GL.GenTextures(1, out ssaoTex);
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, ssaoFBO);
				GL.BindTexture(TextureTarget.Texture2D, ssaoTex);
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, width, height, 0,
					OpenTK.Graphics.OpenGL.PixelFormat.Red, PixelType.UnsignedByte, IntPtr.Zero);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
				GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
					TextureTarget.Texture2D, ssaoTex, 0);
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
			}
		}

		private void LoadShaders()
		{
			if (IsComputeSupported)
			{
				SSAOComputeShader = new ComputeShader(Renderer, "SSAOCompute", true);
			}
			else
			{
				SSAOFragmentShader = new Shader(Renderer, "rectangle", "SSAOFragment", true);
			}
			CombineShader = new Shader(Renderer, "rectangle", "CombineSSAO", true);
		}

		public void BeginSceneCapture()
		{
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, sceneFBO);
			GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
		}

		public void EndSceneCapture()
		{
			GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, sceneFBO);
			GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
			GL.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		}

		public void ApplySSAO()
		{
			float nearClip = (float)Math.Max(0.01, Renderer.currentOptions.NearClipBase);
			float farClip = (float)Renderer.currentOptions.ViewingDistance;

			float tanFovYHalf = (float)Math.Tan(Renderer.Camera.VerticalViewingAngle * 0.5);
			float tanFovXHalf = (float)Math.Tan(Renderer.Camera.HorizontalViewingAngle * 0.5);

			if (IsComputeSupported)
			{
				SSAOComputeShader.Activate();
				GL.BindImageTexture(0, sceneColorTex, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba16f);

				GL.ActiveTexture(TextureUnit.Texture0);
				GL.BindTexture(TextureTarget.Texture2D, sceneDepthTex);
				int uDepthTexLoc = GL.GetUniformLocation(SSAOComputeShader.Handle, "uDepthTex");
				if (uDepthTexLoc != -1) GL.Uniform1(uDepthTexLoc, 0);

				int uScreenSizeLoc = GL.GetUniformLocation(SSAOComputeShader.Handle, "uScreenSize");
				if (uScreenSizeLoc != -1) GL.Uniform2(uScreenSizeLoc, (float)width, (float)height);

				int uNearLoc = GL.GetUniformLocation(SSAOComputeShader.Handle, "uNear");
				if (uNearLoc != -1) GL.Uniform1(uNearLoc, nearClip);

				int uFarLoc = GL.GetUniformLocation(SSAOComputeShader.Handle, "uFar");
				if (uFarLoc != -1) GL.Uniform1(uFarLoc, farClip);

				int uTanFovHalfLoc = GL.GetUniformLocation(SSAOComputeShader.Handle, "uTanFovHalf");
				if (uTanFovHalfLoc != -1) GL.Uniform2(uTanFovHalfLoc, tanFovXHalf, tanFovYHalf);

				int groupX = (width + 15) / 16;
				int groupY = (height + 15) / 16;
				GL.DispatchCompute(groupX, groupY, 1);
				GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
				SSAOComputeShader.Deactivate();
			}
			else
			{
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, ssaoFBO);
				GL.Clear(ClearBufferMask.ColorBufferBit);

				GL.Disable(EnableCap.DepthTest);
				GL.DepthMask(false);

				SSAOFragmentShader.Activate();
				GL.ActiveTexture(TextureUnit.Texture0);
				GL.BindTexture(TextureTarget.Texture2D, sceneDepthTex);
				int uDepthTexLoc = GL.GetUniformLocation(SSAOFragmentShader.Handle, "uDepthTex");
				if (uDepthTexLoc != -1) GL.Uniform1(uDepthTexLoc, 0);

				int uScreenSizeLoc = GL.GetUniformLocation(SSAOFragmentShader.Handle, "uScreenSize");
				if (uScreenSizeLoc != -1) GL.Uniform2(uScreenSizeLoc, (float)width, (float)height);

				int uNearLoc = GL.GetUniformLocation(SSAOFragmentShader.Handle, "uNear");
				if (uNearLoc != -1) GL.Uniform1(uNearLoc, nearClip);

				int uFarLoc = GL.GetUniformLocation(SSAOFragmentShader.Handle, "uFar");
				if (uFarLoc != -1) GL.Uniform1(uFarLoc, farClip);

				int uTanFovHalfLoc = GL.GetUniformLocation(SSAOFragmentShader.Handle, "uTanFovHalf");
				if (uTanFovHalfLoc != -1) GL.Uniform2(uTanFovHalfLoc, tanFovXHalf, tanFovYHalf);

				DrawFullscreenQuad(SSAOFragmentShader);
				SSAOFragmentShader.Deactivate();
				
				GL.Enable(EnableCap.DepthTest);
				GL.DepthMask(true);
				
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
			}
		}

		public void CombineToScreen()
		{
			GL.Disable(EnableCap.DepthTest);
			GL.DepthMask(false);

			CombineShader.Activate();
			GL.ActiveTexture(TextureUnit.Texture0);
			GL.BindTexture(TextureTarget.Texture2D, sceneColorTex);
			int uSceneTexLoc = GL.GetUniformLocation(CombineShader.Handle, "uSceneTex");
			if (uSceneTexLoc != -1) GL.Uniform1(uSceneTexLoc, 0);

			int uUseSSAOTexLoc = GL.GetUniformLocation(CombineShader.Handle, "uUseSSAOTex");
			if (uUseSSAOTexLoc != -1) GL.Uniform1(uUseSSAOTexLoc, IsComputeSupported ? 0 : 1);

			if (!IsComputeSupported)
			{
				GL.ActiveTexture(TextureUnit.Texture1);
				GL.BindTexture(TextureTarget.Texture2D, ssaoTex);
				int uSSAOTexLoc = GL.GetUniformLocation(CombineShader.Handle, "uSSAOTex");
				if (uSSAOTexLoc != -1) GL.Uniform1(uSSAOTexLoc, 1);
			}

			DrawFullscreenQuad(CombineShader);
			CombineShader.Deactivate();

			GL.Enable(EnableCap.DepthTest);
			GL.DepthMask(true);
		}

		private void DrawFullscreenQuad(Shader shader)
		{
			int uPointLoc = GL.GetUniformLocation(shader.Handle, "uPoint");
			if (uPointLoc != -1) GL.Uniform2(uPointLoc, -1.0f, -1.0f);

			int uSizeLoc = GL.GetUniformLocation(shader.Handle, "uSize");
			if (uSizeLoc != -1) GL.Uniform2(uSizeLoc, 2.0f, 2.0f);

			int uCoordinatesLoc = GL.GetUniformLocation(shader.Handle, "uCoordinates");
			if (uCoordinatesLoc != -1) GL.Uniform2(uCoordinatesLoc, 1.0f, 1.0f);

			int uProjLoc = GL.GetUniformLocation(shader.Handle, "uCurrentProjectionMatrix");
			if (uProjLoc != -1)
			{
				OpenTK.Matrix4 identity = OpenTK.Matrix4.Identity;
				GL.UniformMatrix4(uProjLoc, false, ref identity);
			}

			int uMvLoc = GL.GetUniformLocation(shader.Handle, "uCurrentModelViewMatrix");
			if (uMvLoc != -1)
			{
				OpenTK.Matrix4 identity = OpenTK.Matrix4.Identity;
				GL.UniformMatrix4(uMvLoc, false, ref identity);
			}

			Renderer.dummyVao.Bind();
			GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
			Renderer.dummyVao.UnBind();
		}

		public void Dispose()
		{
			if (sceneFBO != 0) GL.DeleteFramebuffer(sceneFBO);
			if (sceneColorTex != 0) GL.DeleteTexture(sceneColorTex);
			if (sceneDepthTex != 0) GL.DeleteTexture(sceneDepthTex);
			if (ssaoFBO != 0) GL.DeleteFramebuffer(ssaoFBO);
			if (ssaoTex != 0) GL.DeleteTexture(ssaoTex);

			SSAOComputeShader?.Dispose();
			SSAOFragmentShader?.Dispose();
			CombineShader?.Dispose();
		}
	}
}
