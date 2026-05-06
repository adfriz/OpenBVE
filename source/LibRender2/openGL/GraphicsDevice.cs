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
		private bool depthClampEnabled;
		private bool depthMaskEnabled;

		private BlendingFactor currentSrcBlend = (BlendingFactor)0;
		private BlendingFactor currentDestBlend = (BlendingFactor)0;

		private int lastBoundVAO = -1;
		private int lastBoundTexture = -1;
		private bool wireframeEnabled;
		private float currentLineWidth = 1.0f;
		private TextureUnit activeTexture = TextureUnit.Texture0;
		private int currentFramebuffer = 0;
		private CullFaceMode cullFaceMode = CullFaceMode.Back;

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
			depthClampEnabled = GL.IsEnabled(EnableCap.DepthClamp);
			GL.GetBoolean(GetPName.DepthWritemask, out depthMaskEnabled);
			GL.GetFloat(GetPName.LineWidth, out currentLineWidth);
			activeTexture = TextureUnit.Texture0; // GL default
			GL.GetInteger(GetPName.FramebufferBinding, out currentFramebuffer);
			GL.GetInteger(GetPName.CullFaceMode, out int cfm);
			cullFaceMode = (CullFaceMode)cfm;
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

		public void SetCullFaceMode(CullFaceMode mode)
		{
			if (cullFaceMode != mode)
			{
				GL.CullFace(mode);
				cullFaceMode = mode;
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

		public void SetDepthClamp(bool enabled)
		{
			if (depthClampEnabled != enabled)
			{
				if (enabled) GL.Enable(EnableCap.DepthClamp);
				else GL.Disable(EnableCap.DepthClamp);
				depthClampEnabled = enabled;
			}
		}

		public void SetDepthMask(bool enabled)
		{
			if (depthMaskEnabled != enabled)
			{
				GL.DepthMask(enabled);
				depthMaskEnabled = enabled;
			}
		}

		public void SetWireframe(bool enabled)
		{
			if (wireframeEnabled != enabled)
			{
				GL.PolygonMode(MaterialFace.FrontAndBack, enabled ? PolygonMode.Line : PolygonMode.Fill);
				wireframeEnabled = enabled;
			}
		}

		public void SetLineWidth(float width)
		{
			if (Math.Abs(currentLineWidth - width) > float.Epsilon)
			{
				GL.LineWidth(width);
				currentLineWidth = width;
			}
		}

		public void BindVAO(int handle)
		{
			if (lastBoundVAO != handle)
			{
				GL.BindVertexArray(handle);
				lastBoundVAO = handle;
			}
		}

		public void BindTexture(int handle)
		{
			if (lastBoundTexture != handle)
			{
				GL.BindTexture(TextureTarget.Texture2D, handle);
				lastBoundTexture = handle;
			}
		}

		public void BindFramebuffer(FramebufferTarget target, int handle)
		{
			if (currentFramebuffer != handle)
			{
				GL.BindFramebuffer(target, handle);
				currentFramebuffer = handle;
			}
		}

		public void SetActiveTexture(TextureUnit unit)
		{
			if (activeTexture != unit)
			{
				GL.ActiveTexture(unit);
				activeTexture = unit;
			}
		}

		/// <summary>Sets the clear color</summary>
		public void SetClearColor(float r, float g, float b, float a)
		{
			GL.ClearColor(r, g, b, a);
		}

		/// <summary>Clears the specified buffers</summary>
		public void Clear(ClearBufferMask mask)
		{
			GL.Clear(mask);
		}

		/// <summary>Sets the viewport</summary>
		public void SetViewport(int x, int y, int width, int height)
		{
			GL.Viewport(x, y, width, height);
		}

		/// <summary>Sets the polygon mode (Wireframe / Solid)</summary>
		public void SetPolygonMode(MaterialFace face, PolygonMode mode)
		{
			if (currentPolygonMode != mode)
			{
				GL.PolygonMode(face, mode);
				currentPolygonMode = mode;
			}
		}

		/// <summary>Binds a buffer to an indexed target</summary>
		public void BindBufferBase(BufferRangeTarget target, int index, int buffer)
		{
			GL.BindBufferBase(target, index, buffer);
		}

		/// <summary>Resets the cached state (Call after external GL context changes if any)</summary>
		public void ResetCache()
		{
			lastBoundVAO = -1;
			lastBoundTexture = -1;
			currentDepthFunc = (DepthFunction) 0;
			// Re-query actual GL state for booleans
			depthTestEnabled = GL.IsEnabled(EnableCap.DepthTest);
			blendEnabled = GL.IsEnabled(EnableCap.Blend);
			cullFaceEnabled = GL.IsEnabled(EnableCap.CullFace);
			scissorTestEnabled = GL.IsEnabled(EnableCap.ScissorTest);
			depthClampEnabled = GL.IsEnabled(EnableCap.DepthClamp);
			GL.GetBoolean(GetPName.DepthWritemask, out depthMaskEnabled);
			GL.GetFloat(GetPName.LineWidth, out currentLineWidth);
			activeTexture = TextureUnit.Texture0;
			GL.GetInteger(GetPName.FramebufferBinding, out currentFramebuffer);
			GL.GetInteger(GetPName.CullFaceMode, out int cfm2);
			cullFaceMode = (CullFaceMode) cfm2;
			GL.GetInteger(GetPName.PolygonMode, out int pm);
			currentPolygonMode = (PolygonMode) pm;
		}
	}
}
