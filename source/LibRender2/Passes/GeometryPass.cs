using System.Collections.Generic;
using System.Linq;
using LibRender2.Objects;
using LibRender2.Pipeline;
using OpenBveApi.Graphics;
using OpenBveApi.Interface;
using OpenBveApi.Objects;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Passes
{
	/// <summary>
	/// Renders the opaque and alpha-tested world geometry.
	/// </summary>
	public class GeometryPass : IRenderPass
	{
		public void Execute(RenderContext context)
		{
			BaseRenderer renderer = context.Renderer;

			// 1. Setup lighting and fog for world geometry
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
				renderer.DefaultShader.SetCurrentProjectionMatrix(context.ProjectionMatrix);
				renderer.BindCSMToDefaultShader();
			}

			renderer.ResetOpenGlState();

			if (renderer.OptionWireFrame)
			{
				GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
			}

			List<FaceState> opaqueFaces, alphaFaces;
			lock (renderer.Scene.VisibleObjects.LockObject)
			{
				opaqueFaces = renderer.Scene.VisibleObjects.OpaqueFaces.ToList();
				alphaFaces = renderer.Scene.VisibleObjects.GetSortedPolygons();
			}

			// 2. Render Opaque Faces
			foreach (FaceState face in opaqueFaces)
			{
				face.Draw();
			}

			// 3. Render Alpha Faces
			renderer.ResetOpenGlState();

			if (renderer.currentOptions.TransparencyMode == TransparencyMode.Performance)
			{
				renderer.SetBlendFunc();
				renderer.SetAlphaFunc(AlphaFunction.Greater, 0.0f);
				renderer.Device.SetDepthMask(false);

				foreach (FaceState face in alphaFaces)
				{
					face.Draw();
				}
			}
			else
			{
				// Quality Transparency Mode
				renderer.UnsetBlendFunc();
				renderer.SetAlphaFunc(AlphaFunction.Equal, 1.0f);
				renderer.Device.SetDepthMask(true);

				foreach (FaceState face in alphaFaces)
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
				renderer.Device.SetDepthMask(false);
				bool additive = false;

				foreach (FaceState face in alphaFaces)
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
			
			// Restore default depth mask
			renderer.Device.SetDepthMask(true);

			if (renderer.OptionWireFrame)
			{
				GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
			}
		}
	}
}
