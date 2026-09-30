using System.Collections.Generic;

namespace LibRender2.PostProcessing
{
	/// Post steps in order. Tonemap goes last.
	public class PostChain : System.IDisposable
	{
		private readonly List<IPostEffect> effects = new List<IPostEffect>();

		public int Count => effects.Count;

		public void Add(IPostEffect effect)
		{
			if (effect != null && !effects.Contains(effect))
			{
				effects.Add(effect);
			}
		}

		public void Insert(int index, IPostEffect effect)
		{
			if (effect == null || effects.Contains(effect))
			{
				return;
			}
			if (index < 0)
			{
				index = 0;
			}
			if (index > effects.Count)
			{
				index = effects.Count;
			}
			effects.Insert(index, effect);
		}

		public bool Remove(IPostEffect effect)
		{
			return effects.Remove(effect);
		}

		public void Clear()
		{
			effects.Clear();
		}

		// Run each step, feed its output to the next.
		public int Draw(int input)
		{
			int texture = input;
			for (int i = 0; i < effects.Count; i++)
			{
				texture = effects[i].Apply(texture);
			}
			return texture;
		}

		public void Dispose()
		{
			for (int i = 0; i < effects.Count; i++)
			{
				try
				{
					(effects[i] as System.IDisposable)?.Dispose();
				}
				catch
				{
					// Best effort; shutdown may have no context.
				}
			}
			effects.Clear();
		}
	}
}
