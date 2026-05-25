using System;
using System.Collections.Generic;
using System.Linq;
using LibRenderNext.Pipeline;
using LibRenderNext.Objects;
using LibRenderNext.Viewports;
using OpenBveApi.Colors;
using OpenBveApi.Graphics;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenTK.Graphics.OpenGL;

namespace LibRenderNext.Pipeline.Passes
{
	public class OverlayPass : IRenderPass
	{
		private readonly Action<RenderContext> renderUiAction;

		public OverlayPass(Action<RenderContext> renderUiAction)
		{
			this.renderUiAction = renderUiAction;
		}

		public void Execute(RenderContext context)
		{
			var renderer = context.Renderer;

			// Cab/Interior Layer Setup
			renderer.Fog.Enabled = false;
			renderer.UpdateViewport(ViewportChangeMode.ChangeToCab);

			if (renderer.AvailableNewRenderer)
			{
				renderer.DefaultShader.Activate();
				renderer.ResetShader(renderer.DefaultShader);
				renderer.DefaultShader.SetCurrentProjectionMatrix(renderer.CurrentProjectionMatrix);
			}

			renderer.CurrentViewMatrix = Matrix4D.LookAt(Vector3.Zero, new Vector3(renderer.Camera.AbsoluteDirection.X, renderer.Camera.AbsoluteDirection.Y, -renderer.Camera.AbsoluteDirection.Z), new Vector3(renderer.Camera.AbsoluteUp.X, renderer.Camera.AbsoluteUp.Y, -renderer.Camera.AbsoluteUp.Z));
			context.ViewMatrix = renderer.CurrentViewMatrix;

			if (renderer.AvailableNewRenderer)
			{
				renderer.DefaultShader.SetCurrentViewMatrix(renderer.CurrentViewMatrix);
			}

			List<FaceState> overlayOpaqueFaces, overlayAlphaFaces;
			lock (renderer.VisibleObjects.LockObject)
			{
				overlayOpaqueFaces = renderer.VisibleObjects.OverlayOpaqueFaces.ToList();
				overlayAlphaFaces = renderer.VisibleObjects.GetSortedPolygons(true);
			}

			if (renderer.Camera.CurrentRestriction == CameraRestrictionMode.NotAvailable || renderer.Camera.CurrentRestriction == CameraRestrictionMode.Restricted3D)
			{
				// 3D Cab
				renderer.ResetOpenGlState();
				GL.Clear(ClearBufferMask.DepthBufferBit);
				renderer.OptionLighting = true;

				Color24 prevOptionAmbientColor = renderer.Lighting.OptionAmbientColor;
				Color24 prevOptionDiffuseColor = renderer.Lighting.OptionDiffuseColor;
				renderer.Lighting.OptionAmbientColor = Color24.LightGrey;
				renderer.Lighting.OptionDiffuseColor = Color24.LightGrey;

				if (renderer.AvailableNewRenderer)
				{
					renderer.DefaultShader.SetIsLight(true);
					Vector3 lightPos = new Vector3(renderer.Lighting.OptionLightPosition.X, renderer.Lighting.OptionLightPosition.Y, -renderer.Lighting.OptionLightPosition.Z);
					renderer.DefaultShader.SetLightPosition(lightPos);
					renderer.DefaultShader.SetLightAmbient(renderer.Lighting.OptionAmbientColor);
					renderer.DefaultShader.SetLightDiffuse(renderer.Lighting.OptionDiffuseColor);
					renderer.DefaultShader.SetLightSpecular(renderer.Lighting.OptionSpecularColor);
					renderer.DefaultShader.SetLightModel(renderer.Lighting.LightModel);
				}

				foreach (var face in overlayOpaqueFaces)
				{
					face.Draw();
				}

				renderer.ResetOpenGlState();
				if (renderer.currentOptions.TransparencyMode == TransparencyMode.Performance)
				{
					renderer.SetBlendFunc();
					renderer.SetAlphaFunc(AlphaFunction.Greater, 0.0f);
					RHI.RHIStateCache.SetDepthState(true, DepthFunction.Lequal, false);

					foreach (var face in overlayAlphaFaces)
					{
						face.Draw();
					}
				}
				else
				{
					renderer.UnsetBlendFunc();
					renderer.SetAlphaFunc(AlphaFunction.Equal, 1.0f);
					RHI.RHIStateCache.SetDepthState(true, DepthFunction.Lequal, true);

					foreach (var face in overlayAlphaFaces)
					{
						var material = face.Object.Prototype.Mesh.Materials[face.Face.Material];
						if (material.BlendMode == MeshMaterialBlendMode.Normal && material.GlowAttenuationData == 0)
						{
							if (material.Color.A == 255)
							{
								face.Draw();
							}
						}
					}

					renderer.SetBlendFunc();
					renderer.SetAlphaFunc(AlphaFunction.Less, 1.0f);
					RHI.RHIStateCache.SetDepthState(true, DepthFunction.Lequal, false);
					bool additive = false;

					foreach (var face in overlayAlphaFaces)
					{
						var material = face.Object.Prototype.Mesh.Materials[face.Face.Material];
						if (material.BlendMode == MeshMaterialBlendMode.Additive)
						{
							if (!additive)
							{
								renderer.UnsetAlphaFunc();
								additive = true;
							}
						}
						else
						{
							if (additive)
							{
								renderer.SetAlphaFunc();
								additive = false;
							}
						}
						face.Draw();
					}
				}

				renderer.Lighting.OptionAmbientColor = prevOptionAmbientColor;
				renderer.Lighting.OptionDiffuseColor = prevOptionDiffuseColor;
				renderer.Lighting.Initialize();
			}
			else
			{
				// 2D Cab
				renderer.ResetOpenGlState();
				renderer.OptionLighting = false;
				if (renderer.AvailableNewRenderer)
				{
					renderer.DefaultShader.SetIsLight(false);
				}

				renderer.SetBlendFunc();
				renderer.UnsetAlphaFunc();
				RHI.RHIStateCache.SetDepthState(true, DepthFunction.Lequal, true);

				foreach (var face in overlayOpaqueFaces)
				{
					face.Draw();
				}

				RHI.RHIStateCache.SetDepthState(false, DepthFunction.Lequal, false);

				foreach (var face in overlayAlphaFaces)
				{
					face.Draw();
				}
			}

			// Render UI
			renderer.OptionLighting = false;
			renderer.ResetOpenGlState();
			renderer.SetBlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
			renderer.SetAlphaFunc(AlphaFunction.Greater, 0.0f);
			RHI.RHIStateCache.SetDepthState(false, DepthFunction.Lequal, false);

			if (renderer.AvailableNewRenderer)
			{
				renderer.CurrentViewMatrix = Matrix4D.Identity;
				renderer.DefaultShader.SetCurrentViewMatrix(renderer.CurrentViewMatrix);
			}

			renderUiAction?.Invoke(context);
		}
	}
}
