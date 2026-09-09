using OpenBveApi.Hosts;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>Detected OpenGL capabilities used to gate post-processing / compute paths.</summary>
	public struct RenderCapabilities
	{
		public int GlMajor;
		public int GlMinor;
		public bool SupportsCompute;
		public string GlVersionString;
		public string GlRendererString;
		public string GlslVersionString;

		public static bool IsMacOS(HostPlatform p) => p == HostPlatform.AppleOSX;

		/// <summary>Detects capabilities from the current GL context. Must be called with a current context.</summary>
		public static RenderCapabilities Detect(HostPlatform platform)
		{
			RenderCapabilities caps = new RenderCapabilities { GlMajor = 3, GlMinor = 3, GlVersionString = string.Empty, GlRendererString = string.Empty, GlslVersionString = string.Empty };
			try
			{
				caps.GlVersionString = GL.GetString(StringName.Version) ?? string.Empty;
				caps.GlRendererString = GL.GetString(StringName.Renderer) ?? string.Empty;
				caps.GlslVersionString = GL.GetString(StringName.ShadingLanguageVersion) ?? string.Empty;
				int major = GL.GetInteger(GetPName.MajorVersion);
				int minor = GL.GetInteger(GetPName.MinorVersion);
				if (major <= 0)
				{
					// Fallback: parse "4.6.0 NVIDIA ..." / "OpenGL ES 3.0 ..." via Split + TryParse.
					string[] tokens = caps.GlVersionString.Split(new char[] { ' ', '.' }, System.StringSplitOptions.RemoveEmptyEntries);
					major = 0;
					minor = 0;
					for (int i = 0; i + 1 < tokens.Length; i++)
					{
						if (int.TryParse(tokens[i], out major) && int.TryParse(tokens[i + 1], out minor))
						{
							break;
						}
						major = 0;
						minor = 0;
					}
				}
				if (major <= 0)
				{
					major = 3;
					minor = 3;
				}
				caps.GlMajor = major;
				caps.GlMinor = minor;
				int maxImages = 0;
				if (major > 4 || (major == 4 && minor >= 3))
				{
					maxImages = GL.GetInteger(GetPName.MaxComputeImageUniforms);
				}
				caps.SupportsCompute = platform != HostPlatform.AppleOSX && maxImages >= 4;
			}
			catch
			{
				caps.GlMajor = 3;
				caps.GlMinor = 3;
				caps.SupportsCompute = false;
			}
			return caps;
		}
	}
}
