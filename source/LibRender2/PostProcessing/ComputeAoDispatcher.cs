using System;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// Thin AO compute dispatch plumbing.
	/// Single source of truth for AO binding units + <c>ceil(size/8)</c>
	/// group math for the 8x8 local size used by every AO kernel.
	/// Every GL call goes through <c>OpenTK.Graphics.OpenGL.GL.*</c> directly.
	///
	/// Image units: 0 = scene color (Rgba8), 2 = AO raw (R16f/R8), 3 = normal (RGBA16F/8).
	/// Sampler units: 1 = scene depth, 4 = linear depth, 5 = normal, 6 = AO.
	/// 7 = packed depth edges (image binding in the depth pass, sampler in denoise).
	/// Depth stays on a sampler (DepthComponent24 cannot bind as R/RGBA image).
	/// </summary>
	public static class ComputeAoDispatcher
	{
		/// <summary>Local work-group size shared by all AO compute kernels.</summary>
		public const int LocalSize = 8;

		/// <summary>Image unit carrying the scene color (read-only).</summary>
		public const int ColorUnit = 0;

		/// <summary>Texture unit carrying the scene depth sampler.</summary>
		public const int DepthUnit = 1;

		/// <summary>Image unit carrying the AO target (write-only).</summary>
		public const int AoUnit = 2;

		/// <summary>Image unit carrying the normal target (write-only).</summary>
		public const int NormalImageUnit = 3;

		/// <summary>Sampler unit for the linear-depth texture.</summary>
		public const int LinearSamplerUnit = 4;

		/// <summary>Sampler unit for the normal texture.</summary>
		public const int NormalSamplerUnit = 5;

		/// <summary>Sampler unit for AO input (denoise/composite).</summary>
		public const int AoSamplerUnit = 6;

		/// <summary>Image unit carrying the packed depth-edge target (write-only).</summary>
		public const int EdgesImageUnit = 7;

		/// <summary>Sampler unit for the packed depth-edge texture.</summary>
		public const int EdgesSamplerUnit = 7;

		/// <summary>Computes work-group counts for the 8x8 local size.</summary>
		public static void GroupsFor(int width, int height, out int groupsX, out int groupsY)
		{
			groupsX = Math.Max(1, (width + LocalSize - 1) / LocalSize);
			groupsY = Math.Max(1, (height + LocalSize - 1) / LocalSize);
		}

		/// <summary>Binds a sampler uniform. No-op for handle 0 or missing uniform.</summary>
		public static void BindSampler(ComputeShader shader, string uniformName, int textureHandle, int textureUnit)
		{
			if (shader == null || textureHandle == 0)
			{
				return;
			}
			int location = shader.GetUniformLocation(uniformName);
			if (location < 0)
			{
				return;
			}
			GL.ActiveTexture(TextureUnit.Texture0 + textureUnit);
			GL.BindTexture(TextureTarget.Texture2D, textureHandle);
			shader.SetInt(location, textureUnit);
			GL.ActiveTexture(TextureUnit.Texture0);
		}

		/// <summary>Unbinds a sampler unit. Never throws.</summary>
		public static void UnbindSampler(int textureUnit)
		{
			try
			{
				GL.ActiveTexture(TextureUnit.Texture0 + textureUnit);
				GL.BindTexture(TextureTarget.Texture2D, 0);
				GL.ActiveTexture(TextureUnit.Texture0);
			}
			catch
			{
				// ignored
			}
		}

		/// <summary>Unbinds an image unit. Never throws (handle 0 makes the format meaningless).</summary>
		public static void UnbindImage(int imageUnit)
		{
			try
			{
				GL.BindImageTexture(imageUnit, 0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba8);
			}
			catch
			{
				// ignored
			}
		}
	}
}
