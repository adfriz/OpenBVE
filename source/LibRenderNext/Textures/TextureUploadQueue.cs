using System;
using System.Collections.Concurrent;
using OpenBveApi.Textures;
using OpenBveApi.Graphics;
using OpenTK.Graphics.OpenGL;
using PixelFormat = OpenBveApi.Textures.PixelFormat;

namespace LibRenderNext.Textures
{
	public enum AsyncLoadState
	{
		NotStarted,
		Decoding,
		ReadyToUpload,
		Uploaded,
		Failed
	}

	public class TextureLoadTask
	{
		public AsyncLoadState State = AsyncLoadState.NotStarted;
		public Texture DecodedTexture;
		public Exception Error;
	}

	public struct UploadJob
	{
		public Texture Handle;
		public OpenGlTextureWrapMode Wrap;
		public Texture DecodedTexture;
	}

	public static class TextureUploadQueue
	{
		public static readonly ConcurrentDictionary<Texture, TextureLoadTask> AsyncTasks = new ConcurrentDictionary<Texture, TextureLoadTask>();
		public static readonly ConcurrentQueue<UploadJob> Queue = new ConcurrentQueue<UploadJob>();

		public static void ProcessUploadQueue(InterpolationMode Interpolation, int AnisotropicFilteringLevel, BaseRenderer renderer, OpenBveApi.Hosts.HostInterface currentHost)
		{
			int uploaded = 0;
			while (uploaded < 5 && Queue.TryDequeue(out UploadJob job))
			{
				if (job.Handle == null || job.DecodedTexture == null)
				{
					continue;
				}

				if (job.Handle.OpenGlTextures[(int)job.Wrap].Valid)
				{
					continue;
				}

				try
				{
					UploadTextureInternal(job.Handle, job.DecodedTexture, job.Wrap, Interpolation, AnisotropicFilteringLevel, renderer, currentHost);
					if (AsyncTasks.TryGetValue(job.Handle, out var task))
					{
						task.State = AsyncLoadState.Uploaded;
					}
				}
				catch
				{
					if (AsyncTasks.TryGetValue(job.Handle, out var task))
					{
						task.State = AsyncLoadState.Failed;
					}
				}
				uploaded++;
			}
		}

