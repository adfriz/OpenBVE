using System;
using System.Collections.Generic;
using LibRender2.Shaders;
using OpenBveApi.Math;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Objects
{
	/// <summary>
	/// Manages hardware instanced rendering for repetitive objects.
	/// </summary>
	public class InstanceManager : IDisposable
	{
		private readonly BaseRenderer renderer;
		private readonly InstanceBufferObject instanceBuffer;
		private readonly Matrix4[] matrixCache;
		private const int MaxInstancesPerBatch = 1024;
		private bool disposed;

		public InstanceManager(BaseRenderer renderer)
		{
			this.renderer = renderer;
			this.instanceBuffer = new InstanceBufferObject();
			this.matrixCache = new Matrix4[MaxInstancesPerBatch];
			// Reserve GPU memory for the buffer once
			this.instanceBuffer.Bind();
			this.instanceBuffer.BufferData(matrixCache, BufferUsageHint.DynamicDraw);
		}

		/// <summary>
		/// Renders a batch of identical faces using hardware instancing.
		/// </summary>
		/// <param name="shader">The shader to use.</param>
		/// <param name="faces">The list of faces to draw.</param>
		/// <param name="startIndex">The starting index in the list.</param>
		/// <param name="count">The number of instances to draw.</param>
		public void RenderBatch(Shader shader, PrimitiveType drawMode, List<FaceState> faces, int startIndex, int count)
		{
			if (count <= 0 || shader == null) return;

			FaceState firstFace = faces[startIndex];
			MeshMaterial material = firstFace.Object.Prototype.Mesh.Materials[firstFace.Face.Material];
			VertexArrayObject vao = (VertexArrayObject)firstFace.Object.Prototype.Mesh.VAO;
			
			if (vao == null) return;

			// 1. Setup Shader material and texture
			shader.SetIsInstanced(true);
			shader.SetMaterialFlags(material.Flags);
			shader.SetMaterialAmbient(material.Color);
			shader.SetMaterialDiffuse(material.Color);
			shader.SetMaterialSpecular(material.Color);
			shader.SetMaterialEmission(material.EmissiveColor);
			shader.SetMaterialShininess(material.Shininess);
			
			if (material.DaytimeTexture != null && renderer.TextureManager.LoadTexture(ref material.DaytimeTexture, (OpenGlTextureWrapMode)(material.WrapMode ?? OpenGlTextureWrapMode.ClampClamp)))
			{
				GL.ActiveTexture(TextureUnit.Texture0);
				GL.BindTexture(TextureTarget.Texture2D, material.DaytimeTexture.OpenGlTextures[(int)(material.WrapMode ?? OpenGlTextureWrapMode.ClampClamp)].Name);
				renderer.LastBoundTexture = material.DaytimeTexture.OpenGlTextures[(int)(material.WrapMode ?? OpenGlTextureWrapMode.ClampClamp)];
			}

			// 2. Prepare instance data
			int instancesToDraw = Math.Min(count, MaxInstancesPerBatch);
			for (int i = 0; i < instancesToDraw; i++)
			{
				FaceState state = faces[startIndex + i];
				// Compute ModelView matrix (consistent with standard RenderFace)
				Matrix4D modelMatrix = state.Object.ModelMatrix * renderer.Camera.TranslationMatrix;
				Matrix4D modelViewMatrix = modelMatrix * renderer.CurrentViewMatrix;
				matrixCache[i] = ConvertToMatrix4(modelViewMatrix);
			}

			// 2. Setup VAO and Buffer
			vao.Bind();
			if (vao.InstanceVbo == null)
			{
				vao.SetInstanceVBO(instanceBuffer, shader.VertexLayout);
			}
			
			instanceBuffer.Bind();
			instanceBuffer.BufferSubData(matrixCache, instancesToDraw);

			// 3. Set Shader state
			shader.SetIsInstanced(true);
			shader.SetMaterialFlags(firstFace.Object.Prototype.Mesh.Materials[firstFace.Face.Material].Flags);
			// Note: We assume all objects in the batch have the same material/texture for now.
			// This is true for StaticObjects using the same Prototype.

			// 4. Draw
			vao.DrawInstanced(drawMode, firstFace.Face.IboStartIndex, firstFace.Face.Vertices.Length, instancesToDraw);

			// 5. Cleanup
			shader.SetIsInstanced(false);
		}

		private Matrix4 ConvertToMatrix4(Matrix4D mat)
		{
			return new Matrix4(
				(float)mat.Row0.X, (float)mat.Row0.Y, (float)mat.Row0.Z, (float)mat.Row0.W,
				(float)mat.Row1.X, (float)mat.Row1.Y, (float)mat.Row1.Z, (float)mat.Row1.W,
				(float)mat.Row2.X, (float)mat.Row2.Y, (float)mat.Row2.Z, (float)mat.Row2.W,
				(float)mat.Row3.X, (float)mat.Row3.Y, (float)mat.Row3.Z, (float)mat.Row3.W
			);
		}

		public void Dispose()
		{
			if (disposed) return;
			instanceBuffer.Dispose();
			disposed = true;
		}
	}
}
