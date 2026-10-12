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
		/// <summary>The blend mode for this material</summary>
		public MeshMaterialBlendMode BlendMode;
		/// <summary>A bit mask specifying the glow properties. Use GetGlowAttenuationData to create valid data for this field.</summary>
		[CLSCompliant(false)]
		public ushort GlowAttenuationData;
		/// <summary>The wrap mode, or null to allow the renderer to decide</summary>
		public OpenGlTextureWrapMode? WrapMode;
		/// <summary>Metallic factor for PBR materials (0 dielectric - 1 metal)</summary>
		public float MetallicFactor;
		/// <summary>Roughness factor for PBR materials (0 smooth - 1 rough)</summary>
		public float RoughnessFactor;
		/// <summary>The PBR normal map texture</summary>
		public Texture NormalTexture;
		/// <summary>Normal map strength scale</summary>
		public float NormalScale;
		/// <summary>The PBR occlusion texture</summary>
		public Texture OcclusionTexture;
		/// <summary>Occlusion strength</summary>
		public float OcclusionStrength;
		/// <summary>The PBR emissive texture</summary>
		public Texture EmissiveTexture;
		/// <summary>The PBR metallic-roughness (ARM) texture: R occlusion, G roughness, B metallic</summary>
		public Texture MetallicRoughnessTexture;
		/// <summary>Thin-glass transmission factor 0-1 (KHR_materials_transmission approx, file-agnostic)</summary>
		public float TransmissionFactor;
		/// <summary>Dielectric specular weight 0-1 (KHR_materials_specular, file-agnostic)</summary>
		public float SpecularFactor;
		/// <summary>Dielectric F0 color factor, linear RGB, may exceed 1 (KHR_materials_specular, file-agnostic)</summary>
		public OpenBveApi.Math.Vector3 SpecularColorFactor;
		/// <summary>Index of refraction, 1.5 when unset (KHR_materials_ior, file-agnostic)</summary>
		public float Ior;
		/// <summary>The PBR specular-factor texture (alpha channel): specularFactor * alpha</summary>
		public Texture SpecularTexture;
		/// <summary>The PBR specular-color texture (sRGB RGB): specularColorFactor * linear(sample)</summary>
		public Texture SpecularColorTexture;

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
			if (A.BlendMode != B.BlendMode) return false;
			if (A.GlowAttenuationData != B.GlowAttenuationData) return false;
			if (A.WrapMode != B.WrapMode) return false;
			if (A.MetallicFactor != B.MetallicFactor) return false;
			if (A.RoughnessFactor != B.RoughnessFactor) return false;
			if (A.NormalTexture != B.NormalTexture) return false;
			if (A.NormalScale != B.NormalScale) return false;
			if (A.OcclusionTexture != B.OcclusionTexture) return false;
			if (A.OcclusionStrength != B.OcclusionStrength) return false;
			if (A.EmissiveTexture != B.EmissiveTexture) return false;
			if (A.MetallicRoughnessTexture != B.MetallicRoughnessTexture) return false;
			if (A.TransmissionFactor != B.TransmissionFactor) return false;
			if (A.SpecularFactor != B.SpecularFactor) return false;
			if (A.SpecularColorFactor != B.SpecularColorFactor) return false;
			if (A.Ior != B.Ior) return false;
			if (A.SpecularTexture != B.SpecularTexture) return false;
			if (A.SpecularColorTexture != B.SpecularColorTexture) return false;
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
			if (A.BlendMode != B.BlendMode) return true;
			if (A.GlowAttenuationData != B.GlowAttenuationData) return true;
			if (A.WrapMode != B.WrapMode) return true;
			if (A.MetallicFactor != B.MetallicFactor) return true;
			if (A.RoughnessFactor != B.RoughnessFactor) return true;
			if (A.NormalTexture != B.NormalTexture) return true;
			if (A.NormalScale != B.NormalScale) return true;
			if (A.OcclusionTexture != B.OcclusionTexture) return true;
			if (A.OcclusionStrength != B.OcclusionStrength) return true;
			if (A.EmissiveTexture != B.EmissiveTexture) return true;
			if (A.MetallicRoughnessTexture != B.MetallicRoughnessTexture) return true;
			if (A.TransmissionFactor != B.TransmissionFactor) return true;
			if (A.SpecularFactor != B.SpecularFactor) return true;
			if (A.SpecularColorFactor != B.SpecularColorFactor) return true;
			if (A.Ior != B.Ior) return true;
			if (A.SpecularTexture != B.SpecularTexture) return true;
			if (A.SpecularColorTexture != B.SpecularColorTexture) return true;
			return false;
		}
	}
}
