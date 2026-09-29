using System;
using System.Drawing;
using OpenBveApi.Math;
using OpenBveApi.Textures;

namespace LibRender2.Text
{
	/// <summary>One rendered character.</summary>
	public struct OpenGlFontChar
	{
		/// <summary>Where it lives in the atlas texture.</summary>
		public Vector4 TextureCoordinates;
		/// <summary>Size on screen.</summary>
		public Vector2 PhysicalSize;
		/// <summary>Size for measuring text.</summary>
		public Vector2 TypographicSize;

		/// <summary>Creates a character.</summary>
		/// <param name="textureCoordinates">The texture coordinates that represent the character in the underlying texture.</param>
		/// <param name="physicalSize">The physical size of the character.</param>
		/// <param name="typographicSize">The typographic size of the character.</param>
		public OpenGlFontChar(Vector4 textureCoordinates, Vector2 physicalSize, Vector2 typographicSize)
		{
			TextureCoordinates = textureCoordinates;
			PhysicalSize = physicalSize;
			TypographicSize = typographicSize;
		}
	}

	/// <summary>A usable font.</summary>
	public sealed class OpenGlFont : IDisposable
	{
		/// <summary>The GDI+ font.</summary>
		public readonly Font Font;
		/// <summary>Font size in pixels.</summary>
		public readonly float FontSize;
		/// <summary>4352 tables x 256 chars covering U+0000 to U+10FFFF.</summary>
		private readonly OpenGlFontTable[] Tables;

		private readonly StringFormat Default;

		private readonly StringFormat Typographic;

		private bool disposed;

		// --- constructors ---
		/// <summary>Creates a new font.</summary>
		/// <param name="family">The font family.</param>
		/// <param name="size">The size in pixels.</param>
		public OpenGlFont(FontFamily family, float size)
		{
			Font = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel);
			FontSize = size;
			Tables = new OpenGlFontTable[4352];
			Default = StringFormat.GenericDefault;
			Typographic = StringFormat.GenericTypographic;
		}

		// --- functions ---
		/// <summary>Reads one codepoint (1-2 chars for surrogate pairs).</summary>
		/// <param name="text">Source string.</param>
		/// <param name="offset">Where to read.</param>
		/// <param name="texture">Atlas holding the codepoint.</param>
		/// <param name="data">Character data.</param>
		/// <returns>The number of characters read.</returns>
		public int GetCharacterData(string text, int offset, out Texture texture, out OpenGlFontChar data)
		{
			int value = char.ConvertToUtf32(text, offset);
			int hi = value >> 8;
			int lo = value & 0xFF;

			if (Tables[hi] == null || Tables[hi].Texture == null)
			{
				lock (BaseRenderer.GdiPlusLock)
				{
					Tables[hi] = new OpenGlFontTable(Font, hi << 8, Default, Typographic);
				}
			}

			texture = Tables[hi].Texture;
			data = Tables[hi].Characters[lo];
			return value >= 0x10000 ? 2 : 1;
		}

		/// <summary>Measures text as rendered.</summary>
		public Vector2 MeasureString(string text)
		{
			double width = 0;
			double height = 0;

			if (text != null)
			{
				for (int i = 0; i < text.Length; i++)
				{
					i += GetCharacterData(text, i, out Texture _, out OpenGlFontChar data) - 1;
					width += data.TypographicSize.X;

					if (data.TypographicSize.Y > height)
					{
						height = data.TypographicSize.Y;
					}
				}
			}

			return new Vector2(width, height);
		}

		private void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (disposing)
				{
					Font?.Dispose();
				}

				disposed = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		~OpenGlFont()
		{
			Dispose(true);
		}
	}
}
