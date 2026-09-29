using System;
using OpenBveApi.Colors;
using OpenBveApi.Math;
using OpenBveApi.Textures;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Primitives
{
	public class Picturebox : GLControl
	{
		/// <summary>How the image fits the box.</summary>
		public ImageSizeMode SizeMode;

		private bool flipX;
		private bool flipY;

		public Picturebox(BaseRenderer renderer) : base(renderer)
		{
			SizeMode = ImageSizeMode.Zoom;
		}

		public override void Draw()
		{
			if (!Renderer.currentHost.LoadTexture(ref Texture, OpenGlTextureWrapMode.ClampClamp))
			{
				return;
			}

			GL.DepthMask(true);
			Vector2 newSize;
			switch (SizeMode)
			{
				case ImageSizeMode.Normal:
				case ImageSizeMode.Center:
					// Backing box first, then the (possibly cropped) image on top.
					Renderer.Rectangle.Draw(Texture, Location, Size, BackgroundColor);
					newSize = Texture.Size;
					if (newSize.X > Size.X)
					{
						newSize.X = Size.X;
					}

					if (newSize.Y > Size.Y)
					{
						newSize.Y = Size.Y;
					}
					Vector2 position = Location;
					if (SizeMode == ImageSizeMode.Center)
					{
						position += (newSize - Size) / 2;
					}
					Renderer.Rectangle.DrawAlpha(Texture, position, newSize, Color128.White, new Vector2(newSize / Size));
					break;
				case ImageSizeMode.Stretch:
					Renderer.Rectangle.Draw(Texture, Location, Size, BackgroundColor);
					break;
				case ImageSizeMode.Zoom:
					// Backing box first, then the image scaled to fit.
					Renderer.Rectangle.Draw(null, Location, Size, BackgroundColor);
					Vector2 ratio = Size / Texture.Size;
					double newRatio = Math.Min(ratio.X, ratio.Y);
					newSize = new Vector2(Texture.Width, Texture.Height) * newRatio;
					OpenGlTextureWrapMode wrapMode = flipX
						? (flipY ? OpenGlTextureWrapMode.RepeatRepeat : OpenGlTextureWrapMode.RepeatClamp)
						: (flipY ? OpenGlTextureWrapMode.ClampRepeat : OpenGlTextureWrapMode.ClampClamp);
					Renderer.Rectangle.DrawAlpha(Texture, new Vector2(Location.X + (Size.X - newSize.X) / 2, Location.Y + (Size.Y - newSize.Y) / 2), newSize, Color128.White, new Vector2(flipX ? -1 : 1, flipY ? -1 : 1), wrapMode);
					break;
			}
		}

		/// <summary>Flips the image.</summary>
		public void Flip(bool flipX, bool flipY)
		{
			this.flipX = flipX;
			this.flipY = flipY;
		}
	}
}
