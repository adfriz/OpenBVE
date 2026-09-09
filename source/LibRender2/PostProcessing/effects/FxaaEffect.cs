using OpenBveApi;

namespace LibRender2.PostProcessing.Effects
{
	/// <summary>
	/// FXAA lightweight anti-aliasing fragment effect.
	/// Default OFF; OFF keeps the manager in bypass.
	/// </summary>
	public class FxaaEffect : SinglePassEffect
	{
		public FxaaEffect(BaseRenderer renderer)
			: base(renderer, "FXAA", "FXAA", 30, "post_fxaa", 1.0f, o => o.PostFxaa)
		{
		}
	}
}
