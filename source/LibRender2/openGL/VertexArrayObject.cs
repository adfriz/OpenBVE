using System;
using System.Collections.Generic;
using System.Linq;
using LibRender2.Shaders;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Routes;
using Raylib_cs;

namespace LibRender2
{
	public class VertexArrayObject : IDisposable
	{
		internal uint vboId;
		internal uint iboId;
		internal int vertexCount;
		private bool disposed;

		public VertexArrayObject()
		{
		}

		public void Bind()
		{
			// rlgl handles binding internally when drawing
		}

		public void SetVBO(LibRenderVertex[] vertices, bool isDynamic)
		{
			if (vboId != 0) rlgl.rlUnloadVertexBuffer(vboId);
			vboId = rlgl.rlLoadVertexBuffer(vertices, isDynamic);
			vertexCount = vertices.Length;
		}

		public void SetIBO(ushort[] indices, bool isDynamic)
		{
			if (iboId != 0) rlgl.rlUnloadVertexBuffer(iboId);
			iboId = rlgl.rlLoadVertexBufferElement(indices, isDynamic);
		}

		public void SetIBO(uint[] indices, bool isDynamic)
		{
			// rlgl handles uint indices if supported by backend
			if (iboId != 0) rlgl.rlUnloadVertexBuffer(iboId);
			iboId = rlgl.rlLoadVertexBufferElement(indices, isDynamic);
		}

		public void Draw(rlgl.rlPrimitiveMode drawMode, int start, int count)
		{
			rlgl.rlEnableVertexArray(vboId);
			rlgl.rlEnableVertexBufferElement(iboId);
			
			// Setup Attributes
			// Assumes a fixed layout for OpenBVE shaders
			int stride = LibRenderVertex.SizeInBytes;
			rlgl.rlSetVertexAttribute(0, 3, rlgl.RL_FLOAT, false, stride, (IntPtr)0); // Position
			rlgl.rlEnableVertexAttribute(0);
			rlgl.rlSetVertexAttribute(1, 3, rlgl.RL_FLOAT, false, stride, (IntPtr)12); // Normal
			rlgl.rlEnableVertexAttribute(1);
			rlgl.rlSetVertexAttribute(2, 2, rlgl.RL_FLOAT, false, stride, (IntPtr)24); // UV
			rlgl.rlEnableVertexAttribute(2);
			rlgl.rlSetVertexAttribute(3, 4, rlgl.RL_FLOAT, false, stride, (IntPtr)32); // Color (Color128 is 4 floats)
			rlgl.rlEnableVertexAttribute(3);
			rlgl.rlSetVertexAttribute(4, 3, rlgl.RL_INT, false, stride, (IntPtr)48); // MatrixChain
			rlgl.rlEnableVertexAttribute(4);

			rlgl.rlDrawVertexArrayElements(start, count, IntPtr.Zero);
			
			rlgl.rlDisableVertexAttribute(0);
			rlgl.rlDisableVertexAttribute(1);
			rlgl.rlDisableVertexAttribute(2);
			rlgl.rlDisableVertexAttribute(3);
			rlgl.rlDisableVertexAttribute(4);
			rlgl.rlDisableVertexArray();
			rlgl.rlDisableVertexBufferElement();
		}

		public void UnBind()
		{
		}

		public void Dispose()
		{
			if (disposed) return;
			if (vboId != 0) rlgl.rlUnloadVertexBuffer(vboId);
			if (iboId != 0) rlgl.rlUnloadVertexBuffer(iboId);
			disposed = true;
		}
	}

	public static class VAOExtensions
	{
		public static void CreateVAO(Mesh mesh, bool isDynamic, VertexLayout vertexLayout, BaseRenderer renderer)
		{
			var vertexData = new List<LibRenderVertex>(mesh.Vertices.Length);
			var indexData = new List<uint>();

			for (int i = 0; i < mesh.Faces.Length; i++)
			{
				mesh.Faces[i].IboStartIndex = indexData.Count;
				foreach (var vertex in mesh.Faces[i].Vertices)
				{
					vertexData.Add(new LibRenderVertex(mesh.Vertices[vertex.Index], vertex.Normal));
				}
				indexData.AddRange(Enumerable.Range(mesh.Faces[i].IboStartIndex, mesh.Faces[i].Vertices.Length).Select(x => (uint)x));
			}

			VertexArrayObject vao = new VertexArrayObject();
			vao.SetVBO(vertexData.ToArray(), isDynamic);
			vao.SetIBO(indexData.ToArray(), isDynamic);
			mesh.VAO = vao;
		}
	}
}
