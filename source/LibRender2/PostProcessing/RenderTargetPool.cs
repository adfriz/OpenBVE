//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the above disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the above disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
//ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
//ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// Centralized pool of reusable <see cref="RenderTarget"/> instances.
	/// </summary>
	/// <remarks>
	/// Passes acquire a target instead of allocating their own texture+FBO, and release it
	/// back to the pool when no longer needed. Targets are matched by width/height so a
	/// pass that needs the same-size target on the next frame reuses the GL objects. The
	/// pool owns the lifetime of every target it hands out; callers must never dispose a
	/// borrowed target directly.
	/// </remarks>
	internal sealed class RenderTargetPool
	{
		private readonly List<RenderTarget> available = new List<RenderTarget>();
		private readonly List<RenderTarget> inUse = new List<RenderTarget>();

		/// <summary>
		/// Returns a target of the requested size, reusing a free one if possible.
		/// </summary>
		internal RenderTarget Acquire(int width, int height, PixelInternalFormat internalFormat, PixelFormat format)
		{
			for (int i = 0; i < available.Count; i++)
			{
				RenderTarget rt = available[i];
				if (rt.Width == width && rt.Height == height && rt.InternalFormat == internalFormat)
				{
					available.RemoveAt(i);
					inUse.Add(rt);
					return rt;
				}
			}

			RenderTarget created = new RenderTarget(width, height, internalFormat, format);
			inUse.Add(created);
			return created;
		}

		/// <summary>
		/// Returns <paramref name="rt"/> to the pool for later reuse (it is not disposed).
		/// </summary>
		internal void Release(RenderTarget rt)
		{
			if (rt == null)
			{
				return;
			}

			if (inUse.Remove(rt))
			{
				available.Add(rt);
			}
		}

		/// <summary>
		/// Moves every in-use target back to the available list (recycle for the next frame).
		/// </summary>
		internal void Clear()
		{
			available.AddRange(inUse);
			inUse.Clear();
		}

		/// <summary>Disposes all pooled targets and clears both lists.</summary>
		internal void Dispose()
		{
			foreach (RenderTarget rt in available)
			{
				rt.Dispose();
			}
			foreach (RenderTarget rt in inUse)
			{
				rt.Dispose();
			}
			available.Clear();
			inUse.Clear();
		}
	}
}
