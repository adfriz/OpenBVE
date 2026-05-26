using System.Collections.Generic;
using LibRenderNext.Pipeline;
using LibRenderNext.Objects;
using OpenBveApi.Graphics;
using OpenBveApi.Objects;
using OpenTK.Graphics.OpenGL;

namespace LibRenderNext.Pipeline.Passes
{
	public class TransparentPass : IRenderPass
	{
		public void Execute(RenderContext context)
		{
			var renderer = context.Renderer;

			List<FaceState> alphaFacesToSort;
			lock (renderer.VisibleObjects.LockObject)
			{
				alphaFacesToSort = new List<FaceState>(renderer.VisibleObjects.AlphaFaces);
			}
			List<FaceState> alphaFaces = renderer.VisibleObjects.GetSortedPolygons(alphaFacesToSort);

			renderer.ResetOpenGlState();
			RDI.RDIStateCache.SetDepthState(true, DepthFunction.Lequal, true);

			if (renderer.currentOptions.TransparencyMode == TransparencyMode.Performance)
			{
				renderer.SetBlendFunc();
				renderer.SetAlphaFunc(AlphaFunction.Greater, 0.0f);
				RDI.RDIStateCache.SetDepthState(true, DepthFunction.Lequal, false);

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
				RDI.RDIStateCache.SetDepthState(true, DepthFunction.Lequal, true);

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
				RDI.RDIStateCache.SetDepthState(true, DepthFunction.Lequal, false);
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

			RDI.RDIStateCache.SetDepthState(true, DepthFunction.Lequal, true);
		}
	}
}
