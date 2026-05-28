using System;
using System.Collections.Generic;
using LibRenderNext;
using LibRenderNext.Objects;
using LibRenderNext.Pipeline;
using LibRenderNext.Screens;
using LibRenderNext.Shaders;
using LibRenderNext.Viewports;
using OpenBve.Graphics.Renderers;
using OpenBveApi;
using OpenBveApi.Colors;
using OpenBveApi.FileSystem;
using OpenBveApi.Graphics;
using OpenBveApi.Hosts;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Routes;
using OpenBveApi.Runtime;
using OpenBveApi.Textures;
using OpenTK.Graphics.OpenGL;
using Vector2 = OpenBveApi.Math.Vector2;
using Vector3 = OpenBveApi.Math.Vector3;

namespace OpenBve.Graphics
{
	internal class NextRenderer : BaseRenderer, INextRenderer
	{
		private Events events;
		private Overlays overlays;
		internal Touch Touch;

		public override void Initialize()
		{
			base.Initialize();
			events = new Events(this);
			overlays = new Overlays(this);
			Touch = new Touch(this);
			ObjectsSortedByStart = new int[] { };
			ObjectsSortedByEnd = new int[] { };
			
			Program.FileSystem.AppendToLogFile("RendererNext initialised successfully.");
		}

		protected override void UpdateViewport(int width, int height)
		{
			Screen.Width = width;
			Screen.Height = height;
			GL.Viewport(0, 0, Screen.Width, Screen.Height);

			double aspect = Screen.Width / (double)Screen.Height;
			if (aspect <= 0 || double.IsNaN(aspect) || double.IsInfinity(aspect))
			{
				aspect = 1.0;
			}
			Screen.AspectRatio = aspect;

			double fov = Camera.VerticalViewingAngle;
			if (fov <= 0 || fov >= Math.PI || double.IsNaN(fov))
			{
				fov = 45.0 * Math.PI / 180.0;
			}

			Camera.HorizontalViewingAngle = 2.0 * Math.Atan(Math.Tan(0.5 * fov) * Screen.AspectRatio);

			switch (CurrentViewportMode)
			{
				case ViewportMode.Scenery:
					double cd = Program.CurrentRoute.CurrentBackground is BackgroundObject b ? Math.Max(Program.CurrentRoute.CurrentBackground.BackgroundImageDistance, b.ClipDistance) : Program.CurrentRoute.CurrentBackground.BackgroundImageDistance;
					if (cd <= 0 || double.IsNaN(cd) || double.IsInfinity(cd))
					{
						cd = 1000.0;
					}
					double nearClipScenery = Math.Max(0.01, Interface.CurrentOptions.NearClipScenery);
					if (nearClipScenery >= cd)
					{
						cd = nearClipScenery + 1.0;
					}
					CurrentProjectionMatrix = Matrix4D.CreatePerspectiveFieldOfView(fov, Screen.AspectRatio, nearClipScenery, cd);
					break;
				case ViewportMode.Cab:
					double nearClipCab = Math.Max(0.01, Interface.CurrentOptions.NearClipCab);
					double cdCab = 50.0;
					if (nearClipCab >= cdCab)
					{
						cdCab = nearClipCab + 1.0;
					}
					CurrentProjectionMatrix = Matrix4D.CreatePerspectiveFieldOfView(fov, Screen.AspectRatio, nearClipCab, cdCab);
					break;
			}

			Touch.UpdateViewport();
		}

