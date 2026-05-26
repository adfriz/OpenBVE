using System;
using OpenTK.Graphics.OpenGL;

namespace LibRenderNext.RDI
{
	public static class RDIStateCache
	{
		private static int activeShader = 0;
		private static int activeVao = 0;
		private static int activeVbo = 0;
		private static int activeEbo = 0;
		
		private static TextureUnit activeTextureUnit = TextureUnit.Texture0;
		private static readonly int[] boundTextures = new int[32]; // Max 32 texture units

		private static bool blendEnabled = false;
		private static BlendingFactor blendSrcFactor = BlendingFactor.SrcAlpha;
		private static BlendingFactor blendDestFactor = BlendingFactor.OneMinusSrcAlpha;

		private static bool depthTestEnabled = true;
		private static DepthFunction depthTestFunc = DepthFunction.Lequal;
		private static bool depthWriteMask = true;

		private static bool cullFaceEnabled = true;
		private static CullFaceMode cullFaceMode = CullFaceMode.Front;

		public static void BindShader(int handle)
		{
			if (activeShader != handle)
			{
				GL.UseProgram(handle);
				activeShader = handle;
			}
		}

		public static void BindVertexArray(int handle)
		{
			if (activeVao != handle)
			{
				GL.BindVertexArray(handle);
				activeVao = handle;
			}
		}

		public static void BindVertexBuffer(int handle)
		{
			if (activeVbo != handle)
			{
				GL.BindBuffer(BufferTarget.ArrayBuffer, handle);
				activeVbo = handle;
			}
		}

		public static void BindIndexBuffer(int handle)
		{
			if (activeEbo != handle)
			{
				GL.BindBuffer(BufferTarget.ElementArrayBuffer, handle);
				activeEbo = handle;
			}
		}

		public static void SetActiveTexture(TextureUnit unit)
		{
			if (activeTextureUnit != unit)
			{
				RDI.RDIStateCache.SetActiveTexture(unit);
				activeTextureUnit = unit;
			}
		}

		public static void BindTexture(TextureUnit unit, int textureId)
		{
			SetActiveTexture(unit);
			int unitIndex = (int)(unit - TextureUnit.Texture0);
			if (unitIndex >= 0 && unitIndex < boundTextures.Length)
			{
				if (boundTextures[unitIndex] != textureId)
				{
					RDI.RDIStateCache.BindTexture( textureId);
					boundTextures[unitIndex] = textureId;
				}
			}
			else
			{
				RDI.RDIStateCache.BindTexture( textureId);
			}
		}

		public static void BindTexture(int textureId)
		{
			BindTexture(activeTextureUnit, textureId);
		}

		public static void SetBlendState(bool enabled, BlendingFactor src = BlendingFactor.SrcAlpha, BlendingFactor dest = BlendingFactor.OneMinusSrcAlpha)
		{
			if (blendEnabled != enabled)
			{
				if (enabled)
					GL.Enable(EnableCap.Blend);
				else
					GL.Disable(EnableCap.Blend);
				blendEnabled = enabled;
			}

			if (enabled && (blendSrcFactor != src || blendDestFactor != dest))
			{
				GL.BlendFunc(src, dest);
				blendSrcFactor = src;
				blendDestFactor = dest;
			}
		}

		public static void SetDepthState(bool enabled, DepthFunction func = DepthFunction.Lequal, bool writeMask = true)
		{
			if (depthTestEnabled != enabled)
			{
				if (enabled)
					GL.Enable(EnableCap.DepthTest);
				else
					GL.Disable(EnableCap.DepthTest);
				depthTestEnabled = enabled;
			}

			if (enabled && depthTestFunc != func)
			{
				GL.DepthFunc(func);
				depthTestFunc = func;
			}

			if (depthWriteMask != writeMask)
			{
				GL.DepthMask(writeMask);
				depthWriteMask = writeMask;
			}
		}

		public static void SetCullState(bool enabled, CullFaceMode mode = CullFaceMode.Front)
		{
			if (cullFaceEnabled != enabled)
			{
				if (enabled)
					GL.Enable(EnableCap.CullFace);
				else
					GL.Disable(EnableCap.CullFace);
				cullFaceEnabled = enabled;
			}

			if (enabled && cullFaceMode != mode)
			{
				GL.CullFace(mode);
				cullFaceMode = mode;
			}
		}

		public static void Reset()
		{
			activeShader = 0;
			activeVao = 0;
			activeVbo = 0;
			activeEbo = 0;
			activeTextureUnit = TextureUnit.Texture0;
			Array.Clear(boundTextures, 0, boundTextures.Length);
			blendEnabled = false;
			depthTestEnabled = true;
			depthTestFunc = DepthFunction.Lequal;
			depthWriteMask = true;
			cullFaceEnabled = true;
			cullFaceMode = CullFaceMode.Front;
		}
	}
}
