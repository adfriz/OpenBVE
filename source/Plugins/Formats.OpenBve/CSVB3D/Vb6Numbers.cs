//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2026, Christopher Lees, The OpenBVE Project
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

using System;
using System.Globalization;

namespace Formats.OpenBve
{
	/// <summary>
	/// Allocation-free equivalents of the VB6 number readers used by the CSV/B3D reader.
	/// The shared OpenBveApi versions allocate a LINQ iterator, a StringBuilder and a string
	/// for every single numeric token (TrimInside). Parser objects may be loaded in parallel,
	/// so the small normalisation buffer is thread-local instead of shared: no locks, no
	/// per-token heap traffic, identical parse semantics.
	/// </summary>
	internal static class Vb6Numbers
	{
		private const char ReplacementDash = '\uFFFD';
		private const char EmDash = '\u2014';
		private const char EnDash = '\u2013';
		private const NumberStyles Styles = NumberStyles.Float;

		[ThreadStatic] private static char[] normalisationBuffer;

		private static bool IsDash(char c)
		{
			return c == ReplacementDash || c == EmDash || c == EnDash;
		}

		/// <summary>Returns the expression unchanged when it is already clean; otherwise removes all whitespace into a reusable buffer.</summary>
		private static string Clean(string expression, bool convertDash)
		{
			if (expression == null)
			{
				return string.Empty;
			}
			for (int i = 0; i < expression.Length; i++)
			{
				if (char.IsWhiteSpace(expression[i]) || (convertDash && i == 0 && IsDash(expression[0])))
				{
					return Normalise(expression, convertDash);
				}
			}
			return expression;
		}

		private static string Normalise(string expression, bool convertDash)
		{
			char[] buffer = normalisationBuffer;
			if (buffer == null || buffer.Length < expression.Length)
			{
				buffer = new char[Math.Max(64, expression.Length)];
				normalisationBuffer = buffer;
			}
			int count = 0;
			for (int i = 0; i < expression.Length; i++)
			{
				char c = expression[i];
				if (char.IsWhiteSpace(c))
				{
					continue;
				}
				if (convertDash && i == 0 && IsDash(c))
				{
					c = '-';
				}
				buffer[count++] = c;
			}
			return new string(buffer, 0, count);
		}

		/// <summary>Parses a double formatted as a Visual Basic 6 string.</summary>
		internal static bool TryParseDouble(string expression, out double value)
		{
			if (string.IsNullOrEmpty(expression))
			{
				value = 0.0;
				return false;
			}
			string text = Clean(expression, true);
			if (double.TryParse(text, Styles, CultureInfo.InvariantCulture, out value))
			{
				return true;
			}
			for (int n = text.Length - 1; n > 0; n--)
			{
				if (double.TryParse(text.Substring(0, n), Styles, CultureInfo.InvariantCulture, out value))
				{
					return true;
				}
			}
			value = 0.0;
			return false;
		}

		/// <summary>Parses an integer formatted as a Visual Basic 6 string.</summary>
		internal static bool TryParseInt(string expression, out int value)
		{
			if (string.IsNullOrEmpty(expression))
			{
				value = 0;
				return false;
			}
			string text = Clean(expression, false);
			if (double.TryParse(text, Styles, CultureInfo.InvariantCulture, out double a))
			{
				if (a >= -2147483648.0 & a <= 2147483647.0)
				{
					value = (int)Math.Round(a);
					return true;
				}
				value = 0;
				return false;
			}
			for (int n = text.Length - 1; n > 0; n--)
			{
				if (double.TryParse(text.Substring(0, n), Styles, CultureInfo.InvariantCulture, out a))
				{
					if (a >= -2147483648.0 & a <= 2147483647.0)
					{
						value = (int)Math.Round(a);
						return true;
					}
					break;
				}
			}
			value = 0;
			return false;
		}

		/// <summary>Parses a byte bounded number formatted as a Visual Basic 6 string.</summary>
		internal static bool TryParseByte(string expression, out int value)
		{
			if (string.IsNullOrEmpty(expression) || expression.IndexOf(',') != -1)
			{
				value = 0;
				return false;
			}
			string text = Clean(expression, false);
			bool parsed = double.TryParse(text, Styles, CultureInfo.InvariantCulture, out double a);
			if (!parsed)
			{
				for (int n = text.Length - 1; n > 0; n--)
				{
					if (double.TryParse(text.Substring(0, n), Styles, CultureInfo.InvariantCulture, out a))
					{
						parsed = true;
						break;
					}
				}
			}
			if (!parsed)
			{
				value = 0;
				return false;
			}
			value = (int)Math.Round(a);
			if (a >= 0 & a <= 255)
			{
				return true;
			}
			value = value < 0 ? 0 : 255;
			return false;
		}
	}
}
