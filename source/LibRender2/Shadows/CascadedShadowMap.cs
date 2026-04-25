using System;
using Raylib_cs;

namespace LibRender2.Shadows
{
	public class CascadedShadowMap : IDisposable
	{
		public int CascadeCount { get; private set; }
		public int Resolution { get; private set; }
		public uint[] FBOs { get; private set; }
		public uint[] DepthTextures { get; private set; }

		public CascadedShadowMap(int cascadeCount = 3, int resolution = 2048)
		{
			CascadeCount = cascadeCount;
			Resolution = resolution;
			FBOs = new uint[cascadeCount];
			DepthTextures = new uint[cascadeCount];

			for (int i = 0; i < cascadeCount; i++)
			{
				CreateCascade(i);
			}
		}

		public void Resize(int newCascadeCount, int newResolution)
		{
			Dispose();
			CascadeCount = newCascadeCount;
			Resolution = newResolution;
			FBOs = new uint[newCascadeCount];
			DepthTextures = new uint[newCascadeCount];

			for (int i = 0; i < newCascadeCount; i++)
			{
				CreateCascade(i);
			}
		}

		private void CreateCascade(int index)
		{
			FBOs[index] = rlgl.rlLoadFramebuffer(Resolution, Resolution);
			rlgl.rlEnableFramebuffer(FBOs[index]);

			// Create depth texture
			DepthTextures[index] = rlgl.rlLoadTextureDepth(Resolution, Resolution, false);
			rlgl.rlFramebufferAttach(FBOs[index], DepthTextures[index], rlFramebufferAttachType.RL_ATTACHMENT_DEPTH, rlFramebufferAttachTextureType.RL_ATTACHMENT_TEXTURE2D, 0);

			if (!rlgl.rlFramebufferComplete(FBOs[index]))
			{
				Console.Error.WriteLine($"[CSM] Cascade {index} FBO incomplete");
			}

			rlgl.rlDisableFramebuffer();
		}

		public void BindCascadeForWriting(int cascadeIndex)
		{
			rlgl.rlEnableFramebuffer(FBOs[cascadeIndex]);
			rlgl.rlViewport(0, 0, Resolution, Resolution);
		}

		public void Unbind()
		{
			rlgl.rlDisableFramebuffer();
			rlgl.rlViewport(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
		}

		public void BindAllCascadesForReading(int baseUnit)
		{
			for (int i = 0; i < CascadeCount; i++)
			{
				rlgl.rlActiveTextureSlot(baseUnit + i);
				rlgl.rlEnableTexture(DepthTextures[i]);
			}
			rlgl.rlActiveTextureSlot(0);
		}

		public void Dispose()
		{
			for (int i = 0; i < CascadeCount; i++)
			{
				if (FBOs[i] != 0) rlgl.rlUnloadFramebuffer(FBOs[i]);
				if (DepthTextures[i] != 0) rlgl.rlUnloadTexture(DepthTextures[i]);
			}
		}
	}
}
