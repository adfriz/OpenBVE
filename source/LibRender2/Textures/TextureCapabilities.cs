using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Textures
{
	/// <summary>Runtime probe of GPU texture capabilities (observe only, no behavior change).</summary>
	/// <remarks>Must be called once with a current GL context (e.g. after game window init).
	/// Uses GetStringi (GL 3.0+) with fallback to the legacy extension string, as the latter
	/// fails with InvalidEnum on forward-compatible contexts.</remarks>
	public static class TextureCapabilities
	{
		public static bool Probed;
		/// <summary>S3TC available (BC1/BC2/BC3 direct upload possible).</summary>
		public static bool CanS3TC;
		/// <summary>BPTC available (BC7 direct upload possible; often false on a 3.3 context).</summary>
		public static bool CanBPTC;
		/// <summary>RGTC available (BC4/BC5, useful for grayscale).</summary>
		public static bool CanRGTC;
		public static int MaxTextureSize;
		public static string Summary = "not probed";

		public static void Probe()
		{
			Probed = false;
			CanS3TC = false;
			CanBPTC = false;
			CanRGTC = false;
			MaxTextureSize = 0;
			HashSet<string> extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				int count = GL.GetInteger(GetPName.NumExtensions);
				if (GL.GetError() == ErrorCode.NoError && count > 0 && count < 10000)
				{
					for (int i = 0; i < count; i++)
					{
						try
						{
							string ext = GL.GetString(StringNameIndexed.Extensions, i);
							if (!string.IsNullOrEmpty(ext))
							{
								extensions.Add(ext.Trim());
							}
						}
						catch
						{
							break;
						}
					}
				}
			}
			catch
			{
				// fall through to legacy path below
			}
			if (extensions.Count == 0)
			{
				try
				{
					string legacy = GL.GetString(StringName.Extensions);
					GL.GetError(); // swallow InvalidEnum on forward-compatible contexts
					if (!string.IsNullOrEmpty(legacy))
					{
						foreach (string ext in legacy.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
						{
							extensions.Add(ext.Trim());
						}
					}
				}
				catch
				{
					// no extension info available; report all-false
				}
			}
			CanS3TC = extensions.Contains("GL_EXT_texture_compression_s3tc");
			CanBPTC = extensions.Contains("GL_ARB_texture_compression_bptc") || extensions.Contains("GL_EXT_texture_compression_bptc");
			CanRGTC = extensions.Contains("GL_EXT_texture_compression_rgtc") || extensions.Contains("GL_ARB_texture_compression_rgtc");
			try
			{
				MaxTextureSize = GL.GetInteger(GetPName.MaxTextureSize);
				GL.GetError();
			}
			catch
			{
				MaxTextureSize = 0;
			}
			Probed = true;
			Summary = "S3TC=" + CanS3TC + " BPTC=" + CanBPTC + " RGTC=" + CanRGTC + " MaxSize=" + MaxTextureSize + " (" + extensions.Count + " ext)";
		}
	}
}
