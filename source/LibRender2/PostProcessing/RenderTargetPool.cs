using System;
using System.Collections.Generic;

namespace LibRender2.PostProcessing
{
	/// <summary>Resolution of a pooled target relative to the scene.</summary>
	public enum PostTargetScale
	{
		Full,
		Half,
		Quarter,
		Eighth,
		Sixteenth
	}

	/// <summary>
	/// Describes a transien target request. The name is for debugging only: targets with the same
	/// format/scale/attachment options share one pool bucket, so two effects at half resolution
	/// reuse the same GPU memory.
	/// </summary>
	public sealed class PostTargetDesc
	{
		public readonly string Name;
		public readonly RenderTargetFormat Format;
		public readonly PostTargetScale Scale;
		public readonly bool WithDepth;
		public readonly bool Mipmaps;
		public readonly bool Nearest;
		public readonly int Attachments;

		public PostTargetDesc(string name, RenderTargetFormat format = RenderTargetFormat.Sdr,
			PostTargetScale scale = PostTargetScale.Full, bool withDepth = false,
			bool mipmaps = false, bool nearest = false, int attachments = 1)
		{
			Name = name ?? "target";
			Format = format;
			Scale = scale;
			WithDepth = withDepth;
			Mipmaps = mipmaps;
			Nearest = nearest;
			Attachments = Math.Max(1, Math.Min(2, attachments));
		}

		public int Width(int sceneWidth)
		{
			switch (Scale)
			{
				case PostTargetScale.Half: return Math.Max(1, (int)Math.Round(sceneWidth * 0.5f));
				case PostTargetScale.Quarter: return Math.Max(1, (int)Math.Round(sceneWidth * 0.25f));
				case PostTargetScale.Eighth: return Math.Max(1, (int)Math.Round(sceneWidth * 0.125f));
				case PostTargetScale.Sixteenth: return Math.Max(1, (int)Math.Round(sceneWidth * 0.0625f));
				default: return Math.Max(1, sceneWidth);
			}
		}

		public int Height(int sceneHeight)
		{
			switch (Scale)
			{
				case PostTargetScale.Half: return Math.Max(1, (int)Math.Round(sceneHeight * 0.5f));
				case PostTargetScale.Quarter: return Math.Max(1, (int)Math.Round(sceneHeight * 0.25f));
				case PostTargetScale.Eighth: return Math.Max(1, (int)Math.Round(sceneHeight * 0.125f));
				case PostTargetScale.Sixteenth: return Math.Max(1, (int)Math.Round(sceneHeight * 0.0625f));
				default: return Math.Max(1, sceneHeight);
			}
		}

		internal string Key(int sceneWidth, int sceneHeight)
		{
			return Width(sceneWidth) + "x" + Height(sceneHeight) + "|" + Format + "|" +
				(WithDepth ? "D" : "-") + (Mipmaps ? "M" : "-") + (Nearest ? "N" : "-") + "|A" + Attachments;
		}
	}

	/// <summary>
	/// Reuses offscreen targets across frames instead of allocating one per pass. Targets are
	/// acquired by description and returned at the end of the frame; only transient (per-frame)
	/// work belongs here. History buffers are persistent and are held outside the pool until
	/// explicitly released.
	/// </summary>
	internal sealed class RenderTargetPool : IDisposable
	{
		private readonly Dictionary<string, Stack<RenderTarget>> available = new Dictionary<string, Stack<RenderTarget>>();
		private readonly List<RenderTarget> all = new List<RenderTarget>();

		public RenderTarget Acquire(PostTargetDesc desc, int sceneWidth, int sceneHeight)
		{
			string key = desc.Key(sceneWidth, sceneHeight);
			Stack<RenderTarget> bucket;
			if (available.TryGetValue(key, out bucket) && bucket.Count > 0)
			{
				return bucket.Pop();
			}
			RenderTarget target = new RenderTarget(desc.Width(sceneWidth), desc.Height(sceneHeight),
				desc.WithDepth, desc.Format, desc.Mipmaps, desc.Nearest, desc.Attachments);
			target.PoolKey = key;
			all.Add(target);
			return target;
		}

		public void Release(RenderTarget target)
		{
			if (target == null || target.PoolKey == null)
			{
				return;
			}
			Stack<RenderTarget> bucket;
			if (!available.TryGetValue(target.PoolKey, out bucket))
			{
				bucket = new Stack<RenderTarget>();
				available.Add(target.PoolKey, bucket);
			}
			if (!bucket.Contains(target))
			{
				bucket.Push(target);
			}
		}

