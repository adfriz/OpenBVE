//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2026, The OpenBVE Project
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
using System.IO;
using OpenBveApi.FileSystem;
using OpenBveApi.Hosts;
using OpenBveApi.Interface;
using OpenBveApi.Objects;

namespace Plugin
{
    public class Plugin : ObjectInterface
    {
	    internal static HostInterface CurrentHost;

	    public override string[] SupportedStaticObjectExtensions => new[] { ".gltf", ".glb" };

	    public override void Load(HostInterface host, FileSystem fileSystem) {
		    CurrentHost = host;
	    }

	    public override bool CanLoadObject(string path)
	    {
		    if (string.IsNullOrEmpty(path) || !File.Exists(path))
		    {
			    return false;
		    }
		    try
		    {
			    if (path.EndsWith(".glb", StringComparison.InvariantCultureIgnoreCase))
			    {
				    // A .glb starts with the magic word "glTF"
				    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				    {
					    byte[] magic = new byte[4];
					    return stream.Read(magic, 0, 4) == 4 && magic[0] == 'g' && magic[1] == 'l' && magic[2] == 'T' && magic[3] == 'F';
				    }
			    }
			    if (path.EndsWith(".gltf", StringComparison.InvariantCultureIgnoreCase))
			    {
				    // A .gltf is JSON, so the first thing in it should be '{'
				    using (StreamReader reader = new StreamReader(path))
				    {
					    int c;
					    do
					    {
						    c = reader.Read();
					    } while (c >= 0 && char.IsWhiteSpace((char)c));
					    return c == '{';
				    }
			    }
		    }
		    catch
		    {
			    // ignored
		    }
		    return false;
	    }

	    public override bool LoadObject(string path, System.Text.Encoding textEncoding, out UnifiedObject unifiedObject)
	    {
		    try
		    {
			    unifiedObject = GltfObject.ReadObject(path);
			    return true;
		    }
		    catch (Exception ex)
		    {
			    unifiedObject = null;
			    CurrentHost.AddMessage(MessageType.Error, false, "An unexpected error occured whilst attempting to load the following object: " + path + " (" + ex.Message + ")");
		    }
		    return false;
	    }
    }
}
