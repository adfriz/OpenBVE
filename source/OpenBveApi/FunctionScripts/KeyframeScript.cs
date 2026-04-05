//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, Christopher Lees, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
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


using OpenBveApi.Math;
using OpenBveApi.Trains;

namespace OpenBveApi.FunctionScripting
{
	/// <summary>An animation script using keyframes</summary>
	public class KeyframeScript : AnimationScript
	{
		/// <inheritdoc />
		public double LastResult { get; set; }
		/// <inheritdoc />
		public double Maximum { get; set; } = double.NaN;
		/// <inheritdoc />
		public double Minimum { get; set; } = double.NaN;

		/// <summary>The input script whose result is used as the key for the keyframes</summary>
		public readonly AnimationScript InputScript;

		/// <summary>The keyframes for this script</summary>
		public readonly Objects.DoubleFrame[] Keyframes;

		/// <summary>Creates a new keyframe script</summary>
		/// <param name="inputScript">The input script</param>
		/// <param name="keyframes">The keyframes</param>
		public KeyframeScript(AnimationScript inputScript, Objects.DoubleFrame[] keyframes)
		{
			InputScript = inputScript;
			Keyframes = keyframes;
		}

		/// <inheritdoc />
		public double ExecuteScript(AbstractTrain Train, int CarIndex, Vector3 Position, double TrackPosition, int SectionIndex, bool IsPartOfTrain, double TimeElapsed, int CurrentState)
		{
			double animationKey = InputScript.ExecuteScript(Train, CarIndex, Position, TrackPosition, SectionIndex, IsPartOfTrain, TimeElapsed, CurrentState);
			
			int currentFrame = Objects.FrameExtensions.FindCurrentFrame(Keyframes, animationKey, out int interpolateFrame, out double frac);
			LastResult = Keyframes[currentFrame].Value + (Keyframes[interpolateFrame].Value - Keyframes[currentFrame].Value) * frac;

			if (!double.IsNaN(this.Minimum) && LastResult < Minimum)
			{
				return Minimum;
			}
			if (!double.IsNaN(this.Maximum) && LastResult > Maximum)
			{
				return Maximum;
			}
			return LastResult;
		}

		/// <inheritdoc />
		public AnimationScript Clone()
		{
			return new KeyframeScript(InputScript.Clone(), (Objects.DoubleFrame[])Keyframes.Clone());
		}
	}
}
