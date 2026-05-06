using System;
using System.Collections.Generic;
using System.Linq;
using LibRender2.Objects;
using LibRender2.Shaders;
using OpenBveApi.Graphics;
using OpenBveApi.Hosts;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenTK.Graphics.OpenGL;
using LibRender2.GraphicsCore;
using OpenBveApi.Colors;
using OpenBveApi.Textures;
using OpenBveApi.Routes;


namespace LibRender2.Models
{
	public class ModelRenderer
	{
		private readonly RendererCore renderer;

		public List<ObjectState> StaticObjectStates;
		public List<ObjectState> DynamicObjectStates;
		public readonly VisibleObjectLibrary VisibleObjects;

		public int[] ObjectsSortedByStart;
		public int[] ObjectsSortedByEnd;
		public int ObjectsSortedByStartPointer;
		public int ObjectsSortedByEndPointer;
		public double LastUpdatedTrackPosition;

		// Render state
		public ObjectState lastObjectState;
		public Matrix4D lastModelMatrix;
		public Matrix4D lastModelViewMatrix;
		public bool sendToShader;
		public int lastVAO = -1;
		public Color32 lastColor = Color32.White;

		public ModelRenderer(RendererCore renderer)
		{
			this.renderer = renderer;
			StaticObjectStates = new List<ObjectState>();
			DynamicObjectStates = new List<ObjectState>();
			VisibleObjects = new VisibleObjectLibrary(renderer);
		}

		public void RenderFace(FaceState state, bool isDebugTouchMode = false)
		{
			RenderFace(renderer.CurrentShader as Shader, state.Object, state.Face, isDebugTouchMode);
		}

		public void RenderFace(Shader shader, ObjectState state, MeshFace face, Matrix4D modelMatrix, Matrix4D modelViewMatrix)
		{
			lastModelMatrix = modelMatrix;
			lastModelViewMatrix = modelViewMatrix;
			sendToShader = true;
			RenderFace(shader, state, face, false, true);
		}

		public void RenderFace(Shader shader, ObjectState state, MeshFace face, bool debugTouchMode = false, bool screenSpace = false)
		{
			if ((state != lastObjectState || state.Prototype.Dynamic) && !screenSpace)
			{
				lastModelMatrix = state.ModelMatrix * renderer.Camera.TranslationMatrix;
				lastModelViewMatrix = lastModelMatrix * renderer.CurrentViewMatrix;
				sendToShader = true;
			}

			if (state.Prototype.Mesh.Vertices.Length < 1)
			{
				return;
			}

			MeshMaterial material = state.Prototype.Mesh.Materials[face.Material];
			VertexArrayObject VAO = (VertexArrayObject)state.Prototype.Mesh.VAO;

			if (lastVAO != VAO.handle)
			{
				renderer.GraphicsDevice.BindVAO(VAO.handle);
				lastVAO = VAO.handle;
			}

			if (!renderer.OptionBackFaceCulling || (face.Flags & FaceFlags.Face2Mask) != 0)
			{
				renderer.GraphicsDevice.SetCullFace(false);
			}
			else if (renderer.OptionBackFaceCulling)
			{
				if ((face.Flags & FaceFlags.Face2Mask) == 0)
				{
					renderer.GraphicsDevice.SetCullFace(true);
				}
			}

			// model matricies
			if (state.Matricies != null && state.Matricies.Length > 0 && state != lastObjectState)
			{
				shader.SetCurrentAnimationMatricies(state);
				renderer.GraphicsDevice.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, state.MatrixBufferIndex);
			}

			// matrix
			if (sendToShader)
			{
				shader.SetCurrentModelViewMatrix(lastModelViewMatrix);
				shader.SetCurrentTextureMatrix(state.TextureTranslation);
				sendToShader = false;
			}

			if (renderer.OptionWireFrame || debugTouchMode)
			{
				renderer.GraphicsDevice.SetWireframe(true);
			}

