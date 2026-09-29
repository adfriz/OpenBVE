//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2023, Christopher Lees, The OpenBVE Project
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

using LibRender2.Text;
using OpenBveApi.Colors;
using OpenBveApi.Graphics;

namespace LibRender2.Primitives
{
	/// <summary>Shared bits for text controls (buttons, labels).</summary>
	public abstract class TextControl : GLControl
	{
		/// <summary>Font used for the text.</summary>
		public OpenGlFont Font;

		protected TextControl(BaseRenderer renderer) : base(renderer)
		{
			Font = Renderer.Fonts.LargeFont;
			// default colors to match GLMenu
			BackgroundColor = Color128.Black;
		}

		// Sizes the control to fit its text.
		protected void AutoSize(string text)
		{
			Size = Font.MeasureString(text) * 1.5;
		}

		// True when the mouse is inside the control.
		protected bool HitTest(int x, int y)
		{
			return x > Location.X && x < Location.X + Size.X && y > Location.Y && y < Location.Y + Size.Y;
		}

		// Draws the background rectangle.
		protected void DrawFrame()
		{
			Renderer.Rectangle.Draw(Texture, Location, Size, BackgroundColor);
		}

		// Draws the text over the background.
		protected void DrawText(string text, Color128 color)
		{
			Renderer.OpenGlString.Draw(Font, text, Location + (Size * 0.15), TextAlignment.TopLeft, color);
		}
	}
}
