//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, Maurizo M. Gavioli, The OpenBVE Project
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

using OpenBveApi.Textures;

namespace LibRender2.Menu
{
	/// <summary>Base class for menu rows.</summary>
	public abstract class MenuEntry
	{
		/// <summary>The menu owning this entry.</summary>
		public readonly AbstractMenu BaseMenu;
		/// <summary>Full text.</summary>
		public string Text;
		/// <summary>Visible text, scrolls when too long.</summary>
		public string DisplayText(double TimeElapsed)
		{
			if (DisplayLength == 0)
			{
				return Text;
			}
			timer += TimeElapsed;
			if (timer > 0.5)
			{
				if (pause)
				{
					pause = false;
					return _displayText;
				}
				timer = 0;
				scroll++;
				if (scroll == Text.Length)
				{
					scroll = 0;
					pause = true;
				}
				_displayText = Text.Substring(scroll);
				if (_displayText.Length > _displayLength)
				{
					_displayText = _displayText.Substring(0, _displayLength);
				}
			}
			return _displayText;
		}
		/// <summary>Backing text for scrolling.</summary>
		private string _displayText;
		/// <summary>Visible length.</summary>
		private int _displayLength;
		/// <summary>Visible length (resets the scroll).</summary>
		public int DisplayLength
		{
			get => _displayLength;
			set
			{
				_displayLength = value;
				_displayText = Text.Substring(0, value);
				timer = 0;
			}
		}
		/// <summary>Icon drawn next to the entry.</summary>
		public Texture Icon;
		// Scroll state for overlong text.
		private double timer;
		private int scroll;
		private bool pause;

		protected MenuEntry(AbstractMenu menu)
		{
			BaseMenu = menu;
		}

	}
	/// <summary>A header line at the top of the menu.</summary>
	public class MenuCaption : MenuEntry
	{
		public MenuCaption(AbstractMenu menu, string Text) : base(menu)
		{
			this.Text = Text;
		}
	}
	/// <summary>An error line shown in the menu.</summary>
	public class MenuErrorDisplay : MenuEntry
	{
		public MenuErrorDisplay(AbstractMenu menu, string Text) : base(menu)
		{
			this.Text = Text;
		}
	}
}
