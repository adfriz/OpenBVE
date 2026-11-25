using System;
using System.Drawing;
using System.IO;
using OpenBveApi;
using OpenBveApi.FileSystem;
using OpenBveApi.Hosts;
using OpenBveApi.Textures;

namespace Plugin {
	/// <summary>Implements the texture interface.</summary>
	public partial class Plugin : TextureInterface {
		
		/// <summary>The host that loaded the plugin.</summary>
		internal static HostInterface CurrentHost;
		/// <summary>The list of enabled hacks</summary>
		internal static CompatabilityHacks EnabledHacks;

		internal static BaseOptions CurrentOptions;
		
		public override void Load(HostInterface host, FileSystem fileSystem, BaseOptions options) {
			CurrentHost = host;
			CurrentOptions = options;
		}

		public override void SetCompatabilityHacks(CompatabilityHacks enabledHacks)
		{
			EnabledHacks = enabledHacks;
		}

		/// <summary>Queries the dimensions of a texture.</summary>
		/// <param name="path">The path to the file or folder that contains the texture.</param>
		/// <param name="width">Receives the width of the texture.</param>
		/// <param name="height">Receives the height of the texture.</param>
		/// <returns>Whether querying the dimensions was successful.</returns>
		public override bool QueryTextureDimensions(string path, out int width, out int height) {
			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
				using (BinaryReader reader = new BinaryReader(stream)) {
					uint identifier1 = reader.ReadUInt32();
					uint identifier2 = reader.ReadUInt32();
					if ((identifier1 & 0xFFFF) == 0x4D42) {
						/* BMP */
						stream.Position = 18;
						width = reader.ReadInt32();
						height = reader.ReadInt32();
						return true;
					}
				    if (identifier1 == 0x38464947 & ((identifier2 & 0xFFFF) == 0x6137 | (identifier2 & 0xFFFF) == 0x6139)) {
				        /* GIF */
				        stream.Position = 6;
				        width = reader.ReadUInt16();
				        height = reader.ReadUInt16();
				        return true;
				    }
				    if (identifier1 == 0x474E5089 & identifier2 == 0x0A1A0A0D) {
				        /* PNG */
				        if (reader.ReadUInt32() == 0x0D000000) {
				            if (reader.ReadUInt32() == 0x52444849) {
				                uint bigWidth = reader.ReadUInt32();
				                uint bigHeight = reader.ReadUInt32();
				                width = (int)((bigWidth >> 24) | ((bigWidth >> 8) & 0xFF00) | ((bigWidth & 0xFF00) << 8) | (bigWidth << 24));
				                height = (int)((bigHeight >> 24) | ((bigHeight >> 8) & 0xFF00) | ((bigHeight & 0xFF00) << 8) | (bigHeight << 24));
				                return true;
				            }
				        }
				    }
				}
			}
			using (Image image = Image.FromFile(path)) {
				width = image.Width;
				height = image.Height;
				return true;
			}
		}
		
		/// <summary>Checks whether the plugin can load the specified texture.</summary>
		/// <param name="path">The path to the file or folder that contains the texture.</param>
		/// <returns>Whether the plugin can load the specified texture.</returns>
		public override bool CanLoadTexture(string path) {
			try
			{
				// One open for both the Nautilus check and the signature sniff. The old code opened
				// the file twice here (FileFormats.IsNautilusFile + this stream), which matters with
				// thousands of textures per route.
				using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					if (stream.Length < 8)
					{
						return false;
					}
					byte[] header = new byte[16];
					int read = stream.Read(header, 0, header.Length);
					if (stream.Length >= 32 && read >= 16 &&
					    BitConverter.ToUInt32(header, 0) == 0x554c544eu && BitConverter.ToUInt32(header, 4) == 0x4d524453u &&
					    BitConverter.ToUInt32(header, 8) == 0x14131211u && BitConverter.ToUInt32(header, 12) == 0x00811919u)
					{
						return false;
					}
					uint identifier1 = BitConverter.ToUInt32(header, 0);
					uint identifier2 = BitConverter.ToUInt32(header, 4);
					if ((identifier1 & 0xFFFF) == 0x4D42) {
						/* BMP */
						return true;
					}

					if (identifier1 == 0x38464947 & ((identifier2 & 0xFFFF) == 0x6137 | (identifier2 & 0xFFFF) == 0x6139)) {
						/* GIF */
						return true;
					}

					if (identifier1 == 0xE0FFD8FF | identifier1 == 0xE1FFD8FF) {
						/* JPEG */
						return true;
					}

					if (identifier1 == 0x474E5089 & identifier2 == 0x0A1A0A0D) {
						/* PNG */
						return true;
					}

					if (identifier1 == 0x002A4949 | identifier1 == 0x2A004D4D) {
						/* TIFF */
						return true;
					}
				}
			}
			catch
			{
				return false;
			}
			return false;
		}
		
		/// <summary>Loads the specified texture.</summary>
		/// <param name="path">The path to the file or folder that contains the texture.</param>
		/// <param name="texture">Receives the texture.</param>
		/// <returns>Whether loading the texture was successful.</returns>
		public override bool LoadTexture(string path, out Texture texture) {
			return Parse(path, out texture);
		}
		
	}
}
