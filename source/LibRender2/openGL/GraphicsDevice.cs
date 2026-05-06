using OpenTK.Graphics.OpenGL;

namespace LibRender2.GraphicsCore
{
	/// <summary>
	/// Represents the low-level graphics device (Equivalent to Raylib's rlgl).
	/// Handles OpenGL state management and caching to reduce driver overhead.
	/// </summary>
	public class GraphicsDevice
	{
		// Cache for common OpenGL states
		private bool depthTestEnabled;
		private bool blendEnabled;
		private bool cullFaceEnabled;
		private bool scissorTestEnabled;

		private BlendingFactor currentSrcBlend = (BlendingFactor)0;
		private BlendingFactor currentDestBlend = (BlendingFactor)0;

		/// <summary>
		/// Initializes the graphics device with default OpenGL states.
		/// </summary>
		public void Initialize()
		{
			// Set initial states to match OpenGL defaults or our preferred defaults
			depthTestEnabled = GL.IsEnabled(EnableCap.DepthTest);
			blendEnabled = GL.IsEnabled(EnableCap.Blend);
			cullFaceEnabled = GL.IsEnabled(EnableCap.CullFace);
			scissorTestEnabled = GL.IsEnabled(EnableCap.ScissorTest);
		}

		public void SetDepthTest(bool enabled)
		{
			if (depthTestEnabled != enabled)
			{
				if (enabled) GL.Enable(EnableCap.DepthTest);
				else GL.Disable(EnableCap.DepthTest);
				depthTestEnabled = enabled;
			}
		}

		public void SetBlend(bool enabled)
		{
			if (blendEnabled != enabled)
			{
				if (enabled) GL.Enable(EnableCap.Blend);
				else GL.Disable(EnableCap.Blend);
				blendEnabled = enabled;
			}
		}

		public void SetCullFace(bool enabled)
		{
			if (cullFaceEnabled != enabled)
			{
				if (enabled) GL.Enable(EnableCap.CullFace);
				else GL.Disable(EnableCap.CullFace);
				cullFaceEnabled = enabled;
			}
		}

		public void SetScissorTest(bool enabled)
		{
			if (scissorTestEnabled != enabled)
			{
				if (enabled) GL.Enable(EnableCap.ScissorTest);
				else GL.Disable(EnableCap.ScissorTest);
				scissorTestEnabled = enabled;
			}
		}

		public void SetBlendFunc(BlendingFactor sfactor, BlendingFactor dfactor)
		{
			if (currentSrcBlend != sfactor || currentDestBlend != dfactor)
			{
				GL.BlendFunc(sfactor, dfactor);
				currentSrcBlend = sfactor;
				currentDestBlend = dfactor;
			}
		}

		public void SetViewport(int x, int y, int width, int height)
		{
			GL.Viewport(x, y, width, height);
		}

		public void SetClearColor(float r, float g, float b, float a)
		{
			GL.ClearColor(r, g, b, a);
		}

		public void Clear(ClearBufferMask mask)
		{
			GL.Clear(mask);
		}
		private DepthFunction currentDepthFunc = (DepthFunction)0;

		public void SetDepthFunc(DepthFunction func)
		{
			if (currentDepthFunc != func)
			{
				GL.DepthFunc(func);
				currentDepthFunc = func;
			}
		}
	}
}
