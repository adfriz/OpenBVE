using System;
using System.Collections.Generic;
using System.IO;
using OpenBveApi;

namespace OpenBveApi.Hosts
{
	public abstract partial class HostInterface
	{
		private static readonly Dictionary<string, string> TexturePluginNamespaces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{ ".dds", "Texture.Dds" },
			{ ".tga", "Texture.Tga" },
			{ ".ace", "Texture.Ace" },
			{ ".bmp", "Plugin" },
			{ ".png", "Plugin" },
			{ ".gif", "Plugin" },
			{ ".jpg", "Plugin" },
			{ ".jpeg", "Plugin" },
			{ ".tif", "Plugin" },
			{ ".tiff", "Plugin" }
		};

		/// <summary>
		/// Returns the plugin indices with the format's own decoder first. Hosts otherwise probe
		/// every texture plugin in filesystem order, so e.g. every .dds was opened by the BMP
		/// plugin before the DDS plugin got a chance - thousands of wasted file opens per route.
		/// Plugins that don't recognise the extension are still tried afterwards, so behaviour
		/// for mislabelled or container files is unchanged.
		/// </summary>
		protected static int[] OrderedTexturePlugins(string path, ContentLoadingPlugin[] plugins)
		{
			int[] order = new int[plugins.Length];
			string preferred = null;
			bool hasPreferred = path != null && TexturePluginNamespaces.TryGetValue(System.IO.Path.GetExtension(path), out preferred);
			int count = 0;
			if (hasPreferred)
			{
				for (int i = 0; i < plugins.Length; i++)
				{
					if (plugins[i].Texture != null && string.Equals(plugins[i].Texture.GetType().Namespace, preferred, StringComparison.Ordinal))
					{
						order[count++] = i;
					}
				}
			}
			for (int i = 0; i < plugins.Length; i++)
			{
				if (!hasPreferred || !string.Equals(plugins[i].Texture != null ? plugins[i].Texture.GetType().Namespace : null, preferred, StringComparison.Ordinal))
				{
					order[count++] = i;
				}
			}
			return order;
		}
	}
}
