using System;
using System.IO;
using OpenBveApi.Hosts;
using OpenBveApi.Textures;

namespace Texture.Dds {
	/// <summary>Implements the texture interface.</summary>
	public class Plugin : TextureInterface {
		private HostInterface host;
		// --- functions ---
		
		/// <summary>Called when the plugin is loaded.</summary>
		/// <param name="host">The host that loaded the plugin.</param>
		public override void Load(HostInterface host) {
			this.host = host;
		}
		
		/// <summary>Queries the dimensions of a texture.</summary>
		/// <param name="path">The path to the file or folder that contains the texture.</param>
		/// <param name="width">Receives the width of the texture.</param>
		/// <param name="height">Receives the height of the texture.</param>
		/// <returns>Whether querying the dimensions was successful.</returns>
		/// <remarks>Validates the DDS header and dimensions without reading image data.
		/// Structure and header fields:
		/// https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds-pguide
		/// https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header
		/// </remarks>
		public override bool QueryTextureDimensions(string path, out int width, out int height) {
			width = 0;
			height = 0;
			try
			{
				using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
				{
					if (stream.Length < 128)
					{
						return false;
					}
					using (BinaryReader reader = new BinaryReader(stream))
					{
						DdsHeader header = new DdsHeader(reader);
						DDSImage.ValidateHeader(header);
						width = header.width;
						height = header.height;
						return true;
					}
				}
			}
			catch
			{
				width = 0;
				height = 0;
				return false;
			}
		}
		
		/// <summary>Checks whether the plugin can load the specified texture.</summary>
		/// <param name="path">The path to the file or folder that contains the texture.</param>
		/// <returns>Whether the plugin can load the specified texture.</returns>
		public override bool CanLoadTexture(string path)
		{
			if (!File.Exists(path)) return false;
			try
			{
				using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
				{
					if (fileStream.Length < 128) return false;
					using (BinaryReader reader = new BinaryReader(fileStream))
					{
						DdsHeader header = new DdsHeader(reader);
						DDSImage.ValidateHeader(header);
						return true;
					}
				}
			}
			catch
			{
				return false;
			}
		}

		/// <summary>Loads the specified texture.</summary>
		/// <param name="path">The path to the file or folder that contains the texture.</param>
		/// <param name="texture">Receives the texture.</param>
		/// <returns>Whether loading the texture was successful.</returns>
		public override bool LoadTexture(string path, out OpenBveApi.Textures.Texture texture)
		{
			texture = null;
			try
			{
				// Native-capable hosts receive the full mip chain; other hosts use the top mip.
				using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
				{
					DDSImage d = new DDSImage(stream, host?.TextureCapabilities ?? TextureCapabilities.None);
					texture = d.myTexture;
					return texture != null;
				}
			}
			catch (Exception)
			{
				texture = null;
				return false;
			}
		}
		
	}
}
