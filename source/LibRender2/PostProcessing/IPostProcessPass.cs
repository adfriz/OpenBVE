//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
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

using OpenTK.Graphics.OpenGL;

namespace LibRender2.PostProcessing
{
	/// <summary>
	/// A single stage in the post-processing pipeline.
	/// </summary>
	/// <remarks>
	/// A pass receives the scene (or the previous pass's output) as an OpenGL texture
	/// handle and is expected to return the texture handle of its result. The pipeline
	/// owns the shared offscreen framebuffer and fullscreen-triangle geometry, so a pass
	/// only deals with its own shaders and intermediate targets. This keeps each effect
	/// (bloom, DOF, colour grade, vignette, ...) self-contained and independently testable.
	/// </remarks>
	public interface IPostProcessPass
	{
		/// <summary>Whether this pass should run this frame.</summary>
		bool Enabled { get; }

		/// <summary>
		/// Called once before the 3D scene is rendered. The pipeline already binds the
		/// shared scene-capture framebuffer; passes that need per-frame setup can do it
		/// here. The return value is ignored by the pipeline (the capture target is owned
		/// centrally), but is kept for symmetry.
		/// </summary>
		/// <returns>0; the pipeline manages the capture framebuffer.</returns>
		int BeginScene();

		/// <summary>
		/// Applies the effect.
		/// </summary>
		/// <param name="sceneTexture">The original captured scene texture (useful for composite passes that mix the scene with the effect).</param>
		/// <param name="inputTexture">The texture handle produced by the previous stage (or the scene if this is the first stage).</param>
		/// <param name="sceneWidth">Width of the scene in pixels.</param>
		/// <param name="sceneHeight">Height of the scene in pixels.</param>
		/// <returns>The texture handle of this pass's output.</returns>
		int Render(int sceneTexture, int inputTexture, int sceneWidth, int sceneHeight);

		/// <summary>Reallocates internal targets after a viewport resize.</summary>
		void Resize(int width, int height);

		/// <summary>Releases all GL resources held by the pass.</summary>
		void Dispose();
	}
}
