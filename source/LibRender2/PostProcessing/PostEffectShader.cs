using LibRender2.Shaders;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// Base shader for fragment-based post effects.
	/// Loads the shared post.vert vertex shader plus a per-effect fragment
	/// shader from EmbeddedResource, following the AbstractShader
	/// isFromStream pattern (LibRender2.post.vert / LibRender2.{frag}.frag).
	/// Exposes the shared uniforms uInputTex / uResolution / uTime / uIntensity.
	/// Missing (optimized-out) uniforms report location -1 and are skipped.
	/// </summary>
	public class PostEffectShader : AbstractShader
	{
		private readonly int uInputTexLocation;
		private readonly int uResolutionLocation;
		private readonly int uTimeLocation;
		private readonly int uIntensityLocation;

		/// <param name="renderer">A reference to the base renderer.</param>
		/// <param name="fragmentName">Fragment resource name without extension, e.g. "post_copy".</param>
		public PostEffectShader(BaseRenderer renderer, string fragmentName)
			: base(renderer, "post", fragmentName, true, true)
		{
			uInputTexLocation = GL.GetUniformLocation(Handle, "uInputTex");
			uResolutionLocation = GL.GetUniformLocation(Handle, "uResolution");
			uTimeLocation = GL.GetUniformLocation(Handle, "uTime");
			uIntensityLocation = GL.GetUniformLocation(Handle, "uIntensity");
			if (uInputTexLocation >= 0)
			{
				GL.ProgramUniform1(Handle, uInputTexLocation, 0);
			}
		}

		/// <summary>Selects the texture unit carrying the input image (default 0).</summary>
		public void SetInputTextureUnit(int unit)
		{
			if (uInputTexLocation >= 0)
			{
				GL.ProgramUniform1(Handle, uInputTexLocation, unit);
			}
		}

		/// <summary>Uploads the viewport resolution in pixels.</summary>
		public void SetResolution(int width, int height)
		{
			if (uResolutionLocation >= 0)
			{
				GL.ProgramUniform2(Handle, uResolutionLocation, (float)width, (float)height);
			}
		}

		/// <summary>Uploads the effect time in seconds (for animated effects).</summary>
		public void SetTime(float time)
		{
			if (uTimeLocation >= 0)
			{
				GL.ProgramUniform1(Handle, uTimeLocation, time);
			}
		}

		/// <summary>Uploads the effect intensity multiplier.</summary>
		public void SetIntensity(float intensity)
		{
			if (uIntensityLocation >= 0)
			{
				GL.ProgramUniform1(Handle, uIntensityLocation, intensity);
			}
		}
	}
}
