using OpenBveApi;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing.Effects
{
	/// <summary>
	/// Pixel-identical passthrough effect (foundation / smoke test).
	/// Copies the input texture to the target framebuffer unchanged.
	/// Runs last in the chain (large <see cref="Order"/>).
	/// Presents via driver blit (no shader/VAO/viewport involvement), which is
	/// pixel-identical to the post_copy draw for unmodified input; falls back
	/// to the inherited shader draw if the blit ever fails.
	/// </summary>
	public class CopyEffect : SinglePassEffect
	{
		private int blitFbo;
		private bool blitReady;
		private bool disposedCopy;

		public CopyEffect(BaseRenderer renderer)
			: base(renderer, "Copy", "Copy (passthrough)", 1000, "post_copy", 1.0f, null)
		{
		}

		/// <inheritdoc/>
		public override void SyncFromOptions(BaseOptions options)
		{
			// Copy has no parameters; master switch and order are owned by PostProcessManager.
		}

		/// <inheritdoc/>
		protected override bool TryBlitPresent(int inputTex, int targetFboOrZero)
		{
			try
			{
				if (inputTex == 0 || width <= 0 || height <= 0 || disposedCopy)
				{
					return false;
				}
				if (!blitReady)
				{
					GL.GenFramebuffers(1, out blitFbo);
					blitReady = blitFbo != 0;
					if (!blitReady)
					{
						return false;
					}
				}
				GL.MemoryBarrier(MemoryBarrierFlags.AllBarrierBits);
				GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, blitFbo);
				GL.FramebufferTexture2D(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, inputTex, 0);
				GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, targetFboOrZero);
				bool scissor = false;
				try
				{
					scissor = GL.IsEnabled(EnableCap.ScissorTest);
				}
				catch
				{
					// ignored
				}
				if (scissor)
				{
					try
					{
						GL.Disable(EnableCap.ScissorTest);
					}
					catch
					{
						// ignored
					}
				}
				GL.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
				if (scissor)
				{
					try
					{
						GL.Enable(EnableCap.ScissorTest);
					}
					catch
					{
						// ignored
					}
				}
				GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <inheritdoc/>
		public override void Dispose()
		{
			disposedCopy = true;
			try
			{
				if (blitReady && blitFbo != 0)
				{
					GL.DeleteFramebuffer(blitFbo);
				}
			}
			catch
			{
				// ignored: dispose must not throw
			}
			blitFbo = 0;
			blitReady = false;
			base.Dispose();
		}
	}
}
