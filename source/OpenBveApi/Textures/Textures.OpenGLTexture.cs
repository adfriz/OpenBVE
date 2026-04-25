using Raylib_cs;

namespace OpenBveApi.Textures
{
	/// <summary>Represents a graphics API texture (Raylib/OpenGL).</summary>
	public class OpenGlTexture
	{
		/// <summary>Whether the texture has been loaded and is valid.</summary>
		public bool Valid;
		/// <summary>The internal texture handle (for rlgl/OpenGL).</summary>
		public uint Name;
		/// <summary>The Raylib Texture2D object.</summary>
		public Texture2D RaylibTexture;
		/// <summary>Whether the texture has been used.</summary>
		public bool Used;
	}
}