			// lighting
			shader.SetMaterialFlags(material.Flags);
			if (renderer.OptionLighting)
			{
				if (material.Color != lastColor)
				{
					shader.SetMaterialAmbient(material.Color);
					shader.SetMaterialDiffuse(material.Color);
					shader.SetMaterialSpecular((material.Flags & MaterialFlags.Specular) != 0 ? material.SpecularColor : material.Color);
				}
				if ((material.Flags & MaterialFlags.Emissive) != 0)
				{
					shader.SetMaterialEmission(material.EmissiveColor);
				}

				shader.SetMaterialShininess(1.0f);
			}
			else
			{
				if (material.Color != lastColor)
				{
					shader.SetMaterialAmbient(material.Color);
				}
			}

			lastColor = material.Color;
			PrimitiveType drawMode;

			switch (face.Flags & FaceFlags.FaceTypeMask)
			{
				case FaceFlags.Triangles:
					drawMode = PrimitiveType.Triangles;
					break;
				case FaceFlags.TriangleStrip:
					drawMode = PrimitiveType.TriangleStrip;
					break;
				case FaceFlags.Quads:
					drawMode = PrimitiveType.Quads;
					break;
				case FaceFlags.QuadStrip:
					drawMode = PrimitiveType.QuadStrip;
					break;
				default:
					drawMode = PrimitiveType.Polygon;
					break;
			}

			// blend factor
			float distanceFactor;
			if (material.GlowAttenuationData != 0)
			{
				distanceFactor = (float)Glow.GetDistanceFactor(lastModelMatrix, state.Prototype.Mesh.Vertices, ref face, material.GlowAttenuationData);
			}
			else
			{
				distanceFactor = 1.0f;
			}

			float blendFactor = RendererCore.inv255 * state.DaytimeNighttimeBlend + 1.0f - renderer.Lighting.OptionLightingResultingAmount;
			if (blendFactor > 1.0)
			{
				blendFactor = 1.0f;
			}

			// daytime polygon
			{
				if (material.DaytimeTexture != null && renderer.currentHost.LoadTexture(ref material.DaytimeTexture, (OpenGlTextureWrapMode)material.WrapMode))
				{
					renderer.GraphicsDevice.BindTexture(material.DaytimeTexture.OpenGlTextures[(int)material.WrapMode].Name);
					renderer.LastBoundTexture = material.DaytimeTexture.OpenGlTextures[(int)material.WrapMode];
				}
				else
				{
					shader.DisableTexturing();
				}
				
				float factor;
				if (material.BlendMode == MeshMaterialBlendMode.Additive)
				{
					factor = 1.0f;
					renderer.GraphicsDevice.SetBlend(true);
					renderer.GraphicsDevice.SetBlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
					shader.SetFog(false);
				}
				else if (material.NighttimeTexture == null || material.NighttimeTexture == material.DaytimeTexture)
				{
					factor = 1.0f - 0.7f * blendFactor;
				}
				else
				{
					factor = 1.0f;
				}
				shader.SetBrightness(factor);

				float alphaFactor = distanceFactor;
				if (material.NighttimeTexture != null && (material.Flags & MaterialFlags.CrossFadeTexture) != 0)
				{
					alphaFactor *= 1.0f - blendFactor;
				}

				shader.SetOpacity(RendererCore.inv255 * material.Color.A * alphaFactor);
				VAO.Draw(drawMode, face.IboStartIndex, face.Vertices.Length);
			}