		internal void RenderScene(double TimeElapsed, double RealTimeElapsed)
		{
			ReleaseResources();
			ResetOpenGlState();

			if (OptionWireFrame)
			{
				if (Program.CurrentRoute.CurrentFog.Start < Program.CurrentRoute.CurrentFog.End)
				{
					const float fogDistance = 600.0f;
					float n = (fogDistance - Program.CurrentRoute.CurrentFog.Start) / (Program.CurrentRoute.CurrentFog.End - Program.CurrentRoute.CurrentFog.Start);
					float cr = n * inv255 * Program.CurrentRoute.CurrentFog.Color.R;
					float cg = n * inv255 * Program.CurrentRoute.CurrentFog.Color.G;
					float cb = n * inv255 * Program.CurrentRoute.CurrentFog.Color.B;
					GL.ClearColor(cr, cg, cb, 1.0f);
				}
				else
				{
					GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
				}
			}
			else
			{
				GL.ClearColor(Interface.CurrentOptions.ClearColor.R * inv255, Interface.CurrentOptions.ClearColor.G * inv255, Interface.CurrentOptions.ClearColor.B * inv255, 1.0f);
			}

			GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

			CurrentViewMatrix = Matrix4D.LookAt(Vector3.Zero, new Vector3(Camera.AbsoluteDirection.X, Camera.AbsoluteDirection.Y, -Camera.AbsoluteDirection.Z), new Vector3(Camera.AbsoluteUp.X, Camera.AbsoluteUp.Y, -Camera.AbsoluteUp.Z));
			if (Lighting.ShouldInitialize)
			{
				Lighting.Initialize();
				Lighting.ShouldInitialize = false;
			}
			TransformedLightPosition = new Vector3(Lighting.OptionLightPosition.X, Lighting.OptionLightPosition.Y, -Lighting.OptionLightPosition.Z);
			TransformedLightPosition.Transform(CurrentViewMatrix);

			Lighting.OptionLightingResultingAmount = (Lighting.OptionAmbientColor.R + Lighting.OptionAmbientColor.G + Lighting.OptionAmbientColor.B) / 480.0f;

			if (Lighting.OptionLightingResultingAmount > 1.0f)
			{
				Lighting.OptionLightingResultingAmount = 1.0f;
			}

			double fd = Program.CurrentRoute.NextFog.TrackPosition - Program.CurrentRoute.PreviousFog.TrackPosition;

			if (fd != 0.0)
			{
				float fr = (float)((CameraTrackFollower.TrackPosition - Program.CurrentRoute.PreviousFog.TrackPosition) / fd);
				float frc = 1.0f - fr;
				Program.CurrentRoute.CurrentFog.Start = Program.CurrentRoute.PreviousFog.Start * frc + Program.CurrentRoute.NextFog.Start * fr;
				Program.CurrentRoute.CurrentFog.End = Program.CurrentRoute.PreviousFog.End * frc + Program.CurrentRoute.NextFog.End * fr;
				Program.CurrentRoute.CurrentFog.Color.R = (byte)(Program.CurrentRoute.PreviousFog.Color.R * frc + Program.CurrentRoute.NextFog.Color.R * fr);
				Program.CurrentRoute.CurrentFog.Color.G = (byte)(Program.CurrentRoute.PreviousFog.Color.G * frc + Program.CurrentRoute.NextFog.Color.G * fr);
				Program.CurrentRoute.CurrentFog.Color.B = (byte)(Program.CurrentRoute.PreviousFog.Color.B * frc + Program.CurrentRoute.NextFog.Color.B * fr);
				if (!Program.CurrentRoute.CurrentFog.IsLinear)
				{
					Program.CurrentRoute.CurrentFog.Density = Program.CurrentRoute.PreviousFog.Density * frc + Program.CurrentRoute.NextFog.Density * fr;
				}
			}
			else
			{
				Program.CurrentRoute.CurrentFog = Program.CurrentRoute.PreviousFog;
			}

			var context = new RenderContext(this)
			{
				ViewMatrix = CurrentViewMatrix,
				ProjectionMatrix = CurrentProjectionMatrix
			};

			InitializePipeline(
				(ctx) => { Program.CurrentRoute.UpdateBackground(TimeElapsed, false); },
				(ctx) => {
					// render touch
					OptionLighting = false;
					Touch.RenderScene();

					// render overlays
					ResetOpenGlState();
					UnsetAlphaFunc();
					SetBlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
					GL.Disable(EnableCap.DepthTest);
					overlays.Render(RealTimeElapsed);
					switch (CurrentInterface)
					{
						case InterfaceType.Menu:
						case InterfaceType.GLMainMenu:
							Game.Menu.Draw(TimeElapsed);
							break;
						case InterfaceType.SwitchChangeMap:
							Game.SwitchChangeDialog.Draw();
							break;
					}
					OptionLighting = true;
				}
			);

			Pipeline.Execute(context);
		}

		public NextRenderer(HostInterface currentHost, BaseOptions CurrentOptions, FileSystem fileSystem) : base(currentHost, CurrentOptions, fileSystem)
		{
		}

		#region INextRenderer Implementation
		bool INextRenderer.OptionClock => Program.Renderer.OptionClock;
		dynamic INextRenderer.OptionGradient => Program.Renderer.OptionGradient;
		dynamic INextRenderer.OptionSpeed => Program.Renderer.OptionSpeed;
		dynamic INextRenderer.OptionDistanceToNextStation => Program.Renderer.OptionDistanceToNextStation;
		bool INextRenderer.OptionFrameRates => Program.Renderer.OptionFrameRates;
		bool INextRenderer.OptionBrakeSystems => Program.Renderer.OptionBrakeSystems;
		bool INextRenderer.DebugTouchMode => Program.Renderer.DebugTouchMode;
		bool INextRenderer.ForceLegacyOpenGL => ForceLegacyOpenGL;
		bool INextRenderer.AvailableNewRenderer => AvailableNewRenderer;
		double INextRenderer.FrameRate { get => FrameRate; set => FrameRate = value; }
		dynamic INextRenderer.CurrentOutputMode => CurrentOutputMode;

