using System.Collections.Generic;
using LibRender2.PostProcessing.Effects;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// Single place that knows the full post chain.
	/// Adding an effect = edit here, not BaseRenderer.
	/// Order: AO(10) + 3 generics. Copy is registered DISABLED on purpose:
	/// it never runs in the chain (SyncFromOptions no-op, Enabled stays
	/// false) and exists only as the emergency blit for
	/// PostProcessManager.BlitSceneToScreen, so a chain with zero active
	/// effects still presents the captured scene instead of a cleared frame.
	/// </summary>
	public static class EffectRegistry
	{
		public static IEnumerable<IPostEffect> CreateAll(BaseRenderer renderer)
		{
			yield return new AmbientOcclusionEffect(renderer);
			yield return new FxaaEffect(renderer);
			yield return new SharpenEffect(renderer);
			yield return new VignetteEffect(renderer);
			yield return new CopyEffect(renderer);
		}
	}
}