			// nighttime polygon
			if (blendFactor != 0 && material.NighttimeTexture != null && material.NighttimeTexture != material.DaytimeTexture && renderer.currentHost.LoadTexture(ref material.NighttimeTexture, (OpenGlTextureWrapMode)material.WrapMode))
			{
				renderer.GraphicsDevice.BindTexture(material.NighttimeTexture.OpenGlTextures[(int)material.WrapMode].Name);
				renderer.LastBoundTexture = material.NighttimeTexture.OpenGlTextures[(int)material.WrapMode];

				renderer.GraphicsDevice.SetBlend(true);
				shader.SetAlphaTest(true);
				shader.SetAlphaFunction(AlphaFunction.Greater, 0.0f);
				
				float alphaFactor = distanceFactor * blendFactor;
				shader.SetOpacity(RendererCore.inv255 * material.Color.A * alphaFactor);
				VAO.Draw(drawMode, face.IboStartIndex, face.Vertices.Length);
				
				renderer.RestoreBlendFunc();
				renderer.RestoreAlphaFunc();
			}

			// normals
			if (renderer.OptionNormals)
			{
				shader.DisableTexturing();
				shader.SetBrightness(1.0f);
				shader.SetOpacity(1.0f);
				VertexArrayObject normalsVao = (VertexArrayObject)state.Prototype.Mesh.NormalsVAO;
				renderer.GraphicsDevice.BindVAO(normalsVao.handle);
				lastVAO = normalsVao.handle;
				normalsVao.Draw(PrimitiveType.Lines, face.NormalsIboStartIndex, face.Vertices.Length * 2);
			}

			// finalize
			if (material.BlendMode == MeshMaterialBlendMode.Additive)
			{
				renderer.RestoreBlendFunc();
				shader.SetFog(renderer.Fog.Enabled);
			}
			if (renderer.OptionWireFrame || debugTouchMode)
			{
				renderer.GraphicsDevice.SetWireframe(false);
			}
			lastObjectState = state;
		}
		public void CreateDynamicObject(ref ObjectState internalObject)
		{
			if (internalObject == null)
			{
				internalObject = new ObjectState(new StaticObject(renderer.currentHost));
			}

			internalObject.Prototype.Dynamic = true;

			DynamicObjectStates.Add(internalObject);
		}

		public void InitializeVisibility()
		{
			for (int i = 0; i < StaticObjectStates.Count; i++)
			{
				VAOExtensions.CreateVAO(StaticObjectStates[i].Prototype.Mesh, false, renderer.DefaultShader.VertexLayout, renderer);
			}
			for (int i = 0; i < DynamicObjectStates.Count; i++)
			{
				VAOExtensions.CreateVAO(DynamicObjectStates[i].Prototype.Mesh, false, renderer.DefaultShader.VertexLayout, renderer);
			}
			ObjectsSortedByStart = StaticObjectStates.Select((x, i) => new { Index = i, Distance = x.StartingDistance }).OrderBy(x => x.Distance).Select(x => x.Index).ToArray();
			ObjectsSortedByEnd = StaticObjectStates.Select((x, i) => new { Index = i, Distance = x.EndingDistance }).OrderBy(x => x.Distance).Select(x => x.Index).ToArray();
			ObjectsSortedByStartPointer = 0;
			ObjectsSortedByEndPointer = 0;
			
			if (renderer.currentOptions.ObjectDisposalMode == ObjectDisposalMode.QuadTree)
			{
				foreach (ObjectState state in StaticObjectStates)
				{
					VisibleObjects.quadTree.Add(state, Orientation3.Default);
				}
				VisibleObjects.quadTree.Initialize(renderer.currentOptions.QuadTreeLeafSize);
				UpdateQuadTreeVisibility();
			}
			else
			{
				double p = renderer.CameraTrackFollower.TrackPosition + renderer.Camera.Alignment.Position.Z;
				foreach (ObjectState state in StaticObjectStates.Where(recipe => recipe.StartingDistance <= p + renderer.Camera.ForwardViewingDistance & recipe.EndingDistance >= p - renderer.Camera.BackwardViewingDistance))
				{
					VisibleObjects.ShowObject(state, ObjectType.Static);
				}
			}
		}

