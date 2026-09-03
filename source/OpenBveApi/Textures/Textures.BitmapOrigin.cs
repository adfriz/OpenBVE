using System.Drawing;
using System.Drawing.Imaging;
using OpenBveApi.Colors;

namespace OpenBveApi.Textures
{
	/// <summary>Represents a System.Drawing.Bitmap where the texture can be loaded from.</summary>
	public class BitmapOrigin : TextureOrigin
	{
		/// <summary>The bitmap.</summary>
		public readonly Bitmap Bitmap;
		/// <summary>The texture parameters to be applied when loading the texture to OpenGL</summary>
		public readonly TextureParameters Parameters;

		// --- constructors ---
		/// <summary>Creates a new bitmap origin.</summary>
		/// <param name="bitmap">The bitmap.</param>
		public BitmapOrigin(Bitmap bitmap)
		{
			this.Bitmap = bitmap;
		}

		/// <summary>Creates a new bitmap origin.</summary>
		/// <param name="bitmap">The bitmap.</param>
		/// <param name="parameters">The texture parameters</param>
		public BitmapOrigin(Bitmap bitmap, TextureParameters parameters)
		{
			this.Bitmap = bitmap;
			this.Parameters = parameters;
		}

		// --- functions ---
		/// <summary>Gets the texture from this origin.</summary>
		/// <param name="texture">Receives the texture.</param>
		/// <returns>Whether the texture could be obtained successfully.</returns>
		public override bool GetTexture(out Texture texture)
		{
			Bitmap bitmap = this.Bitmap;
			Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
			/* 
			 * If the bitmap format is not already 32-bit BGRA,
			 * then convert it to 32-bit BGRA.
			 * */
			Color24[] p = null;
			if (bitmap.PixelFormat != System.Drawing.Imaging.PixelFormat.Format32bppArgb && bitmap.PixelFormat != System.Drawing.Imaging.PixelFormat.Format24bppRgb)
			{
				/* Only store the color palette data for
				 * textures using a restricted palette
				 * With a large number of textures loaded at
				 * once, this can save a decent chunk of memory
				 * */
				p = new Color24[bitmap.Palette.Entries.Length];
				for (int i = 0; i < bitmap.Palette.Entries.Length; i++)
				{
					p[i] = bitmap.Palette.Entries[i];
				}
			}

			// Caller owns this.Bitmap; only converted is disposed.
			Bitmap source = bitmap;
			Bitmap converted = null;
			if (bitmap.PixelFormat != System.Drawing.Imaging.PixelFormat.Format32bppArgb)
			{
				converted = new Bitmap(bitmap.Width, bitmap.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
				using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(converted))
					graphics.DrawImage(bitmap, rect, rect, GraphicsUnit.Pixel);
				source = converted;
			}

			BitmapData data = source.LockBits(rect, ImageLockMode.ReadOnly, source.PixelFormat);
			try
			{
				if (data.Stride != 4 * data.Width)
				{
					texture = null;
					return false;
				}
				byte[] raw = new byte[data.Stride * data.Height];
				System.Runtime.InteropServices.Marshal.Copy(data.Scan0, raw, 0, raw.Length);
				for (int i = 0; i < raw.Length; i += 4)
					(raw[i], raw[i + 2]) = (raw[i + 2], raw[i]); // BGRA -> RGBA
				texture = new Texture(source.Width, source.Height, PixelFormat.RGBAlpha, raw, p).ApplyParameters(this.Parameters);
				return true;
			}
			finally
			{
				source.UnlockBits(data);
				if (converted != null)
					converted.Dispose();
			}
		}
	}
}
