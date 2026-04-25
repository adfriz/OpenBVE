using System;
using System.Runtime.InteropServices;
using LibRender2.Fogs;
using OpenBveApi.Colors;
using Color24 = OpenBveApi.Colors.Color24;
using Color32 = OpenBveApi.Colors.Color32;

using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Textures;
using Raylib_cs;
using MaterialFlags = OpenBveApi.Objects.MaterialFlags;
using AlphaFunction = OpenTK.Graphics.OpenGL.AlphaFunction;



namespace LibRender2.Shaders
{
	public class Shader : AbstractShader
	{
		public VertexLayout VertexLayout { get; set; }

		public Shader(BaseRenderer Renderer, string vertexShaderName, string fragmentShaderName, bool isFromStream = false) 
			: base(Renderer, vertexShaderName, fragmentShaderName, isFromStream, true)
		{
			// Initialize uniform locations using Raylib
			// Raylib handles most of this automatically if we use SetShaderValue
		}

		private System.Numerics.Matrix4x4 ConvertToNumerics(Matrix4D mat)
		{
			return new System.Numerics.Matrix4x4(
				(float)mat.Row0.X, (float)mat.Row0.Y, (float)mat.Row0.Z, (float)mat.Row0.W,
				(float)mat.Row1.X, (float)mat.Row1.Y, (float)mat.Row1.Z, (float)mat.Row1.W,
				(float)mat.Row2.X, (float)mat.Row2.Y, (float)mat.Row2.Z, (float)mat.Row2.W,
				(float)mat.Row3.X, (float)mat.Row3.Y, (float)mat.Row3.Z, (float)mat.Row3.W
			);
		}

		public void SetCurrentProjectionMatrix(Matrix4D ProjectionMatrix)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uCurrentProjectionMatrix");
			if (loc != -1)
			{
				var mat = ConvertToNumerics(ProjectionMatrix);
				Raylib.SetShaderValueMatrix(Handle, loc, mat);
			}
		}

