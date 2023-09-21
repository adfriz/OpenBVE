//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2020, Christopher Lees, The OpenBVE Project
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenBveApi.FunctionScripting;
using OpenBveApi.Hosts;
using OpenBveApi.Interface;
using OpenBveApi.Math;

namespace Formats.OpenBve
{
	public abstract class Block <T, TT> where T : struct, Enum where TT : struct, Enum
    {
		public abstract Block<T, TT> ReadNextBlock();

	    public virtual int RemainingSubBlocks => 0;

	    public readonly T Key;

	    internal readonly HostInterface currentHost;

	    public virtual bool GetValue(TT key, out string value)
	    {
		    value = string.Empty;
		    return false;
	    }

	    public virtual bool GetVector2(TT key, out Vector2 value)
	    {
			value = Vector2.Null;
			return false;
	    }

	    public virtual bool GetVector3(TT key, out Vector3 value)
	    {
		    value = Vector3.Zero;
		    return false;
	    }

	    public virtual bool GetStringArray(TT key, char separator, out string[] values)
	    {
		    values = new string[0];
		    return false;
	    }

	    public virtual bool GetFunctionScript(TT key, out FunctionScript function)
	    {
		    function = null;
		    return false;
	    }

	    protected Block(T myKey, HostInterface host)
	    {
		    Key = myKey;
		    currentHost = host;
	    }
    }

	/// <summary>Root block for a .CFG type file</summary>
    public class ConfigFile <T, TT> : Block<T, TT> where T : struct, Enum where TT : struct, Enum
	{
		private readonly string[] myLines;

		private readonly List<Block<T, TT>> subBlocks;

		public ConfigFile(string fileName, Encoding encoding, HostInterface host) : base(default, host)
		{
			myLines = File.ReadAllLines(fileName, encoding);
			subBlocks = new List<Block<T, TT>>();
			List<string> blockLines = new List<string>();
			bool addToBlock = false;
			T currentSection = default(T);
			//string 

			for (int i = 0; i < myLines.Length; i++)
			{
				if (myLines[i].StartsWith("[") && myLines[i].EndsWith("]"))
				{
					string sct = myLines[i].Trim().Trim('[', ']');
					
					if (!Enum.TryParse(sct, true, out currentSection))
					{
						addToBlock = false;
						currentHost.AddMessage(MessageType.Error, false, "Unknown Section " + sct + " encountered in file " + fileName + " at Line " + i);
					}
					else
					{
						addToBlock = true;
					}
					if (blockLines.Count > 0)
					{
						subBlocks.Add(new ConfigSection<T, TT>(currentSection, blockLines.ToArray(), currentHost));
						blockLines.Clear();
					}
				}
				else
				{
					if (addToBlock)
					{
						blockLines.Add(myLines[i]);
					}
				}
			}
			// final block
			if (blockLines.Count > 0)
			{
				subBlocks.Add(new ConfigSection<T, TT>(currentSection, blockLines.ToArray(), currentHost));
			}
		}

		public ConfigFile(string[] Lines, HostInterface Host) : base(default, Host)
		{
			myLines = Lines;
			subBlocks = new List<Block<T, TT>>();
			List<string> blockLines = new List<string>();
			bool addToBlock = false;
			T currentSection = default(T);
			//string 

			for (int i = 0; i < myLines.Length; i++)
			{
				if (myLines[i].StartsWith("[") && myLines[i].EndsWith("]"))
				{
					string sct = myLines[i].Trim().Trim('[', ']');
					
					if (!Enum.TryParse(sct, true, out currentSection))
					{
						addToBlock = false;
						currentHost.AddMessage(MessageType.Error, false, "Unknown Section " + sct + " encountered at Line " + i);
					}
					else
					{
						addToBlock = true;
					}
					// add error
					if (blockLines.Count > 0)
					{
						subBlocks.Add(new ConfigSection<T, TT>(currentSection, blockLines.ToArray(), currentHost));
						blockLines.Clear();
					}
				}
				else
				{
					if (addToBlock)
					{
						blockLines.Add(myLines[i]);
					}
				}
			}
			// final block
			if (blockLines.Count > 0)
			{
				subBlocks.Add(new ConfigSection<T, TT>(currentSection, blockLines.ToArray(), currentHost));
			}
		}

		public override Block<T, TT> ReadNextBlock()
		{
			Block<T, TT> b = subBlocks.First();
			subBlocks.RemoveAt(0);
			return b;
		}

		public override int RemainingSubBlocks => subBlocks.Count;
	}

	public class ConfigSection <T, TT> : Block<T, TT> where T : struct, Enum where TT : struct, Enum
	{
		private Dictionary<TT, string> keyValuePairs;
		public override Block<T, TT> ReadNextBlock()
		{
			throw new InvalidDataException("A section in a CFG file cannot contain sub-blocks.");
		}

		internal ConfigSection(T myKey, string[] myLines, HostInterface Host) : base(myKey, Host)
		{
			keyValuePairs = new Dictionary<TT, string>();
			for (int i = 0; i < myLines.Length; i++)
			{
				int j = myLines[i].IndexOf("=", StringComparison.Ordinal);
				if (j > 0)
				{
					string a = myLines[i].Substring(0, j).TrimEnd();
					string b = myLines[i].Substring(j + 1).TrimStart();
					TT key;
					if (Enum.TryParse(a, true, out key))
					{
						keyValuePairs.Add(key, b);
					}
					else
					{
						// add error
					}
				}
			}
		}

		public override bool GetStringArray(TT key, char separator, out string[] values)
		{
			if (keyValuePairs.TryGetValue(key, out var value))
			{
				values = value.Split(separator);
				return true;
			}
			values = new string[0];
			return false;
		}

		public override bool GetFunctionScript(TT key, out FunctionScript function)
		{

			if (keyValuePairs.TryGetValue(key, out var script))
			{
				try
				{
					bool isInfix = key.ToString().IndexOf("RPN", StringComparison.Ordinal) != -1;
					function = new FunctionScript(currentHost, script, isInfix);
					return true;
				}
				catch
				{
					function = null;
					return false;
				}
			}
			function = null;
			return false;
			
		}
	}
}