		private static void UploadTextureInternal(Texture handle, Texture texture, OpenGlTextureWrapMode wrap, InterpolationMode Interpolation, int AnisotropicFilteringLevel, BaseRenderer renderer, OpenBveApi.Hosts.HostInterface currentHost)
		{
			int[] names = new int[1];
			GL.GenTextures(1, names);
			RHI.RHIStateCache.BindTexture(names[0]);
			handle.OpenGlTextures[(int)wrap].Name = names[0];
			if (texture.MultipleFrames)
			{
				texture.OpenGlTextures[(int)wrap].Name = names[0];
			}

			handle.Size = texture.Size;
			handle.Transparency = texture.GetTransparencyType();

			switch (Interpolation)
			{
				case InterpolationMode.NearestNeighbor:
					GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (float)TextureMinFilter.Nearest);
					GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (float)TextureMagFilter.Nearest);
					break;
				case InterpolationMode.Bilinear:
					GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (float)TextureMinFilter.Linear);
					GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (float)TextureMagFilter.Linear);
					break;
				case InterpolationMode.NearestNeighborMipmapped:
					GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (float)TextureMinFilter.NearestMipmapNearest);
					GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (float)TextureMagFilter.Nearest);
					break;
				case InterpolationMode.BilinearMipmapped:
					GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (float)TextureMinFilter.NearestMipmapLinear);
					GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (float)TextureMagFilter.Linear);
					break;
				case InterpolationMode.TrilinearMipmapped:
					GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (float)TextureMinFilter.LinearMipmapLinear);
					GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (float)TextureMagFilter.Linear);
					break;
				default:
					GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (float)TextureMinFilter.LinearMipmapLinear);
					GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (float)TextureMagFilter.Linear);
					break;
			}

			if ((wrap & OpenGlTextureWrapMode.RepeatClamp) != 0)
			{
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (float)TextureWrapMode.Repeat);
			}
			else
			{
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (float)TextureWrapMode.ClampToEdge);
			}

			if ((wrap & OpenGlTextureWrapMode.ClampRepeat) != 0)
			{
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (float)TextureWrapMode.Repeat);
			}
			else
			{
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (float)TextureWrapMode.ClampToEdge);
			}

			if (renderer.ForceLegacyOpenGL)
			{
				if (Interpolation == InterpolationMode.NearestNeighbor || Interpolation == InterpolationMode.Bilinear)
				{
					GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.GenerateMipmap, 0);
				}
				else
				{
					GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.GenerateMipmap, 1);
				}
			}

			if (Interpolation == InterpolationMode.AnisotropicFiltering && AnisotropicFilteringLevel > 0)
			{
				GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, AnisotropicFilteringLevel);
			}

			bool noLuminanceChannel = currentHost.Platform == OpenBveApi.Hosts.HostPlatform.AppleOSX || renderer.currentOptions.ForceForwardsCompatibleContext;

			if (handle.Transparency == TextureTransparencyType.Opaque)
			{
				switch (texture.PixelFormat)
				{
					case PixelFormat.Grayscale:
						GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
						GL.TexImage2D(TextureTarget.Texture2D, 0,
							noLuminanceChannel ? PixelInternalFormat.R8 : PixelInternalFormat.Luminance,
							texture.Width, texture.Height, 0,
							noLuminanceChannel ? OpenTK.Graphics.OpenGL.PixelFormat.Red : OpenTK.Graphics.OpenGL.PixelFormat.Luminance,
							PixelType.UnsignedByte, texture.Bytes);

						if (noLuminanceChannel)
						{
							GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleRgba, new[] { 6403, 6403, 6403, 1 });
						}
						break;
					case PixelFormat.RGB:
						GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
						GL.TexImage2D(TextureTarget.Texture2D, 0,
							PixelInternalFormat.Rgb8,
							texture.Width, texture.Height, 0,
							OpenTK.Graphics.OpenGL.PixelFormat.Rgb,
							PixelType.UnsignedByte, texture.Bytes);
						break;
					case PixelFormat.RGBAlpha:
						int stride = (3 * (texture.Width + 1) >> 2) << 2;
						byte[] newBytes = new byte[stride * texture.Height];
						int i = 0, j = 0;

						for (int y = 0; y < texture.Height; y++)
						{
							for (int x = 0; x < texture.Width; x++)
							{
								newBytes[j + 0] = texture.Bytes[i + 0];
								newBytes[j + 1] = texture.Bytes[i + 1];
								newBytes[j + 2] = texture.Bytes[i + 2];
								i += 4;
								j += 3;
							}
							j += stride - 3 * texture.Width;
						}
						GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
						GL.TexImage2D(TextureTarget.Texture2D, 0,
							PixelInternalFormat.Rgb8,
							texture.Width, texture.Height, 0,
							OpenTK.Graphics.OpenGL.PixelFormat.Rgb,
							PixelType.UnsignedByte, newBytes);
						break;
				}
			}
			else
			{
				switch (texture.PixelFormat)
				{
					case PixelFormat.GrayscaleAlpha:
						if (noLuminanceChannel)
						{
							int stride = (2 * (texture.Width + 1) >> 2) << 2;
							byte[] newBytes = new byte[stride * texture.Height];
							int i = 0, j = 0;

							for (int y = 0; y < texture.Height; y++)
							{
								for (int x = 0; x < texture.Width; x++)
								{
									newBytes[j + 0] = texture.Bytes[i + 0];
									newBytes[j + 1] = texture.Bytes[i + 0];
									newBytes[j + 2] = texture.Bytes[i + 0];
									newBytes[j + 3] = texture.Bytes[i + 1];
									i += 4;
									j += 4;
								}
								j += stride - 3 * texture.Width;
								GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
								GL.TexImage2D(TextureTarget.Texture2D, 0,
									PixelInternalFormat.Rgba8,
									texture.Width, texture.Height, 0,
									OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
									PixelType.UnsignedByte, newBytes);
							}
						}
						else
						{
							GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
							GL.TexImage2D(TextureTarget.Texture2D, 0,
								PixelInternalFormat.LuminanceAlpha,
								texture.Width, texture.Height, 0,
								OpenTK.Graphics.OpenGL.PixelFormat.LuminanceAlpha,
								PixelType.UnsignedByte, texture.Bytes);
						}
						break;
					case PixelFormat.RGBAlpha:
						GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
						GL.TexImage2D(TextureTarget.Texture2D, 0,
							PixelInternalFormat.Rgba8,
							texture.Width, texture.Height, 0,
							OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
							PixelType.UnsignedByte, texture.Bytes);
						break;
				}
			}

			if (renderer.ForceLegacyOpenGL == false)
			{
				GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
			}
			handle.OpenGlTextures[(int)wrap].Valid = true;
			if (texture.MultipleFrames)
			{
				texture.OpenGlTextures[(int)wrap].Valid = true;
			}
		}
	}
}
