using OpenBveApi.Colors;
using OpenBveApi.Math;
using OpenBveApi.Textures;

namespace OpenBveApi.Objects
{
	/// <summary>Helper class describing textual material properties to be applied to a 3D face</summary>
	public class Material {
		/// <summary>The base color</summary>
		public Color32 Color;
		/// <summary>The emissive color (RGBA — supports optional alpha for advanced transparency effects)</summary>
		public Color32 EmissiveColor;
		/// <summary>The specular color</summary>
		public Color24 SpecularColor;
		/// <summary>The transparent color</summary>
		public Color24 TransparentColor;
		/// <summary>Describes the special properties (if any) of the material</summary>
		public MaterialFlags Flags;
		/// <summary>The absolute on-disk path to the daytime texture</summary>
		public string DaytimeTexture;
		/// <summary>The absolute on-disk path to the nighttime texture</summary>
		public string NighttimeTexture;
		/// <summary>The absolute on-disk path to the transparency texture (Used for both daytime and nighttime textures)</summary>
		public string TransparencyTexture;
		/// <summary>The absolute on-disk path to the light map texture</summary>
		public string LightMap;
		/// <summary>Metallic factor for PBR materials (0 dielectric - 1 metal)</summary>
		public float MetallicFactor;
		/// <summary>Roughness factor for PBR materials (0 smooth - 1 rough)</summary>
		public float RoughnessFactor;
		/// <summary>The absolute on-disk path to the PBR normal map texture</summary>
		public string NormalTexture;
		/// <summary>Normal map strength scale</summary>
		public float NormalScale;
		/// <summary>The absolute on-disk path to the PBR occlusion texture</summary>
		public string OcclusionTexture;
		/// <summary>Occlusion strength</summary>
		public float OcclusionStrength;
		/// <summary>The absolute on-disk path to the PBR emissive texture</summary>
		public string EmissiveTexture;
		/// <summary>The blend mode to be used</summary>
		public MeshMaterialBlendMode BlendMode;
		/// <summary>The wrap mode, or a null reference if not in use</summary>
		public OpenGlTextureWrapMode? WrapMode;
		/// <summary>The glow attenuation data, as a packed ushort</summary>
		public ushort GlowAttenuationData;
		/// <summary>The text to overlay onto the texture when it is loaded</summary>
		public string Text;
		/// <summary></summary>
		public string Key;
		/// <summary>The color of the text to overlay (RGBA — alpha defaults to 255 for backward compatibility)</summary>
		public Color32 TextColor;
		/// <summary>The background color for the text (RGBA — alpha defaults to 255 for backward compatibility)</summary>
		public Color32 BackgroundColor;
		/// <summary>The font to use for the text</summary>
		public string Font;
		/// <summary>The text padding to apply</summary>
		public Vector2 TextPadding;
		/// <summary>Whether the first color in the texture palette is transparent</summary>
		/// <remarks>Used by Mechanik routes</remarks>
		public bool FirstColorTransparent;

		/// <summary>Creates a new Material with default properties</summary>
		public Material() {
			this.Color = Color32.White;
			this.EmissiveColor = Color32.Black;
			this.TransparentColor = Color24.Black;
			this.SpecularColor = Color24.Black;
			this.Flags = MaterialFlags.None;
			this.DaytimeTexture = null;
			this.NighttimeTexture = null;
			this.BlendMode = MeshMaterialBlendMode.Normal;
			this.GlowAttenuationData = 0;
			this.TextColor = Color32.Black;
			this.BackgroundColor = Color32.White;
			this.TextPadding = new Vector2(0, 0);
			this.Font = "Arial";
			this.WrapMode = null;
			this.MetallicFactor = 1.0f;
			this.RoughnessFactor = 1.0f;
			this.NormalTexture = null;
			this.NormalScale = 1.0f;
			this.OcclusionTexture = null;
			this.OcclusionStrength = 1.0f;
			this.EmissiveTexture = null;
		}

		/// <summary>Creates a new material from the specified texture file with default properties</summary>
		/// <param name="textureFile"></param>
		public Material(string textureFile) {
			this.Color = Color32.White;
			this.EmissiveColor = Color32.Black;
			this.TransparentColor = Color24.Black;
			this.SpecularColor = Color24.Black;
			this.Flags = MaterialFlags.None;
			this.DaytimeTexture = null;
			this.NighttimeTexture = null;
			this.BlendMode = MeshMaterialBlendMode.Normal;
			this.GlowAttenuationData = 0;
			this.TextColor = Color32.Black;
			this.BackgroundColor = Color32.White;
			this.TextPadding = new Vector2(0, 0);
			this.Font = "Arial";
			this.WrapMode = null;
			this.MetallicFactor = 1.0f;
			this.RoughnessFactor = 1.0f;
			this.NormalTexture = null;
			this.NormalScale = 1.0f;
			this.OcclusionTexture = null;
			this.OcclusionStrength = 1.0f;
			this.EmissiveTexture = null;
			this.DaytimeTexture = textureFile;
		}

		/// <summary>Clones an existing material</summary>
		public Material(Material prototypeMaterial) {
			this.Color = prototypeMaterial.Color;
			this.EmissiveColor = prototypeMaterial.EmissiveColor;
			this.TransparentColor = prototypeMaterial.TransparentColor;
			this.SpecularColor = prototypeMaterial.SpecularColor;
			this.Flags = prototypeMaterial.Flags;
			this.DaytimeTexture = prototypeMaterial.DaytimeTexture;
			this.NighttimeTexture = prototypeMaterial.NighttimeTexture;
			this.BlendMode = prototypeMaterial.BlendMode;
			this.GlowAttenuationData = prototypeMaterial.GlowAttenuationData;
			this.TextColor = prototypeMaterial.TextColor;
			this.BackgroundColor = prototypeMaterial.BackgroundColor;
			this.TextPadding = prototypeMaterial.TextPadding;
			this.Font = prototypeMaterial.Font;
			this.WrapMode = prototypeMaterial.WrapMode;
			this.MetallicFactor = prototypeMaterial.MetallicFactor;
			this.RoughnessFactor = prototypeMaterial.RoughnessFactor;
			this.NormalTexture = prototypeMaterial.NormalTexture;
			this.NormalScale = prototypeMaterial.NormalScale;
			this.OcclusionTexture = prototypeMaterial.OcclusionTexture;
			this.OcclusionStrength = prototypeMaterial.OcclusionStrength;
			this.EmissiveTexture = prototypeMaterial.EmissiveTexture;
		}
	}
}
