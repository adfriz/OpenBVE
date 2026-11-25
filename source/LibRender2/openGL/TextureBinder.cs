using OpenTK.Graphics.OpenGL;

namespace LibRender2
{
	/// <summary>
	/// Helper for sampler setup. On GL 4.5+ textures are bound with <see cref="GL.BindTextureUnit"/>,
	/// which targets a unit directly and leaves the global active-texture unit alone. On GL 4.1 the
	/// legacy ActiveTexture + BindTexture pair is used, so behaviour is identical on old contexts.
	/// Configured once from the detected capabilities in BaseRenderer.Initialize().
	/// </summary>
	public static class TextureBinder
	{
		private static bool directStateAccess;

		/// <summary>True when the current context uses the GL 4.5 direct state access path.</summary>
		public static bool DirectStateAccess
		{
			get { return directStateAccess; }
		}

		public static void Configure(GlCapabilities capabilities)
		{
			directStateAccess = capabilities != null && capabilities.IsAtLeast(4, 5);
		}

		/// <summary>Binds a 2D texture to a numbered texture unit.</summary>
		public static void Bind(int unit, int texture)
		{
			if (directStateAccess)
			{
				GL.BindTextureUnit(unit, texture);
			}
			else
			{
				GL.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + unit));
				GL.BindTexture(TextureTarget.Texture2D, texture);
			}
		}

		/// <summary>Binds consecutive textures starting at <paramref name="baseUnit"/>.</summary>
		public static void BindRange(TextureUnit baseUnit, int[] textures, int count)
		{
			int first = (int)baseUnit - (int)TextureUnit.Texture0;
			for (int i = 0; i < count; i++)
			{
				Bind(first + i, textures[i]);
			}
			ResetActiveUnit();
		}

		/// <summary>
		/// Restores the legacy active-texture unit to 0 after a batch of binds. The DSA path never
		/// changes it, so this is a no-op there.
		/// </summary>
		public static void ResetActiveUnit()
		{
			if (!directStateAccess)
			{
				GL.ActiveTexture(TextureUnit.Texture0);
			}
		}

		/// <summary>Sets a texture's compare mode without binding it on GL 4.5+.</summary>
		public static void SetCompareMode(int texture, bool enabled)
		{
			int mode = (int)(enabled ? TextureCompareMode.CompareRefToTexture : TextureCompareMode.None);
			if (directStateAccess)
			{
				int[] parameter = { mode };
				GL.TextureParameterI(texture, TextureParameterName.TextureCompareMode, parameter);
			}
			else
			{
				GL.BindTexture(TextureTarget.Texture2D, texture);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, mode);
			}
		}

		/// <summary>Uploads a byte image into an existing 2D texture without binding it on GL 4.5+.</summary>
		public static void SubImage2D(int texture, int width, int height, PixelFormat format, byte[] pixels)
		{
			if (directStateAccess)
			{
				GL.TextureSubImage2D(texture, 0, 0, 0, width, height, format, PixelType.UnsignedByte, pixels);
			}
			else
			{
				GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, width, height, format, PixelType.UnsignedByte, pixels);
			}
		}

		/// <summary>Regenerates a texture's mipmaps without binding it on GL 4.5+.</summary>
		public static void GenerateMipmap(int texture)
		{
			if (directStateAccess)
			{
				GL.GenerateTextureMipmap(texture);
			}
			else
			{
				GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
			}
		}
	}
}
