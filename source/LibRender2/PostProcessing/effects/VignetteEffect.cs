using OpenBveApi;

namespace LibRender2.PostProcessing.Effects
{
	/// <summary>
	/// Vignette fragment effect.
	/// Intensity defaults to 0.5 (subtle). Default OFF; OFF keeps bypass.
	/// </summary>
	public class VignetteEffect : SinglePassEffect
	{
		public VignetteEffect(BaseRenderer renderer)
			: base(renderer, "Vignette", "Vignette", 60, "post_vignette", 0.5f, o => o.PostVignette)
		{
		}
	}
}