		public void SetCurrentModelViewMatrix(Matrix4D ModelViewMatrix)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uCurrentModelViewMatrix");
			if (loc != -1)
			{
				var mat = ConvertToNumerics(ModelViewMatrix);
				Raylib.SetShaderValueMatrix(Handle, loc, mat);
			}
		}

		public void SetIsLight(bool IsLight)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uIsLight");
			if (loc != -1)
			{
				int val = IsLight ? 1 : 0;
				Raylib.SetShaderValue(Handle, loc, val, ShaderUniformDataType.Int);
			}
		}

		public void SetLightPosition(Vector3 LightPosition)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uLight.position");
			if (loc != -1)
			{
				var vec = new System.Numerics.Vector3((float)LightPosition.X, (float)LightPosition.Y, (float)LightPosition.Z);
				Raylib.SetShaderValue(Handle, loc, vec, ShaderUniformDataType.Vec3);
			}
		}

		public void SetAlphaCutoff(float cutoff)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uAlphaCutoff");
			if (loc != -1)
			{
				Raylib.SetShaderValue(Handle, loc, cutoff, ShaderUniformDataType.Float);
			}
		}

		public void SetShadowEnabled(bool enabled)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uShadowEnabled");
			if (loc != -1)
			{
				int val = enabled ? 1 : 0;
				Raylib.SetShaderValue(Handle, loc, val, ShaderUniformDataType.Int);
			}
		}

		public void SetOpacity(float opacity)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uOpacity");
			if (loc != -1)
			{
				Raylib.SetShaderValue(Handle, loc, opacity, ShaderUniformDataType.Float);
			}
		}

		public void SetBrightness(float brightness)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uBrightness");
			if (loc != -1)
			{
				Raylib.SetShaderValue(Handle, loc, brightness, ShaderUniformDataType.Float);
			}
		}

		public void SetTexture(int slot)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uTexture");
			if (loc != -1)
			{
				Raylib.SetShaderValue(Handle, loc, slot, ShaderUniformDataType.Int);
			}
		}

		public void SetLightAmbient(Color32 color)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uLight.ambient");
			if (loc != -1)
			{
				var vec = new System.Numerics.Vector3(color.R * inv255, color.G * inv255, color.B * inv255);
				Raylib.SetShaderValue(Handle, loc, vec, ShaderUniformDataType.Vec3);
			}
		}

		public void SetLightDiffuse(Color32 color)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uLight.diffuse");
			if (loc != -1)
			{
				var vec = new System.Numerics.Vector3(color.R * inv255, color.G * inv255, color.B * inv255);
				Raylib.SetShaderValue(Handle, loc, vec, ShaderUniformDataType.Vec3);
			}
		}

		public void SetLightSpecular(Color32 color)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uLight.specular");
			if (loc != -1)
			{
				var vec = new System.Numerics.Vector3(color.R * inv255, color.G * inv255, color.B * inv255);
				Raylib.SetShaderValue(Handle, loc, vec, ShaderUniformDataType.Vec3);
			}
		}


		public void SetLightModel(Vector4 model)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uLightModel");
			if (loc != -1)
			{
				var vec = new System.Numerics.Vector4((float)model.X, (float)model.Y, (float)model.Z, (float)model.W);
				Raylib.SetShaderValue(Handle, loc, vec, ShaderUniformDataType.Vec4);
			}
		}
		
		public void DisableTexturing()
		{
			int loc = Raylib.GetShaderLocation(Handle, "uUseTexture");
			if (loc != -1)
			{
				Raylib.SetShaderValue(Handle, loc, 0, ShaderUniformDataType.Int);
			}
		}

		public override void SetAlphaFunction(AlphaFunction alphaFunction, float alphaComparison)
		{
			int locFunc = Raylib.GetShaderLocation(Handle, "uAlphaFunction");
			int locComp = Raylib.GetShaderLocation(Handle, "uAlphaComparison");
			if (locFunc != -1) Raylib.SetShaderValue(Handle, locFunc, (int)alphaFunction, ShaderUniformDataType.Int);
			if (locComp != -1) Raylib.SetShaderValue(Handle, locComp, alphaComparison, ShaderUniformDataType.Float);
		}

		public override void SetAlphaTest(bool enabled)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uAlphaTestEnabled");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, enabled ? 1 : 0, ShaderUniformDataType.Int);
		}

		public void SetCurrentTextureMatrix(Matrix4D TextureMatrix)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uCurrentTextureMatrix");
			if (loc != -1) Raylib.SetShaderValueMatrix(Handle, loc, ConvertToNumerics(TextureMatrix));
		}

		public override void SetFog(bool enabled)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uFogEnabled");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, enabled ? 1 : 0, ShaderUniformDataType.Int);
		}

		public void SetMaterialAmbient(Color32 color)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uMaterial.ambient");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, new System.Numerics.Vector3(color.R * inv255, color.G * inv255, color.B * inv255), ShaderUniformDataType.Vec3);
		}

		public void SetMaterialDiffuse(Color32 color)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uMaterial.diffuse");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, new System.Numerics.Vector3(color.R * inv255, color.G * inv255, color.B * inv255), ShaderUniformDataType.Vec3);
		}

		public void SetMaterialEmission(Color32 color)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uMaterial.emission");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, new System.Numerics.Vector3(color.R * inv255, color.G * inv255, color.B * inv255), ShaderUniformDataType.Vec3);
		}

		public void SetMaterialFlags(MaterialFlags flags)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uMaterialFlags");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, (int)flags, ShaderUniformDataType.Int);
		}

		public void SetMaterialShininess(float shininess)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uMaterial.shininess");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, shininess, ShaderUniformDataType.Float);
		}

		public void SetMaterialSpecular(Color32 color)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uMaterial.specular");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, new System.Numerics.Vector3(color.R * inv255, color.G * inv255, color.B * inv255), ShaderUniformDataType.Vec3);
		}


		public void SetObjectIndex(int index)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uObjectIndex");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, index, ShaderUniformDataType.Int);
		}

		public void SetCascadeBias(int index, float bias)
		{
			int loc = Raylib.GetShaderLocation(Handle, $"uCascadeBias[{index}]");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, bias, ShaderUniformDataType.Float);
		}

		public void SetCascadeCount(int count)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uCascadeCount");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, count, ShaderUniformDataType.Int);
		}

		public void SetCascadeFarDistance(int index, float distance)
		{
			int loc = Raylib.GetShaderLocation(Handle, $"uCascadeFarDistance[{index}]");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, distance, ShaderUniformDataType.Float);
		}

		public void SetCascadeLightSpaceMatrix(int index, Matrix4D matrix)
		{
			int loc = Raylib.GetShaderLocation(Handle, $"uCascadeLightSpaceMatrix[{index}]");
			if (loc != -1) Raylib.SetShaderValueMatrix(Handle, loc, ConvertToNumerics(matrix));
		}

		public void SetCascadeShadowMapUnit(int index, int unit)
		{
			int loc = Raylib.GetShaderLocation(Handle, $"uCascadeShadowMap[{index}]");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, unit, ShaderUniformDataType.Int);
		}

		public void SetNormalBias(int index, float bias)
		{
			int loc = Raylib.GetShaderLocation(Handle, $"uNormalBias[{index}]");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, bias, ShaderUniformDataType.Float);
		}


		public void SetShadowStrength(float strength)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uShadowStrength");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, strength, ShaderUniformDataType.Float);
		}

		public void SetCurrentViewMatrix(Matrix4D ViewMatrix)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uCurrentViewMatrix");
			if (loc != -1) Raylib.SetShaderValueMatrix(Handle, loc, ConvertToNumerics(ViewMatrix));
		}

		public void SetCurrentAnimationMatricies(ObjectState objectState)
		{
			// TODO: Implement UBO support or large uniform array for Raylib
		}

		public void SetSize(Vector2 size)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uSize");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, new System.Numerics.Vector2((float)size.X, (float)size.Y), ShaderUniformDataType.Vec2);
		}

		public void SetCoordinates(Vector2 coordinates)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uCoordinates");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, new System.Numerics.Vector2((float)coordinates.X, (float)coordinates.Y), ShaderUniformDataType.Vec2);
		}

		public void SetPoint(Vector2 point)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uPoint");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, new System.Numerics.Vector2((float)point.X, (float)point.Y), ShaderUniformDataType.Vec2);
		}

		public void SetColor(Color128 color)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uColor");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, new System.Numerics.Vector4(color.R, color.G, color.B, color.A), ShaderUniformDataType.Vec4);
		}

		public void SetAtlasLocation(Vector4 atlasLocation)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uAtlasLocation");
			if (loc != -1) Raylib.SetShaderValue(Handle, loc, new System.Numerics.Vector4((float)atlasLocation.X, (float)atlasLocation.Y, (float)atlasLocation.Z, (float)atlasLocation.W), ShaderUniformDataType.Vec4);
		}


		private const float inv255 = 1.0f / 255.0f;
	}
}

