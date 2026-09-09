using System;
using OpenBveApi;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// A single stackable post-processing effect.
	/// Each effect reads <see cref="Render"/> input textures and composites
	/// to the target framebuffer. Effects run sorted by <see cref="Order"/>.
	/// </summary>
	public interface IPostEffect : IDisposable
	{
		/// <summary>Stable machine id, e.g. "Copy", "AmbientOcclusion".</summary>
		string Id { get; }
		/// <summary>Human readable name for the options UI.</summary>
		string Name { get; }
		/// <summary>Whether this effect participates in the chain.</summary>
		bool Enabled { get; set; }
		/// <summary>Chain position, ascending. Copy/passthrough uses a large value so it runs last.</summary>
		int Order { get; }
		/// <summary>Effect strength multiplier, 0-2 range by convention.</summary>
		float Intensity { get; set; }
		/// <summary>Allocates GL resources for the given viewport size. Requires a current GL context.</summary>
		void Initialize(int width, int height);
		/// <summary>Reallocates for a new viewport size. Requires a current GL context.</summary>
		void Resize(int width, int height);
		/// <summary>
		/// Runs the effect. Reads <paramref name="inputTex"/> (and optionally <paramref name="depthTex"/>,
		/// 0 when unavailable) and writes to <paramref name="targetFboOrZero"/> (0 = default framebuffer).
		/// </summary>
		void Render(int inputTex, int depthTex, int targetFboOrZero);
		/// <summary>Pulls per-effect flags from options.</summary>
		void SyncFromOptions(BaseOptions options);
	}
}
