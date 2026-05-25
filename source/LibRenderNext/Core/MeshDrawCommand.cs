using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Colors;
using LibRenderNext.Shaders;
using OpenTK.Graphics.OpenGL;

namespace LibRenderNext.Core
{
	/// <summary>
	/// Represents a single draw command submitted to the GPU (similar to Unreal's mesh draw commands).
	/// </summary>
	public struct MeshDrawCommand
	{
		public VertexArrayObject VAO;
		public Shader Program;
		public Matrix4D ModelViewMatrix;
		public Matrix4D TextureMatrix;
		
		public int TextureId;
		public int NightTextureId;
		public bool HasTexture;
		public bool HasNightTexture;

		public Color32 MaterialColor;
		public Color24 SpecularColor;
		public Color24 EmissiveColor;
		public MaterialFlags MaterialFlags;
		public float Shininess;
		public float Opacity;
		public float Brightness;
		public float AttenuationFactor;

		public PrimitiveType DrawMode;
		public int IndexStart;
		public int IndexCount;

		public float Depth; // Used for back-to-front sorting (translucent) or front-to-back sorting (opaque)
		public bool IsTranslucent;

		public void Execute()
		{
			if (VAO == null || Program == null)
				return;

			// Bind VAO (RHI caches this binding)
			VAO.Bind();

			// Set matrices
			Program.SetCurrentModelViewMatrix(ModelViewMatrix);
			Program.SetCurrentTextureMatrix(TextureMatrix);

			// Set textures
			if (HasTexture)
			{
				RHI.RHIStateCache.BindTexture(TextureUnit.Texture0, TextureId);
			}
			else
			{
				Program.DisableTexturing();
			}

			// Material settings
			Program.SetMaterialAmbient(MaterialColor);
			Program.SetMaterialDiffuse(MaterialColor);
			Program.SetMaterialSpecular((MaterialFlags & MaterialFlags.Specular) != 0 ? (Color32)SpecularColor : MaterialColor);
			
			if ((MaterialFlags & MaterialFlags.Emissive) != 0)
			{
				Program.SetMaterialEmission((Color32)EmissiveColor);
			}
			else
			{
				Program.SetMaterialEmission(Color32.Black);
			}

			Program.SetMaterialShininess(Shininess);
			Program.SetOpacity(Opacity * MaterialColor.A / 255.0f);
			Program.SetBrightness(Brightness * AttenuationFactor);
			Program.SetMaterialFlags(MaterialFlags);

			// Set cull state depending on Face2Mask
			RHI.RHIStateCache.SetCullState(true, CullFaceMode.Front);

			// Draw
			VAO.Draw(DrawMode, IndexStart, IndexCount);
		}
	}
}
