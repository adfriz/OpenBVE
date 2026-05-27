using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Shaders
{
	public class MotionBlurShader : AbstractShader
	{
		private readonly int uColorTexture;
		private readonly int uDepthTexture;
		private readonly int uCurrentViewProjectionInverse;
		private readonly int uPreviousViewProjection;
		private readonly int uCameraOffset;
		private readonly int uNumSamples;
		private readonly int uStrength;

		public MotionBlurShader(BaseRenderer Renderer, string vertexShaderName, string fragmentShaderName, bool isFromStream = false) 
			: base(Renderer, vertexShaderName, fragmentShaderName, isFromStream, true)
		{
			uColorTexture = GL.GetUniformLocation(Handle, "uColorTexture");
			uDepthTexture = GL.GetUniformLocation(Handle, "uDepthTexture");
			uCurrentViewProjectionInverse = GL.GetUniformLocation(Handle, "uCurrentViewProjectionInverse");
			uPreviousViewProjection = GL.GetUniformLocation(Handle, "uPreviousViewProjection");
			uCameraOffset = GL.GetUniformLocation(Handle, "uCameraOffset");
			uNumSamples = GL.GetUniformLocation(Handle, "uNumSamples");
			uStrength = GL.GetUniformLocation(Handle, "uStrength");
		}

		public void SetColorTexture(int unit)
		{
			GL.Uniform1(uColorTexture, unit);
		}

		public void SetDepthTexture(int unit)
		{
			GL.Uniform1(uDepthTexture, unit);
		}

		public void SetCurrentViewProjectionInverse(OpenBveApi.Math.Matrix4D m)
		{
			Matrix4 matrix = ConvertToMatrix4(m);
			GL.UniformMatrix4(uCurrentViewProjectionInverse, false, ref matrix);
		}

		public void SetPreviousViewProjection(OpenBveApi.Math.Matrix4D m)
		{
			Matrix4 matrix = ConvertToMatrix4(m);
			GL.UniformMatrix4(uPreviousViewProjection, false, ref matrix);
		}

		public void SetCameraOffset(OpenBveApi.Math.Vector3 offset)
		{
			GL.Uniform3(uCameraOffset, (float)offset.X, (float)offset.Y, (float)offset.Z);
		}

		public void SetNumSamples(int samples)
		{
			GL.Uniform1(uNumSamples, samples);
		}

		public void SetStrength(float strength)
		{
			GL.Uniform1(uStrength, strength);
		}

		private static Matrix4 ConvertToMatrix4(OpenBveApi.Math.Matrix4D mat)
		{
			return new Matrix4(
				(float)mat.Row0.X, (float)mat.Row0.Y, (float)mat.Row0.Z, (float)mat.Row0.W,
				(float)mat.Row1.X, (float)mat.Row1.Y, (float)mat.Row1.Z, (float)mat.Row1.W,
				(float)mat.Row2.X, (float)mat.Row2.Y, (float)mat.Row2.Z, (float)mat.Row2.W,
				(float)mat.Row3.X, (float)mat.Row3.Y, (float)mat.Row3.Z, (float)mat.Row3.W
			);
		}
	}
}
