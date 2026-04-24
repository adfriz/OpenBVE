using OpenTK.Graphics.OpenGL;

namespace LibRender2.Pipeline
{
	/// <summary>
	/// Manages and caches the OpenGL state to prevent redundant API calls.
	/// </summary>
	public class GraphicsDevice
	{
		private bool blendEnabled;
		public bool BlendEnabled => blendEnabled;
		private BlendingFactor blendSrcFactor;
		private BlendingFactor blendDestFactor;

		private bool depthTestEnabled = true;
		private DepthFunction depthFunction = DepthFunction.Lequal; // less than or equal to
		private bool depthMask = true;

		private bool cullFaceEnabled = true;
		private CullFaceMode cullFaceMode = CullFaceMode.Front;

		private bool lightingEnabled;
		private bool fogEnabled;
		private bool texture2DEnabled;

		private bool alphaTestEnabled;
		public bool AlphaTestEnabled => alphaTestEnabled;
		private AlphaFunction alphaTestFunction = AlphaFunction.Greater;
		public AlphaFunction AlphaTestFunction => alphaTestFunction;
		private float alphaTestThreshold;
		public float AlphaTestThreshold => alphaTestThreshold;

		/// <summary>
		/// Sets the blending state.
		/// </summary>
		public void SetBlend(bool enabled, BlendingFactor src = BlendingFactor.SrcAlpha, BlendingFactor dest = BlendingFactor.OneMinusSrcAlpha)
		{
			if (blendEnabled != enabled)
			{
				blendEnabled = enabled;
				if (enabled) GL.Enable(EnableCap.Blend);
				else GL.Disable(EnableCap.Blend);
			}

			if (enabled && (blendSrcFactor != src || blendDestFactor != dest))
			{
				blendSrcFactor = src;
				blendDestFactor = dest;
				GL.BlendFunc(src, dest);
			}
		}

		/// <summary>
		/// Sets the depth test state.
		/// </summary>
		public void SetDepthTest(bool enabled, DepthFunction function = DepthFunction.Lequal)
		{
			if (depthTestEnabled != enabled)
			{
				depthTestEnabled = enabled;
				if (enabled) GL.Enable(EnableCap.DepthTest);
				else GL.Disable(EnableCap.DepthTest);
			}

			if (enabled && depthFunction != function)
			{
				depthFunction = function;
				GL.DepthFunc(function);
			}
		}

		/// <summary>
		/// Sets whether depth writing is enabled.
		/// </summary>
		public void SetDepthMask(bool enabled)
		{
			if (depthMask != enabled)
			{
				depthMask = enabled;
				GL.DepthMask(enabled);
			}
		}

		/// <summary>
		/// Sets the face culling state.
		/// </summary>
		public void SetCullFace(bool enabled, CullFaceMode mode = CullFaceMode.Front)
		{
			if (cullFaceEnabled != enabled)
			{
				cullFaceEnabled = enabled;
				if (enabled) GL.Enable(EnableCap.CullFace);
				else GL.Disable(EnableCap.CullFace);
			}

			if (enabled && cullFaceMode != mode)
			{
				cullFaceMode = mode;
				GL.CullFace(mode);
			}
		}

		/// <summary>
		/// Sets the lighting state.
		/// </summary>
		public void SetLighting(bool enabled)
		{
			if (lightingEnabled != enabled)
			{
				lightingEnabled = enabled;
				if (enabled) GL.Enable(EnableCap.Lighting);
				else GL.Disable(EnableCap.Lighting);
			}
		}

		/// <summary>
		/// Sets the fog state.
		/// </summary>
		public void SetFog(bool enabled)
		{
			if (fogEnabled != enabled)
			{
				fogEnabled = enabled;
				if (enabled) GL.Enable(EnableCap.Fog);
				else GL.Disable(EnableCap.Fog);
			}
		}

		/// <summary>
		/// Sets the texture 2D state.
		/// </summary>
		public void SetTexture2D(bool enabled)
		{
			if (texture2DEnabled != enabled)
			{
				texture2DEnabled = enabled;
				if (enabled) GL.Enable(EnableCap.Texture2D);
				else GL.Disable(EnableCap.Texture2D);
			}
		}

		/// <summary>
		/// Sets the alpha test state.
		/// </summary>
		public void SetAlphaTest(bool enabled, AlphaFunction function = AlphaFunction.Greater, float threshold = 0.0f)
		{
			if (alphaTestEnabled != enabled)
			{
				alphaTestEnabled = enabled;
				if (enabled) GL.Enable(EnableCap.AlphaTest);
				else GL.Disable(EnableCap.AlphaTest);
			}

			if (enabled && (alphaTestFunction != function || alphaTestThreshold != threshold))
			{
				alphaTestFunction = function;
				alphaTestThreshold = threshold;
				GL.AlphaFunc(function, threshold);
			}
		}

		/// <summary>
		/// Resets the cached state to match the current OpenGL defaults or a known state.
		/// </summary>
		public void Reset()
		{
			// Reset to OpenGL defaults
			GL.Disable(EnableCap.Blend);
			blendEnabled = false;

			GL.Enable(EnableCap.DepthTest);
			depthTestEnabled = true;
			GL.DepthFunc(DepthFunction.Less);
			depthFunction = DepthFunction.Less;

			GL.DepthMask(true);
			depthMask = true;

			GL.Enable(EnableCap.CullFace);
			cullFaceEnabled = true;
			GL.CullFace(CullFaceMode.Back);
			cullFaceMode = CullFaceMode.Back;

			GL.Disable(EnableCap.Lighting);
			lightingEnabled = false;

			GL.Disable(EnableCap.Fog);
			fogEnabled = false;

			GL.Disable(EnableCap.Texture2D);
			texture2DEnabled = false;

			GL.Disable(EnableCap.AlphaTest);
			alphaTestEnabled = false;
			alphaTestFunction = AlphaFunction.Greater;
			alphaTestThreshold = 0.0f;
		}
	}
}
