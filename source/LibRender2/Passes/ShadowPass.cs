using System;
using System.Collections.Generic;
using LibRender2.Objects;
using LibRender2.Pipeline;
using LibRender2.Shaders;
using OpenBveApi.Graphics;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Textures;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Passes
{
	/// <summary>
	/// Performs the Cascaded Shadow Map (CSM) shadow depth rendering pass.
	/// </summary>
	public class ShadowPass : IRenderPass
	{
		public void Execute(RenderContext context)
		{
			BaseRenderer renderer = context.Renderer;

			if (!renderer.ShadowsEnabled || renderer.CSMShadowMaps == null || renderer.CSMCaster == null || renderer.ShadowDepthShaderProgram == null)
				return;

			// 1. Get light direction pointing FROM the sun TOWARD the scene
			Vector3 lightDir = new Vector3(
				-renderer.Lighting.OptionLightPosition.X,
				-renderer.Lighting.OptionLightPosition.Y,
				renderer.Lighting.OptionLightPosition.Z
			);

			if (lightDir.IsNullVector())
			{
				return;
			}

			// 2. Update cascade matrices
			renderer.CSMCaster.Resolution = renderer.CSMShadowMaps.Resolution;
			if (renderer.currentOptions.ShadowDrawDistance == ShadowDistance.ViewingDistance)
			{
				renderer.CSMCaster.ShadowDistance = renderer.currentOptions.ViewingDistance;
			}
			else
			{
				renderer.CSMCaster.ShadowDistance = (double)renderer.currentOptions.ShadowDrawDistance;
			}
			renderer.CSMCaster.Update(lightDir, context.ViewMatrix, context.ProjectionMatrix, 0.1, context.Camera.VerticalViewingAngle, renderer.Screen.AspectRatio);

			// 3. Setup rendering state
			renderer.CurrentShader?.Deactivate();
			renderer.ShadowDepthShaderProgram.Activate();
			
			renderer.SetDepthTest(true, DepthFunction.Less);
			renderer.SetCullFace(false);
			renderer.SetDepthMask(true);
			
			renderer.ShadowDepthShaderProgram.SetTexture(0); // always use texture unit 0

			for (int cascade = 0; cascade < renderer.CSMCaster.CascadeCount; cascade++)
			{
				renderer.CSMShadowMaps.BindCascadeForWriting(cascade);
				GL.Clear(ClearBufferMask.DepthBufferBit);
				renderer.ShadowDepthShaderProgram.SetLightSpaceMatrix(renderer.CSMCaster.LightSpaceMatrices[cascade]);

				lock (renderer.Scene.VisibilityUpdateLock)
				{
					int lastVAOHandle = -1;
					double cascadeNear = cascade == 0 ? 0 : renderer.CSMCaster.CascadeFarDistances[cascade - 1];
					double cascadeFar = renderer.CSMCaster.CascadeFarDistances[cascade];
					// Use a margin to catch shadows from objects just outside the frustum or casting into next cascade
					double margin = 50.0; 

					Action<ICollection<FaceState>> renderFaces = faces =>
					{
						foreach (var face in faces)
						{
							if (face.Object.Prototype.Mesh.VAO == null) continue;
							if (face.Object.DisableShadowCasting) continue;

							// Cascade Culling: check if object is within depth range of this cascade
							Vector3 relativePos = face.Object.WorldPosition - context.Camera.AbsolutePosition;
							double depth = Vector3.Dot(relativePos, context.Camera.AbsoluteDirection);
							double radius = face.Object.Prototype.Mesh.BoundingSphereRadius;
							
							if (depth < cascadeNear - margin - radius || depth > cascadeFar + margin + radius)
							{
								continue;
							}

							Matrix4D modelMatrix = face.Object.ModelMatrix * context.Camera.TranslationMatrix;
							renderer.ShadowDepthShaderProgram.SetModelMatrix(modelMatrix);

							// Bind texture for alpha scissoring if the face has one and it actually needs alpha testing
							var material = face.Object.Prototype.Mesh.Materials[face.Face.Material];
							bool needsTexture = material.DaytimeTexture != null && 
							                    ((material.Flags & MaterialFlags.TransparentColor) != 0 || 
							                     material.Color.A < 255);

							if (needsTexture && renderer.currentHost.LoadTexture(ref material.DaytimeTexture, (OpenGlTextureWrapMode)(material.WrapMode ?? OpenGlTextureWrapMode.ClampClamp)))
							{
								renderer.SetActiveTexture(TextureUnit.Texture0);
								GL.BindTexture(TextureTarget.Texture2D,
									material.DaytimeTexture.OpenGlTextures[(int)(material.WrapMode ?? OpenGlTextureWrapMode.ClampClamp)].Name);
								renderer.ShadowDepthShaderProgram.SetHasTexture(true);
							}
							else
							{
								renderer.ShadowDepthShaderProgram.SetHasTexture(false);
							}

							renderer.ShadowDepthShaderProgram.SetAlphaCutoff(0.5f);
							renderer.ShadowDepthShaderProgram.SetMaterialAlpha(material.Color.A / 255.0f);
							renderer.ShadowDepthShaderProgram.SetMaterialFlags(material.Flags);

#pragma warning disable CS0618
							ObjectState state = face.Object;
							if (state.Matricies != null && state.Matricies.Length > 0)
							{
								renderer.ShadowDepthShaderProgram.SetCurrentAnimationMatricies(state);
								GL.BindBufferBase(BufferTarget.UniformBuffer, 0, state.MatrixBufferIndex);
							}
#pragma warning restore CS0618

							VertexArrayObject vao = (VertexArrayObject)face.Object.Prototype.Mesh.VAO;
							if (vao.handle != lastVAOHandle)
							{
								vao.Bind();
								lastVAOHandle = vao.handle;
							}
							
							// We need GetPrimitiveType, but it's protected in BaseRenderer. 
							// For now, I'll copy the logic or we should move it to a helper.
							PrimitiveType drawMode = GetPrimitiveType(face.Face.Flags);
							vao.Draw(drawMode, face.Face.IboStartIndex, face.Face.Vertices.Length);
						}
					};

					renderFaces(renderer.Scene.VisibleObjects.OpaqueFaces);
					renderFaces(renderer.Scene.VisibleObjects.AlphaFaces);
				}
				renderer.CSMShadowMaps.Unbind();
			}

			// 4. Restore viewport
			renderer.SetViewport(0, 0, renderer.Screen.Width, renderer.Screen.Height);

			renderer.LastBoundTexture = null;
		}

		private PrimitiveType GetPrimitiveType(FaceFlags flags)
		{
			switch (flags & FaceFlags.FaceTypeMask)
			{
				case FaceFlags.Triangles: return PrimitiveType.Triangles;
				case FaceFlags.TriangleStrip: return PrimitiveType.TriangleStrip;
				case FaceFlags.Quads: return PrimitiveType.Quads;
				case FaceFlags.QuadStrip: return PrimitiveType.QuadStrip;
				default: return PrimitiveType.Polygon;
			}
		}
	}
}