		public void UpdateVisibility(double trackPosition, VisibilityUpdate updateVisibility)
		{
			if (renderer.currentOptions.ObjectDisposalMode == ObjectDisposalMode.QuadTree)
			{
				UpdateQuadTreeVisibility();
			}
			else
			{
				if (updateVisibility == VisibilityUpdate.Normal)
				{
					UpdateLegacyVisibility(trackPosition);
				}
				else
				{
					UpdateLegacyVisibility(trackPosition + 0.01);
					UpdateLegacyVisibility(trackPosition - 0.01);
				}
			}
		}

		public void UpdateQuadTreeVisibility()
		{
			if (VisibleObjects == null || VisibleObjects.quadTree == null)
			{
				return;
			}
			renderer.Camera.UpdateQuadTreeLeaf();
		}

		private void UpdateLegacyVisibility(double trackPosition)
		{
			if (ObjectsSortedByStart == null || ObjectsSortedByStart.Length == 0 || StaticObjectStates.Count == 0)
			{
				return;
			}
			double d = trackPosition - LastUpdatedTrackPosition;
			int n = ObjectsSortedByStart.Length;
			double p = renderer.CameraTrackFollower.TrackPosition + renderer.Camera.Alignment.Position.Z;

			if (d < 0.0)
			{
				if (ObjectsSortedByStartPointer >= n)
				{
					ObjectsSortedByStartPointer = n - 1;
				}

				if (ObjectsSortedByEndPointer >= n)
				{
					ObjectsSortedByEndPointer = n - 1;
				}

				// dispose
				while (ObjectsSortedByStartPointer >= 0)
				{
					int o = ObjectsSortedByStart[ObjectsSortedByStartPointer];

					if (StaticObjectStates[o].StartingDistance > p + renderer.Camera.ForwardViewingDistance)
					{
						VisibleObjects.HideObject(StaticObjectStates[o]);
						ObjectsSortedByStartPointer--;
					}
					else
					{
						break;
					}
				}

				// introduce
				while (ObjectsSortedByEndPointer >= 0)
				{
					int o = ObjectsSortedByEnd[ObjectsSortedByEndPointer];

					if (StaticObjectStates[o].EndingDistance >= p - renderer.Camera.BackwardViewingDistance)
					{
						if (StaticObjectStates[o].StartingDistance <= p + renderer.Camera.ForwardViewingDistance)
						{
							VisibleObjects.ShowObject(StaticObjectStates[o], ObjectType.Static);
						}

						ObjectsSortedByEndPointer--;
					}
					else
					{
						break;
					}
				}
			}
			else if (d > 0.0)
			{
				if (ObjectsSortedByStartPointer < 0)
				{
					ObjectsSortedByStartPointer = 0;
				}

				if (ObjectsSortedByEndPointer < 0)
				{
					ObjectsSortedByEndPointer = 0;
				}

				// dispose
				while (ObjectsSortedByEndPointer < n)
				{
					int o = ObjectsSortedByEnd[ObjectsSortedByEndPointer];

					if (StaticObjectStates[o].EndingDistance < p - renderer.Camera.BackwardViewingDistance)
					{
						VisibleObjects.HideObject(StaticObjectStates[o]);
						ObjectsSortedByEndPointer++;
					}
					else
					{
						break;
					}
				}

				// introduce
				while (ObjectsSortedByStartPointer < n)
				{
					int o = ObjectsSortedByStart[ObjectsSortedByStartPointer];

					if (StaticObjectStates[o].StartingDistance <= p + renderer.Camera.ForwardViewingDistance)
					{
						if (StaticObjectStates[o].EndingDistance >= p - renderer.Camera.BackwardViewingDistance)
						{
							VisibleObjects.ShowObject(StaticObjectStates[o], ObjectType.Static);
						}

						ObjectsSortedByStartPointer++;
					}
					else
					{
						break;
					}
				}
			}

			LastUpdatedTrackPosition = trackPosition;
		}
	}
}