		/// <summary>Drops cached targets whose size no longer matches the window (after a resize).</summary>
		public void Trim(int sceneWidth, int sceneHeight)
		{
			string full = new PostTargetDesc("full", RenderTargetFormat.Sdr, PostTargetScale.Full).Width(sceneWidth) + "x" +
			              new PostTargetDesc("full", RenderTargetFormat.Sdr, PostTargetScale.Full).Height(sceneHeight);
			string half = new PostTargetDesc("half", RenderTargetFormat.Sdr, PostTargetScale.Half).Width(sceneWidth) + "x" +
			              new PostTargetDesc("half", RenderTargetFormat.Sdr, PostTargetScale.Half).Height(sceneHeight);
			string quarter = new PostTargetDesc("quarter", RenderTargetFormat.Sdr, PostTargetScale.Quarter).Width(sceneWidth) + "x" +
			                 new PostTargetDesc("quarter", RenderTargetFormat.Sdr, PostTargetScale.Quarter).Height(sceneHeight);
			string eighth = new PostTargetDesc("eighth", RenderTargetFormat.Sdr, PostTargetScale.Eighth).Width(sceneWidth) + "x" +
			                new PostTargetDesc("eighth", RenderTargetFormat.Sdr, PostTargetScale.Eighth).Height(sceneHeight);
			string sixteenth = new PostTargetDesc("sixteenth", RenderTargetFormat.Sdr, PostTargetScale.Sixteenth).Width(sceneWidth) + "x" +
			                   new PostTargetDesc("sixteenth", RenderTargetFormat.Sdr, PostTargetScale.Sixteenth).Height(sceneHeight);
			List<string> dead = new List<string>();
			foreach (KeyValuePair<string, Stack<RenderTarget>> entry in available)
			{
				// Key layout: "WxH|Format|flags". Format and flags do not affect the decision.
				string size = entry.Key.Substring(0, entry.Key.IndexOf('|'));
				if (size == full || size == half || size == quarter || size == eighth || size == sixteenth)
				{
					continue;
				}
				foreach (RenderTarget target in entry.Value)
				{
					target.Dispose();
					all.Remove(target);
				}
				dead.Add(entry.Key);
			}
			for (int i = 0; i < dead.Count; i++)
			{
				available.Remove(dead[i]);
			}
		}

		public void Dispose()
		{
			for (int i = 0; i < all.Count; i++)
			{
				all[i].Dispose();
			}
			all.Clear();
			available.Clear();
		}
	}

	/// <summary>
	/// A persistent pair of targets for temporal effects. Advance once per frame, write to
	/// <see cref="Current"/>, then call <see cref="MarkWritten"/>; on the next frame
	/// <see cref="Previous"/> holds that image and <see cref="IsValid"/> is true.
	/// Validity is lost on resize, a gap in frame numbers, an empty frame or <see cref="Reset"/>.
	/// </summary>
	public sealed class PostHistory : IDisposable
	{
		private readonly RenderTargetPool pool;
		private readonly PostTargetDesc desc;
		private int width;
		private int height;
		private RenderTarget current;
		private RenderTarget previous;
		private bool valid;
		private bool writtenLastFrame;
		private int lastFrame = -1;

		internal PostHistory(RenderTargetPool pool, PostTargetDesc desc, int width, int height)
		{
			this.pool = pool;
			this.desc = desc;
			this.width = width;
			this.height = height;
		}

		public RenderTarget Current => current;
		public RenderTarget Previous => previous;
		public bool IsValid => valid;

		private RenderTarget Acquire()
		{
			return pool.Acquire(desc, width, height);
		}

		public void Advance(int frameNumber)
		{
			bool contiguous = lastFrame < 0 || frameNumber == lastFrame + 1;
			RenderTarget oldPrevious = previous;
			previous = current;
			current = oldPrevious ?? Acquire();
			valid = contiguous && previous != null && writtenLastFrame;
			writtenLastFrame = false;
			lastFrame = frameNumber;
		}

		public void MarkWritten()
		{
			writtenLastFrame = true;
		}

		public void Reset()
		{
			valid = false;
			writtenLastFrame = false;
			lastFrame = -1;
		}

		internal void Resize(int sceneWidth, int sceneHeight)
		{
			pool.Release(current);
			pool.Release(previous);
			current = null;
			previous = null;
			width = sceneWidth;
			height = sceneHeight;
			Reset();
		}

		public void Dispose()
		{
			pool.Release(current);
			pool.Release(previous);
			current = null;
			previous = null;
			Reset();
		}
	}
}
