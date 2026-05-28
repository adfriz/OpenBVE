using System.Collections.Generic;
using OpenBveApi.Colors;
using OpenBveApi.Graphics;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Textures;
using OpenTK.Graphics.OpenGL;

namespace OpenBve.Graphics
{
	internal interface INextRenderer
	{
		bool OptionClock { get; }
		dynamic OptionGradient { get; }
		dynamic OptionSpeed { get; }
		dynamic OptionDistanceToNextStation { get; }
		bool OptionFrameRates { get; }
		bool OptionBrakeSystems { get; }
		bool DebugTouchMode { get; }
		bool ForceLegacyOpenGL { get; }
		bool AvailableNewRenderer { get; }
		double FrameRate { get; set; }
		dynamic CurrentOutputMode { get; }

		int ScreenWidth { get; }
		int ScreenHeight { get; }

		dynamic Camera { get; }
		dynamic Screen { get; }
		dynamic Rectangle { get; }
		dynamic OpenGlString { get; }
		dynamic Fonts { get; }
		dynamic TextureManager { get; }

		dynamic CameraCurrentMode { get; }
		double CameraBackwardViewingDistance { get; }
		double CameraForwardViewingDistance { get; }
		double CameraExtraViewingDistance { get; }
		double CameraTrackFollowerTrackPosition { get; }
		Vector3 CameraAbsolutePosition { get; }
		Vector3 CameraAbsoluteDirection { get; }
		Vector3 CameraAbsoluteUp { get; }
		Vector3 CameraAbsoluteSide { get; }
		Matrix4D CameraTranslationMatrix { get; }

		Matrix4D CurrentProjectionMatrix { get; set; }
		Matrix4D CurrentViewMatrix { get; set; }
		void PushMatrix(MatrixMode mode);
		void PopMatrix(MatrixMode mode);

		void ResetOpenGlState();
		void SetBlendFunc();
		void SetBlendFunc(BlendingFactor srcFactor, BlendingFactor destFactor);
		void UnsetBlendFunc();
		void SetAlphaFunc(AlphaFunction comparison, float value);
		void UnsetAlphaFunc();

		void CreateVAO(Mesh mesh, bool dynamic);
		void RenderFace(ObjectState state, MeshFace face, bool debugTouchMode = false);
		void RenderFaceImmediateMode(ObjectState state, MeshFace face, bool debugTouchMode = false);

		// Picking Shader (Touch)
		void ActivatePickingShader(int objectIndex);
		void DeactivatePickingShader();

		// Default Shader (Touch)
		void ActivateDefaultShader();
		void DeactivateDefaultShader();
		void ResetDefaultShader();

		// Textures & Primitives
		bool LoadTexture(ref Texture texture, OpenGlTextureWrapMode wrapMode);
		void RegisterTexture(string file, out Texture texture);
		Dictionary<Texture, HashSet<Vector3>> CubesToDraw { get; }
		void DrawCube(Vector3 position, Vector3 direction, Vector3 up, Vector3 side, double size, Vector3 cameraPosition, Texture texture);
		void DrawRectangle(Texture texture, Vector2 position, Vector2 size, Color128 color);
		void DrawString(object font, string text, Vector2 position, TextAlignment alignment, Color128 color, bool shadow = false);
		
		// Fonts
		object FontSmall { get; }
		object FontNormal { get; }
		object FontVeryLarge { get; }
	}
}
