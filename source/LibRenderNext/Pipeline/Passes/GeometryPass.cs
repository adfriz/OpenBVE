using System.Collections.Generic;
using LibRenderNext.Pipeline;
using LibRenderNext.Objects;
using OpenBveApi.Graphics;
using OpenBveApi.Interface;
using OpenBveApi.Objects;
using OpenTK.Graphics.OpenGL;

namespace LibRenderNext.Pipeline.Passes
{
	public class GeometryPass : IRenderPass
	{
		public void Execute(RenderContext context)
		{
			var renderer = context.Renderer;

			// Setup lighting and fog
			if (renderer.AvailableNewRenderer)
			{
				if (renderer.OptionLighting)
				{
					renderer.DefaultShader.SetIsLight(true);
					renderer.DefaultShader.SetLightPosition(renderer.TransformedLightPosition);
					renderer.DefaultShader.SetLightAmbient(renderer.Lighting.OptionAmbientColor);
					renderer.DefaultShader.SetLightDiffuse(renderer.Lighting.OptionDiffuseColor);
					renderer.DefaultShader.SetLightSpecular(renderer.Lighting.OptionSpecularColor);
					renderer.DefaultShader.SetLightModel(renderer.Lighting.LightModel);
				}
				renderer.Fog.Set();
				renderer.DefaultShader.SetTexture(0);
				renderer.CurrentProjectionMatrix = context.ProjectionMatrix;
				renderer.CurrentViewMatrix = context.ViewMatrix;
				renderer.DefaultShader.SetCurrentProjectionMatrix(renderer.CurrentProjectionMatrix);
				renderer.DefaultShader.SetCurrentViewMatrix(renderer.CurrentViewMatrix);
				if (renderer.ShadowsEnabled && renderer.Shadows != null)
				{
					renderer.Shadows.Bind(renderer.DefaultShader);
				}
			}

			renderer.ResetOpenGlState();
			RHI.RHIStateCache.SetDepthState(true, DepthFunction.Lequal, true);

			if (renderer.OptionWireFrame)
			{
				GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
			}

			List<FaceState> alphaFaces;
			lock (renderer.VisibleObjects.LockObject)
			{
				// Render Opaque Faces
				int opaqueCount = renderer.VisibleObjects.OpaqueFaces.Count;
				for (int i = 0; i < opaqueCount; i++)
				{
					renderer.VisibleObjects.OpaqueFaces[i].Draw();
				}
				alphaFaces = renderer.VisibleObjects.GetSortedPolygons(false);
			}

			// Render Alpha Faces
			renderer.ResetOpenGlState();
			RHI.RHIStateCache.SetDepthState(true, DepthFunction.Lequal, true);

			if (renderer.currentOptions.TransparencyMode == TransparencyMode.Performance)
			{
				renderer.SetBlendFunc();
				renderer.SetAlphaFunc(AlphaFunction.Greater, 0.0f);
				RHI.RHIStateCache.SetDepthState(true, DepthFunction.Lequal, false);

				foreach (var face in alphaFaces)
				{
					face.Draw();
				}
			}
			else
			{
				// Quality Transparency
				renderer.UnsetBlendFunc();
				renderer.SetAlphaFunc(AlphaFunction.Equal, 1.0f);
				RHI.RHIStateCache.SetDepthState(true, DepthFunction.Lequal, true);

				foreach (var face in alphaFaces)
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

				foreach (var face in alphaFaces)
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

			RHI.RHIStateCache.SetDepthState(true, DepthFunction.Lequal, true);

			if (renderer.OptionWireFrame)
			{
				GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
			}
		}
	}
}
