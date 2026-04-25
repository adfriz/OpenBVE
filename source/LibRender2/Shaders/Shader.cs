using System;
using System.Runtime.InteropServices;
using LibRender2.Fogs;
using OpenBveApi.Colors;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Textures;
using Raylib_cs;

namespace LibRender2.Shaders
{
	public class Shader : AbstractShader
	{
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
				Raylib.SetShaderValue(Handle, loc, val, ShaderUniformDataType.SHADER_UNIFORM_INT);
			}
		}

		public void SetLightPosition(Vector3 LightPosition)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uLight.position");
			if (loc != -1)
			{
				var vec = new System.Numerics.Vector3((float)LightPosition.X, (float)LightPosition.Y, (float)LightPosition.Z);
				Raylib.SetShaderValue(Handle, loc, vec, ShaderUniformDataType.SHADER_UNIFORM_VEC3);
			}
		}

		public void SetAlphaCutoff(float cutoff)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uAlphaCutoff");
			if (loc != -1)
			{
				Raylib.SetShaderValue(Handle, loc, cutoff, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
			}
		}

		public void SetShadowEnabled(bool enabled)
		{
			int loc = Raylib.GetShaderLocation(Handle, "uShadowEnabled");
			if (loc != -1)
			{
				int val = enabled ? 1 : 0;
				Raylib.SetShaderValue(Handle, loc, val, ShaderUniformDataType.SHADER_UNIFORM_INT);
			}
		}

		// Add other uniform setters as needed...
		// For animation matrices, we'll need to handle the UBO or use a large array uniform
		public void SetCurrentAnimationMatricies(ObjectState objectState)
		{
			// TODO: Implement UBO support or large uniform array for Raylib
		}
	}
}
