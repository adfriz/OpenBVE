using OpenBveApi;

namespace LibRender2.PostProcessing.Effects
{
	/// <summary>
	/// Sharpen (unsharp-mask 3x3) fragment effect.
	/// Default OFF; OFF keeps the manager in bypass.
	/// </summary>
	public class SharpenEffect : SinglePassEffect
	{
		public SharpenEffect(BaseRenderer renderer)
			: base(renderer, "Sharpen", "Sharpen", 50, "post_sharpen", 1.0f, o => o.PostSharpen)
		{
		}
	}
}
