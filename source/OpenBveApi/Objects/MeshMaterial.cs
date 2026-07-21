#pragma warning disable 0660, 0661
using System;
using OpenBveApi.Colors;
using OpenBveApi.Textures;

namespace OpenBveApi.Objects
{
	/// <summary>Represents material properties.</summary>
	public struct MeshMaterial
	{
		/// <summary>A bit mask combining constants of the MeshMaterial structure.</summary>
		public MaterialFlags Flags;
		/// <summary>The base color of the material</summary>
		public Color32 Color;
		/// <summary>The texture transparent color</summary>
		/// <remarks>Only valid if using <see cref="TextureTransparencyType.Partial"/></remarks>
		public Color24 TransparentColor;
		/// <summary>The material emissive color (upgraded to Color32 to support optional RGBA; alpha defaults to 255)</summary>
		public Color32 EmissiveColor;
		/// <summary>The material specular color</summary>
		/// <remarks>Only valid if <see cref="MaterialFlags.Specular"/> is set</remarks>
		public Color24 SpecularColor;
		/// <summary>The daytime texture</summary>
		public Texture DaytimeTexture;
		/// <summary>The night-time texture</summary>
		public Texture NighttimeTexture;
		/// <summary>The lightmap texture</summary>
		public Texture LightMapTexture;
		/// <summary>The normal map texture (filename suffix convention, e.g. _normal)</summary>
		public Texture NormalTexture;
		/// <summary>The emissive map texture (reserved for PBR)</summary>
		public Texture EmissiveTexture;
		/// <summary>The metallic/roughness map texture (reserved for PBR)</summary>
		public Texture MetallicRoughnessTexture;
		/// <summary>The ambient occlusion map texture (reserved for PBR)</summary>
		public Texture AmbientOcclusionTexture;
		/// <summary>Whether the ambient occlusion map is packed in the R channel of an ORM map (glTF convention)</summary>
		public bool AmbientOcclusionMapIsORM;
		/// <summary>The specular shininess exponent</summary>
		public float Shininess;
		/// <summary>Whether the normal map uses the DirectX (Y-) convention and the green channel must be flipped</summary>
		public bool NormalMapIsDirectX;
		/// <summary>The blend mode for this material</summary>
		public MeshMaterialBlendMode BlendMode;
		/// <summary>A bit mask specifying the glow properties. Use GetGlowAttenuationData to create valid data for this field.</summary>
		[CLSCompliant(false)]
		public ushort GlowAttenuationData;
		/// <summary>The wrap mode, or null to allow the renderer to decide</summary>
		public OpenGlTextureWrapMode? WrapMode;

		/// <summary>Returns whether two MeshMaterial structs are equal</summary>
		public static bool operator ==(MeshMaterial A, MeshMaterial B)
		{
			if (A.Flags != B.Flags) return false;
			if (A.Color != B.Color) return false;
			if (A.TransparentColor != B.TransparentColor) return false;
			if (A.EmissiveColor != B.EmissiveColor) return false;
			if (A.SpecularColor != B.SpecularColor) return false;
			if (A.DaytimeTexture != B.DaytimeTexture) return false;
			if (A.NighttimeTexture != B.NighttimeTexture) return false;
			if (A.LightMapTexture != B.LightMapTexture) return false;
			if (A.NormalTexture != B.NormalTexture) return false;
			if (A.EmissiveTexture != B.EmissiveTexture) return false;
			if (A.MetallicRoughnessTexture != B.MetallicRoughnessTexture) return false;
			if (A.AmbientOcclusionTexture != B.AmbientOcclusionTexture) return false;
			if (A.AmbientOcclusionMapIsORM != B.AmbientOcclusionMapIsORM) return false;
			if (A.Shininess != B.Shininess) return false;
			if (A.NormalMapIsDirectX != B.NormalMapIsDirectX) return false;
			if (A.BlendMode != B.BlendMode) return false;
			if (A.GlowAttenuationData != B.GlowAttenuationData) return false;
			if (A.WrapMode != B.WrapMode) return false;
			return true;
		}

		/// <summary>Returns whether two MeshMaterial structs are unequal</summary>
		public static bool operator !=(MeshMaterial A, MeshMaterial B)
		{
			if (A.Flags != B.Flags) return true;
			if (A.Color != B.Color) return true;
			if (A.TransparentColor != B.TransparentColor) return true;
			if (A.EmissiveColor != B.EmissiveColor) return true;
			if (A.SpecularColor != B.SpecularColor) return true;
			if (A.DaytimeTexture != B.DaytimeTexture) return true;
			if (A.NighttimeTexture != B.NighttimeTexture) return true;
			if (A.LightMapTexture != B.LightMapTexture) return true;
			if (A.NormalTexture != B.NormalTexture) return true;
			if (A.EmissiveTexture != B.EmissiveTexture) return true;
			if (A.MetallicRoughnessTexture != B.MetallicRoughnessTexture) return true;
			if (A.AmbientOcclusionTexture != B.AmbientOcclusionTexture) return true;
			if (A.AmbientOcclusionMapIsORM != B.AmbientOcclusionMapIsORM) return true;
			if (A.Shininess != B.Shininess) return true;
			if (A.NormalMapIsDirectX != B.NormalMapIsDirectX) return true;
			if (A.BlendMode != B.BlendMode) return true;
			if (A.GlowAttenuationData != B.GlowAttenuationData) return true;
			if (A.WrapMode != B.WrapMode) return true;
			return false;
		}
	}
}
