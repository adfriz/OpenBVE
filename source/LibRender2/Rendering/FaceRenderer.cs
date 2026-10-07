using System.Linq;
using LibRender2.Abstractions;
using LibRender2.Objects;
using LibRender2.Shaders;
using OpenBveApi.Colors;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Textures;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Rendering
{
	/// <summary>
	/// Per-face material / lighting / blending path extracted from BaseRenderer.
	/// BaseRenderer keeps identically-signed RenderFace facades for compatibility.
	/// </summary>
	public class FaceRenderer
	{
		private const float inv255 = 1.0f / 255.0f;

		private readonly IFaceRendererHost host;

		private ObjectState lastObjectState;
		private Matrix4D lastModelMatrix;
		private Matrix4D lastModelViewMatrix;
		private bool sendToShader;

		public FaceRenderer(IFaceRendererHost host)
		{
			this.host = host;
		}

		internal ObjectState LastObjectState
		{
			get => lastObjectState;
			set => lastObjectState = value;
		}

		public PrimitiveType GetPrimitiveType(FaceFlags flags)
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

		public void RenderFace(FaceState state, bool isDebugTouchMode = false)
		{
			RenderFace(host.DefaultShader, state.Object, state.Face, isDebugTouchMode);
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
				lastModelMatrix = state.ModelMatrix * host.Camera.TranslationMatrix;
				lastModelViewMatrix = lastModelMatrix * host.GetCurrentViewMatrix();
				sendToShader = true;
			}

			if (state.Prototype.Mesh.Vertices.Length < 1)
			{
				return;
			}

			MeshMaterial material = state.Prototype.Mesh.Materials[face.Material];
			VertexArrayObject VAO = (VertexArrayObject)state.Prototype.Mesh.VAO;

			if (host.LastVAO != VAO.handle)
			{
				VAO.Bind();
				host.LastVAO = VAO.handle;
			}

			// Mirrors origin/master: always issue the GL call (no stale-cache reads;
			// other passes such as Shadows touch cull state directly). Bookkeeping
			// is kept in sync for any readers of IRenderState.
			if (!host.OptionBackFaceCulling || (face.Flags & FaceFlags.Face2Mask) != 0)
			{
				GL.Disable(EnableCap.CullFace);
				host.CullFaceEnabled = false;
			}
			else if (host.OptionBackFaceCulling)
			{
				if ((face.Flags & FaceFlags.Face2Mask) == 0)
				{
					GL.Enable(EnableCap.CullFace);
					host.CullFaceEnabled = true;
				}
			}

			// model matricies
			if (state.Matricies != null && state.Matricies.Length > 0 && state != lastObjectState)
			{
				// n.b. if buffer has no data in it (matricies are of zero length), attempting to bind generates an InvalidValue
				shader.SetCurrentAnimationMatricies(state);
#pragma warning disable CS0618
				GL.BindBufferBase(BufferTarget.UniformBuffer, 0, state.MatrixBufferIndex);
#pragma warning restore CS0618
			}

			// matrix
			if (sendToShader)
			{
				shader.SetCurrentModelViewMatrix(lastModelViewMatrix);
				shader.SetCurrentTextureMatrix(state.TextureTranslation);
				sendToShader = false;
			}

			if (host.OptionWireFrame || debugTouchMode)
			{
				GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
			}

			// lighting
			shader.SetMaterialFlags(material.Flags);
			if (host.OptionLighting)
			{
				if (material.Color != host.LastColor)
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
				if (material.Color != host.LastColor)
				{
					shader.SetMaterialAmbient(material.Color);
				}
			}

			host.LastColor = material.Color;
			PrimitiveType drawMode = GetPrimitiveType(face.Flags);

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

			float blendFactor = inv255 * state.DaytimeNighttimeBlend + 1.0f - host.Lighting.OptionLightingResultingAmount;
			if (blendFactor > 1.0)
			{
				blendFactor = 1.0f;
			}

			// daytime polygon
			{
				// texture
				// ReSharper disable once PossibleInvalidOperationException
				if (material.DaytimeTexture != null && host.Host.LoadTexture(ref material.DaytimeTexture, (OpenGlTextureWrapMode)material.WrapMode))
				{
					if (host.LastBoundTexture != material.DaytimeTexture.OpenGlTextures[(int)material.WrapMode])
					{
						GL.BindTexture(TextureTarget.Texture2D,
							material.DaytimeTexture.OpenGlTextures[(int)material.WrapMode].Name);
						host.LastBoundTexture = material.DaytimeTexture.OpenGlTextures[(int)material.WrapMode];
					}
				}
				else
				{
					shader.DisableTexturing();
				}
				// Calculate the brightness of the poly to render
				float factor;
				if (material.BlendMode == MeshMaterialBlendMode.Additive)
				{
					//Additive blending- Full brightness
					factor = 1.0f;
					GL.Enable(EnableCap.Blend);
					GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
					shader.SetFog(false);
				}
				else if (material.NighttimeTexture == null || material.NighttimeTexture == material.DaytimeTexture)
				{
					//No nighttime texture or both are identical- Darken the polygon to match the light conditions
					factor = 1.0f - 0.7f * blendFactor;
				}
				else
				{
					//Valid nighttime texture- Blend the two textures by DNB at max brightness
					factor = 1.0f;
				}
				shader.SetBrightness(factor);

				float alphaFactor = distanceFactor;
				if (material.NighttimeTexture != null && (material.Flags & MaterialFlags.CrossFadeTexture) != 0)
				{
					alphaFactor *= 1.0f - blendFactor;
				}

				shader.SetOpacity(inv255 * material.Color.A * alphaFactor);

				// render polygon
				VAO.Draw(drawMode, face.IboStartIndex, face.Vertices.Length);
			}

			// nighttime polygon
			if (blendFactor != 0 && material.NighttimeTexture != null && material.NighttimeTexture != material.DaytimeTexture && host.Host.LoadTexture(ref material.NighttimeTexture, (OpenGlTextureWrapMode)material.WrapMode))
			{
				// texture
				if (host.LastBoundTexture != material.NighttimeTexture.OpenGlTextures[(int)material.WrapMode])
				{
					GL.BindTexture(TextureTarget.Texture2D, material.NighttimeTexture.OpenGlTextures[(int)material.WrapMode].Name);
					host.LastBoundTexture = material.NighttimeTexture.OpenGlTextures[(int)material.WrapMode];
				}


				GL.Enable(EnableCap.Blend);

				// alpha test
				shader.SetAlphaTest(true);
				shader.SetAlphaFunction(AlphaFunction.Greater, 0.0f);

				// blend mode
				float alphaFactor = distanceFactor * blendFactor;

				shader.SetOpacity(inv255 * material.Color.A * alphaFactor);

				// render polygon
				VAO.Draw(drawMode, face.IboStartIndex, face.Vertices.Length);
				host.RestoreBlendFunc();
				host.RestoreAlphaFunc();
			}


			// normals
			if (host.OptionNormals)
			{
				shader.DisableTexturing();
				shader.SetBrightness(1.0f);
				shader.SetOpacity(1.0f);
				Mesh normalsMesh = state.Prototype.Mesh;
				if (normalsMesh.NormalsVAO == null)
				{
					// Build the normals VAO lazily on first use to avoid holding a duplicate vertex buffer in RAM
					VAOExtensions.CreateNormalsVAO(normalsMesh, state.Prototype.Dynamic, host.DefaultShader.VertexLayout, (BaseRenderer)host);
				}
				VertexArrayObject normalsVao = (VertexArrayObject)normalsMesh.NormalsVAO;
				if (normalsVao != null && face.IboStartIndex == 0)
				{
					// Draw all normals for this mesh in a single call (the normals VAO is a contiguous list of line pairs).
					// Solid purple overlay: disable lighting and force a white, emissive material so the shader
					// outputs the baked per-vertex purple colour unchanged (finalColor *= oLightResult == white).
					shader.SetIsLight(false);
					shader.SetMaterialAmbient(Color32.White);
					shader.SetMaterialFlags(MaterialFlags.Emissive);
					normalsVao.Bind();
					host.LastVAO = normalsVao.handle;
					normalsVao.DrawArrays(PrimitiveType.Lines, 0, normalsMesh.Vertices.Length > 0 ? normalsMesh.Faces.Sum(f => f.Vertices.Length) * 2 : 0);
					shader.SetIsLight(host.OptionLighting);
					shader.SetMaterialFlags(material.Flags);
					shader.SetMaterialAmbient(material.Color);
				}
			}

			// finalize
			if (material.BlendMode == MeshMaterialBlendMode.Additive)
			{
				host.RestoreBlendFunc();
				shader.SetFog(host.Fog.Enabled);
			}
			if (host.OptionWireFrame || debugTouchMode)
			{
				GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
			}
			lastObjectState = state;
		}
	}
}
