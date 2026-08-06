using LibRender2.Shaders;
using LibRender2.Textures;
using OpenBveApi.Objects;
using OpenBveApi.Textures;

namespace LibRender2.Objects
{
	/// <summary>The classification of a face with regards to transparency handling</summary>
	public enum FaceAlphaClass
	{
		/// <summary>The face has no transparency at all</summary>
		Opaque = 0,
		/// <summary>The texture is color-key: alpha bytes are only 0 or 255</summary>
		Partial = 1,
		/// <summary>The texture has in-between alpha values, requiring true blending</summary>
		SmoothAlpha = 2,
		/// <summary>The material color has an alpha component less than 255</summary>
		MaterialAlpha = 3,
		/// <summary>The material uses additive blending</summary>
		Additive = 4,
		/// <summary>The material has a non-zero glow attenuation</summary>
		Glow = 5
	}

	/// <summary>Represents a face state within the renderer</summary>
	public class FaceState
	{
		/// <summary>The containing object</summary>
		public readonly ObjectState Object;
		/// <summary>The face to draw</summary>
		public readonly MeshFace Face;
		/// <summary>Holds the reference to the base renderer</summary>
		public readonly BaseRenderer Renderer;
		/// <summary>The cached transparency classification of this face, computed once at construction</summary>
		public readonly FaceAlphaClass AlphaClass;

		public FaceState(ObjectState _object, MeshFace face, BaseRenderer renderer)
		{
			Object = _object;
			Face = face;
			Renderer = renderer;
			if (Object.Prototype.Mesh.VAO == null)
			{
				VAOExtensions.CreateVAO(Object.Prototype.Mesh, Object.Prototype.Dynamic, Renderer.DefaultShader.VertexLayout, Renderer);
			}

			MeshMaterial material = Object.Prototype.Mesh.Materials[face.Material];
			if (material.BlendMode == MeshMaterialBlendMode.Additive)
			{
				AlphaClass = FaceAlphaClass.Additive;
			}
			else if (material.GlowAttenuationData != 0)
			{
				AlphaClass = FaceAlphaClass.Glow;
			}
			else if (material.Color.A != 255)
			{
				AlphaClass = FaceAlphaClass.MaterialAlpha;
			}
			else
			{
				// Texture-based classification. Do not call GetTransparencyType() twice for the same texture.
				TextureTransparencyType daytime = GetTransparencyType(material.DaytimeTexture);
				TextureTransparencyType nighttime = material.NighttimeTexture != null && material.NighttimeTexture != material.DaytimeTexture ? GetTransparencyType(material.NighttimeTexture) : daytime;
				if (daytime == TextureTransparencyType.Alpha || nighttime == TextureTransparencyType.Alpha)
				{
					AlphaClass = FaceAlphaClass.SmoothAlpha;
				}
				else if (daytime == TextureTransparencyType.Partial || nighttime == TextureTransparencyType.Partial)
				{
					AlphaClass = FaceAlphaClass.Partial;
				}
				else
				{
					AlphaClass = FaceAlphaClass.Opaque;
				}
			}
		}

		/// <summary>Gets the transparency type of the specified texture, loading it via the texture cache if required</summary>
		/// <param name="texture">The texture, or null</param>
		private static TextureTransparencyType GetTransparencyType(Texture texture)
		{
			if (texture == null)
			{
				// No texture: treat as opaque
				return TextureTransparencyType.Opaque;
			}
			Texture loadedTexture;
			if (TextureManager.textureCache.ContainsKey(texture.Origin))
			{
				loadedTexture = TextureManager.textureCache[texture.Origin];
			}
			else
			{
				texture.Origin.GetTexture(out loadedTexture);
				if (!TextureManager.textureCache.ContainsKey(texture.Origin)) // because getting the Origin may change the ref
				{
					TextureManager.textureCache.Add(texture.Origin, loadedTexture);
				}
			}
			if (loadedTexture == null)
			{
				// Failed to load the texture, e.g. corrupt file etc: treat as opaque
				return TextureTransparencyType.Opaque;
			}
			return loadedTexture.GetTransparencyType();
		}

		public void Draw()
		{
			Renderer.RenderFace(this);
		}

		/// <summary>Draws the face using the specified shader, rather than the renderer's current shader</summary>
		/// <param name="shader">The shader to use for this face</param>
		/// <remarks>Used by multi-shader pipelines such as the hybrid OIT renderer (LibRender2.OIT.OitRenderer), where the tail pass must render the same faces with the weighted-blended variant of the default program.</remarks>
		public void Draw(Shader shader)
		{
			Renderer.RenderFace(shader, Object, Face);
		}
	}
}
