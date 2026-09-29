using System;
using System.Collections.Generic;
using System.Linq;
using LibRender2.Text;
using OpenBveApi.Colors;
using OpenBveApi.Graphics;
using OpenBveApi.Math;

namespace LibRender2.Primitives
{
	public class Textbox : TextControl
	{
		/// <summary>Line font.</summary>
		private readonly OpenGlFont myFont;
		/// <summary>Line color.</summary>
		private readonly Color128 myFontColor;
		/// <summary>Scrollbar handle color.</summary>
		private readonly Color128 myScrollbarColor;
		/// <summary>Contents (setting resets the scroll).</summary>
		public string Text
		{
			get => myText;
			set
			{
				myText = value;
				topLine = 0;
			}
		}
		/// <summary>Backing text.</summary>
		private string myText;

		/// <summary>Border width.</summary>
		public readonly int Border;
		/// <summary>First visible line.</summary>
		private int topLine;
		/// <summary>Whether scrolling is possible.</summary>
		public bool CanScroll;
		/// <summary>Usable area (shrinks when the scrollbar shows).</summary>
		private Vector2 internalSize => CanScroll ? new Vector2(Size.X, Size.Y - 12) : Size;

		// Wraps text to fit a pixel width (also splits on newlines).
		private List<string> WrappedLines(int width)
		{
			// Split on real newlines and on escaped ones from language files.
			string[] firstSplit = Text.Split(new[] {"\r\n", "\n", @"\r\n"}, StringSplitOptions.None);
			List<string> wrappedLines = new List<string>();
			string currentLine = string.Empty;
			for(int j = 0; j < firstSplit.Length; j++)
			{
				for (int i = 0; i < firstSplit[j].Length; i++)
				{
					char currentChar = firstSplit[j][i];
					currentLine += currentChar;
					if (myFont.MeasureString(currentLine).X > width)
					{
						if (currentLine.Any(char.IsWhiteSpace))
						{
							// Too long: back up to the last space.
							int moveback = 1;
							while (!char.IsWhiteSpace(currentLine[currentLine.Length - moveback]))
							{
								moveback++;
								i--;
							}
							string lineToAdd = currentLine.Substring(0, currentLine.Length - moveback);
							wrappedLines.Add(lineToAdd.TrimStart());
						}
						else
						{
							i--;
							string lineToAdd = currentLine.Substring(0, currentLine.Length - 1);
							wrappedLines.Add(lineToAdd.TrimStart());
						}
						currentLine = string.Empty;
					}
				}
				wrappedLines.Add(currentLine.TrimStart());
				currentLine = string.Empty;
			}
			
			if (currentLine.Length > 0)
			{
				wrappedLines.Add(currentLine.TrimStart());
			}
			return wrappedLines;
		}

		public Textbox(BaseRenderer Renderer, OpenGlFont font, Color128 FontColor, Color128 backgroundColor) : base(Renderer)
		{
			Font = font;
			myFont = font;
			myFontColor = FontColor;
			Border = 5;
			topLine = 0;
			Texture = null;
			BackgroundColor = backgroundColor;
			myScrollbarColor = Color128.Orange;
		}

		public void VerticalScroll(int numberOfLines)
		{
			topLine += numberOfLines;
			if (topLine < 0)
			{
				topLine = 0;
			}
		}

		public override void Draw()
		{
			DrawFrame();
			if (string.IsNullOrEmpty(Text))
			{
				return;
			}

			List<string> splitString = WrappedLines((int)internalSize.Y - Border * 2);
			if (splitString.Count == 1)
			{
				// Single line.
				DrawTextAt(myFont, Text, new Vector2(Location.X + Border, Location.Y + Border), myFontColor);
				CanScroll = false;
			}
			else
			{
				int maxFittingLines = (int)(internalSize.Y / myFont.FontSize);
				if (topLine + maxFittingLines > splitString.Count)
				{
					topLine = Math.Max(0, splitString.Count - maxFittingLines);
				}
				CanScroll = maxFittingLines < splitString.Count;
				if (CanScroll)
				{
					// Scrollbar track.
					Renderer.Rectangle.Draw(null, new Vector2(Location.X + Size.X - 12, Location.Y + 2), new Vector2(8, Size.Y - 4), Color128.Grey);
				}
				// Visible lines.
				int currentLine = topLine;
				int bottomLine = Math.Min(maxFittingLines, splitString.Count);
				for (int i = 0; i < bottomLine; i++)
				{
					DrawTextAt(myFont, splitString[currentLine], new Vector2(Location.X + Border, Location.Y + Border + myFont.FontSize * i), myFontColor);
					currentLine++;
				}

				if (CanScroll)
				{
					// Scrollbar handle.
					double scrollBarHeight = (Size.Y - 4) * maxFittingLines / splitString.Count;
					double percentageScroll = topLine / (double)(splitString.Count - maxFittingLines);
					Renderer.Rectangle.Draw(null, new Vector2(Location.X + Size.X - 13, Location.Y + (Size.Y - scrollBarHeight) * percentageScroll), new Vector2(10, scrollBarHeight), myScrollbarColor);
				}

			}
		}

		public override void MouseMove(int x, int y)
		{
			CurrentlySelected = HitTest(x, y);
			Renderer.SetCursor(CurrentlySelected && CanScroll ? AvailableCursors.ScrollCursor : OpenTK.MouseCursor.Default);
		}
	}
}