		int INextRenderer.ScreenWidth => Screen.Width;
		int INextRenderer.ScreenHeight => Screen.Height;

		dynamic INextRenderer.CameraCurrentMode => Camera.CurrentMode;
		double INextRenderer.CameraBackwardViewingDistance => Camera.BackwardViewingDistance;
		double INextRenderer.CameraForwardViewingDistance => Camera.ForwardViewingDistance;
		double INextRenderer.CameraExtraViewingDistance => Camera.ExtraViewingDistance;
		double INextRenderer.CameraTrackFollowerTrackPosition => CameraTrackFollower.TrackPosition;
		Vector3 INextRenderer.CameraAbsolutePosition => Camera.AbsolutePosition;
		Vector3 INextRenderer.CameraAbsoluteDirection => Camera.AbsoluteDirection;
		Vector3 INextRenderer.CameraAbsoluteUp => Camera.AbsoluteUp;
		Vector3 INextRenderer.CameraAbsoluteSide => Camera.AbsoluteSide;
		Matrix4D INextRenderer.CameraTranslationMatrix => Camera.TranslationMatrix;

		Matrix4D INextRenderer.CurrentProjectionMatrix { get => CurrentProjectionMatrix; set => CurrentProjectionMatrix = value; }
		Matrix4D INextRenderer.CurrentViewMatrix { get => CurrentViewMatrix; set => CurrentViewMatrix = value; }

		public void PushMatrix(MatrixMode mode)
		{
			GL.MatrixMode(mode);
			GL.PushMatrix();
		}

		public void PopMatrix(MatrixMode mode)
		{
			GL.MatrixMode(mode);
			GL.PopMatrix();
		}

		public void CreateVAO(Mesh mesh, bool dynamic)
		{
			if (mesh.VAO == null)
			{
				VAOExtensions.CreateVAO(mesh, dynamic, DefaultShader.VertexLayout, this);
			}
		}

		public void RenderFace(ObjectState state, MeshFace face, bool debugTouchMode = false)
		{
			RenderFace(new FaceState(state, face, this), debugTouchMode);
		}

		public void RenderFaceImmediateMode(ObjectState state, MeshFace face, bool debugTouchMode = false)
		{
			RenderFaceImmediateMode(state, face, debugTouchMode);
		}

		public void ActivatePickingShader(int objectIndex)
		{
		}

		public void DeactivatePickingShader()
		{
		}

		public void ActivateDefaultShader()
		{
			DefaultShader.Activate();
			DefaultShader.SetCurrentProjectionMatrix(CurrentProjectionMatrix);
		}

		public void DeactivateDefaultShader()
		{
			DefaultShader.Deactivate();
		}

		public void ResetDefaultShader()
		{
			ResetShader(DefaultShader);
		}

		public bool LoadTexture(ref Texture texture, OpenGlTextureWrapMode wrapMode)
		{
			return TextureManager.LoadTexture(ref texture, wrapMode, CPreciseTimer.GetClockTicks(), Interface.CurrentOptions.Interpolation, Interface.CurrentOptions.AnisotropicFilteringLevel);
		}

		public void RegisterTexture(string file, out Texture texture)
		{
			TextureManager.RegisterTexture(file, out texture);
		}

		public Dictionary<Texture, HashSet<Vector3>> CubesToDraw => CubesToDraw;

		public void DrawCube(Vector3 position, Vector3 direction, Vector3 up, Vector3 side, double size, Vector3 cameraPosition, Texture texture)
		{
			Cube.Draw(position, direction, up, side, size, cameraPosition, texture);
		}

		public void DrawRectangle(Texture texture, Vector2 position, Vector2 size, Color128 color)
		{
			Rectangle.Draw(texture, position, size, color);
		}

		public void DrawString(object font, string text, Vector2 position, TextAlignment alignment, Color128 color, bool shadow = false)
		{
			OpenGlString.Draw((LibRenderNext.Text.OpenGlFont)font, text, position, alignment, color, shadow);
		}

		public object FontSmall => Fonts.SmallFont;
		public object FontNormal => Fonts.NormalFont;
		public object FontVeryLarge => Fonts.VeryLargeFont;

		dynamic INextRenderer.Camera => Camera;
		dynamic INextRenderer.Screen => Screen;
		dynamic INextRenderer.Rectangle => Rectangle;
		dynamic INextRenderer.OpenGlString => OpenGlString;
		dynamic INextRenderer.Fonts => Fonts;
		dynamic INextRenderer.TextureManager => TextureManager;
		#endregion
	}
}
