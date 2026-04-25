using System;
using Raylib_cs;
using static Raylib_cs.Rlgl;


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
			FBOs[index] = Rlgl.LoadFramebuffer(Resolution, Resolution);
			Rlgl.EnableFramebuffer(FBOs[index]);

			// Create depth texture
			DepthTextures[index] = Rlgl.LoadTextureDepth(Resolution, Resolution, false);
			Rlgl.FramebufferAttach(FBOs[index], DepthTextures[index], (FramebufferAttachType)100, (FramebufferAttachTextureType)0, 0);






			if (!Rlgl.FramebufferComplete(FBOs[index]))
			{
				Console.Error.WriteLine($"[CSM] Cascade {index} FBO incomplete");
			}

			Rlgl.DisableFramebuffer();
		}

		public void BindCascadeForWriting(int cascadeIndex)
		{
			Rlgl.EnableFramebuffer(FBOs[cascadeIndex]);
			Rlgl.Viewport(0, 0, Resolution, Resolution);
		}

		public void Unbind()
		{
			Rlgl.DisableFramebuffer();
			Rlgl.Viewport(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
		}

		public void BindAllCascadesForReading(int baseUnit)
		{
			for (int i = 0; i < CascadeCount; i++)
			{
				Rlgl.ActiveTextureSlot(baseUnit + i);
				Rlgl.EnableTexture(DepthTextures[i]);
			}
			Rlgl.ActiveTextureSlot(0);
		}

		public void Dispose()
		{
			for (int i = 0; i < CascadeCount; i++)
			{
				if (FBOs[i] != 0) Rlgl.UnloadFramebuffer(FBOs[i]);
				if (DepthTextures[i] != 0) Rlgl.UnloadTexture(DepthTextures[i]);
			}
		}

	}
}
