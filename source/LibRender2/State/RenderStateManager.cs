using System;
using LibRender2.Abstractions;
using LibRender2.Shaders;
using OpenBveApi.Colors;
using OpenBveApi.Textures;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.State
{
	/// <summary>
	/// Owns cached OpenGL state (blend / alpha-test / cull / last bindings).
	/// Extracted from BaseRenderer so state transitions live in one place
	/// instead of being scattered across 10+ folders.
	/// </summary>
	public class RenderStateManager : IRenderState
	{
		private bool blendEnabled;
		private BlendingFactor blendSrcFactor;
		private BlendingFactor blendDestFactor;

		private bool alphaTestEnabled;
		private AlphaFunction alphaFuncComparison;
		private float alphaFuncValue;

		/// <summary>Applies deferred alpha state to the active shader.</summary>
		private readonly Func<AbstractShader> currentShaderProvider;

		public RenderStateManager(Func<AbstractShader> currentShaderProvider)
		{
			this.currentShaderProvider = currentShaderProvider;
		}

		/// <summary>Parameterless ctor for standalone use (alpha restore becomes a no-op without a shader).</summary>
		public RenderStateManager() : this(null)
		{
		}

		public bool BlendEnabled => blendEnabled;
		public bool AlphaTestEnabled => alphaTestEnabled;

		public bool CullFaceEnabled { get; set; } = true;

		public int LastVAO { get; set; }

		public OpenGlTexture LastBoundTexture { get; set; }

		public Color32 LastColor { get; set; } = Color32.White;

		public void SetBlendFunc(BlendingFactor srcFactor, BlendingFactor destFactor)
		{
			blendEnabled = true;
			blendSrcFactor = srcFactor;
			blendDestFactor = destFactor;
			GL.Enable(EnableCap.Blend);
			GL.BlendFunc(srcFactor, destFactor);
		}

		public void SetBlendFunc()
		{
			SetBlendFunc(blendSrcFactor, blendDestFactor);
		}

		public void UnsetBlendFunc()
		{
			blendEnabled = false;
			GL.Disable(EnableCap.Blend);
		}

		public void RestoreBlendFunc()
		{
			if (blendEnabled)
			{
				GL.Enable(EnableCap.Blend);
				GL.BlendFunc(blendSrcFactor, blendDestFactor);
			}
			else
			{
				GL.Disable(EnableCap.Blend);
			}
		}

		public void SetAlphaFunc(AlphaFunction comparison, float value)
		{
			alphaTestEnabled = true;
			alphaFuncComparison = comparison;
			alphaFuncValue = value;
			AbstractShader shader = currentShaderProvider?.Invoke();
			if (shader != null)
			{
				shader.SetAlphaTest(true);
				shader.SetAlphaFunction(comparison, value);
			}
		}

		public void SetAlphaFunc()
		{
			SetAlphaFunc(alphaFuncComparison, alphaFuncValue);
		}

		public void UnsetAlphaFunc()
		{
			alphaTestEnabled = false;
			currentShaderProvider?.Invoke()?.SetAlphaTest(false);
		}

		public void RestoreAlphaFunc()
		{
			AbstractShader shader = currentShaderProvider?.Invoke();
			if (shader == null)
			{
				return;
			}
			if (alphaTestEnabled)
			{
				shader.SetAlphaTest(true);
				shader.SetAlphaFunction(alphaFuncComparison, alphaFuncValue);
			}
			else
			{
				shader.SetAlphaTest(false);
			}
		}

		public void ResetOpenGlState()
		{
			GL.Enable(EnableCap.CullFace);
			CullFaceEnabled = true;
			GL.CullFace(CullFaceMode.Front);
			SetBlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
			UnsetBlendFunc();
			GL.Enable(EnableCap.DepthTest);
			GL.DepthFunc(DepthFunction.Lequal);
			GL.Disable(EnableCap.DepthClamp);
			GL.DepthMask(true);
			SetAlphaFunc(AlphaFunction.Greater, 0.9f);
		}

		/// <summary>Binds a VAO only when the handle changed; returns true when a bind occurred.</summary>
		public bool BindVAO(int handle, Action bind)
		{
			if (LastVAO == handle)
			{
				return false;
			}
			bind();
			LastVAO = handle;
			return true;
		}

		/// <summary>Invalidates cached bindings (use after external GL state corruption, e.g. shadow pass).</summary>
		public void InvalidateBindings()
		{
			LastVAO = -1;
			LastBoundTexture = null;
		}
	}
}
