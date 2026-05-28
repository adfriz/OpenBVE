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

			renderer.ResetOpenGlState();
			RDI.RDIStateCache.SetDepthState(true, DepthFunction.Lequal, true);

			if (renderer.OptionWireFrame)
			{
				GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
			}

			List<FaceState> sortedOpaque;
			lock (renderer.VisibleObjects.LockObject)
			{
				sortedOpaque = new List<FaceState>(renderer.VisibleObjects.OpaqueFaces);
			}

			// Sort by Texture Name first, then by VAO handle to group them together
			sortedOpaque.Sort((a, b) =>
			{
				var aMesh = a.Object.Prototype.Mesh;
				var bMesh = b.Object.Prototype.Mesh;
				
				var aMat = aMesh.Materials[a.Face.Material];
				var bMat = bMesh.Materials[b.Face.Material];

				int aTexId = aMat.DaytimeTexture?.OpenGlTextures[(int)aMat.WrapMode]?.Name ?? 0;
				int bTexId = bMat.DaytimeTexture?.OpenGlTextures[(int)bMat.WrapMode]?.Name ?? 0;

				if (aTexId != bTexId)
				{
					return aTexId.CompareTo(bTexId);
				}

				int aVao = (aMesh.VAO as VertexArrayObject)?.handle ?? 0;
				int bVao = (bMesh.VAO as VertexArrayObject)?.handle ?? 0;

				return aVao.CompareTo(bVao);
			});

			// Render Opaque Faces
			int opaqueCount = sortedOpaque.Count;
			for (int i = 0; i < opaqueCount; i++)
			{
				sortedOpaque[i].Draw();
			}

			if (renderer.OptionWireFrame)
			{
				GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
			}
		}
	}
}
