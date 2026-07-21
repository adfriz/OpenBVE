using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenBveApi.Objects.Helpers
{
	/// <summary>
	/// Defines the texture map types recognised from filename suffixes.
	/// Used by the CSV/B3D object parser to auto-assign texture slots without explicit commands.
	/// The suffix set is data-driven so that additional PBR map types can be added without parser changes.
	/// </summary>
	public enum TextureMapType
	{
		/// <summary>Not a recognised map suffix (regular diffuse/color texture).</summary>
		None,
		/// <summary>A tangent-space normal map (OpenGL Y+ by default).</summary>
		Normal,
		/// <summary>A tangent-space normal map in the DirectX (Y-) convention (green channel must be flipped).</summary>
		NormalDirectX,
		/// <summary>An emissive map (legacy-conflict detection only; not loaded as a texture).</summary>
		Emissive,
		/// <summary>A roughness map (reserved for PBR).</summary>
		Roughness,
		/// <summary>A metallic map (reserved for PBR).</summary>
		Metallic,
		/// <summary>An ambient occlusion map (reserved for PBR).</summary>
		AmbientOcclusion,
		/// <summary>A packed ORM map: R = AO, G = Roughness, B = Metallic (glTF convention, reserved for PBR).</summary>
		ORM
	}

	/// <summary>
	/// Provides filename-suffix based detection of texture map types.
	/// Suffixes are matched case-insensitively, immediately before the file extension.
	/// </summary>
	public static class TextureMapConvention
	{
		private static readonly Dictionary<string, TextureMapType> Suffixes = new Dictionary<string, TextureMapType>(StringComparer.OrdinalIgnoreCase)
		{
			// Normal maps (OpenGL Y+ default)
			{ "_normal", TextureMapType.Normal },
			{ "_norm", TextureMapType.Normal },
			{ "_nrm", TextureMapType.Normal },
			{ "_normal_opengl", TextureMapType.Normal },
			// Normal maps (DirectX Y- -> flip green)
			{ "_normal_directx", TextureMapType.NormalDirectX },
			{ "_normal_dx", TextureMapType.NormalDirectX },
			// Emissive (legacy-conflict only)
			{ "_emissive", TextureMapType.Emissive },
			{ "_emission", TextureMapType.Emissive },
			// Reserved PBR types
			{ "_rough", TextureMapType.Roughness },
			{ "_roughness", TextureMapType.Roughness },
			{ "_metal", TextureMapType.Metallic },
			{ "_metallic", TextureMapType.Metallic },
			{ "_ao", TextureMapType.AmbientOcclusion },
			{ "_ambientocclusion", TextureMapType.AmbientOcclusion },
			{ "_orm", TextureMapType.ORM },
			{ "_arm", TextureMapType.ORM }
		};

		/// <summary>
		/// Inspects a texture filename and reports the recognised map type.
		/// </summary>
		/// <param name="fileName">The texture file name or path.</param>
		/// <param name="mapType">The recognised map type.</param>
		/// <returns>True if a recognised map suffix was found; false for a regular diffuse texture.</returns>
		public static bool TryGetMapType(string fileName, out TextureMapType mapType)
		{
			mapType = TextureMapType.None;
			if (string.IsNullOrEmpty(fileName))
			{
				return false;
			}

			string name = System.IO.Path.GetFileNameWithoutExtension(fileName);
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}

		// Match the longest suffix first so that e.g. "_normal_directx" wins over "_normal".
		foreach (var pair in Suffixes.OrderByDescending(s => s.Key.Length))
		{
			if (name.Length > pair.Key.Length && name.EndsWith(pair.Key, StringComparison.OrdinalIgnoreCase))
			{
				mapType = pair.Value;
				return true;
			}
		}

			return false;
		}
	}
}
