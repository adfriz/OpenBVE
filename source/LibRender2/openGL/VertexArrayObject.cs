using System;
using System.Collections.Generic;
using System.Linq;
using LibRender2.Shaders;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Routes;
using OpenBveApi.Math;
using OpenBveApi.Routes;
using OpenBveApi.Colors;
using Raylib_cs;

using Mesh = OpenBveApi.Objects.Mesh;




namespace LibRender2
{
	public class VertexArrayObject : IDisposable
	{
		public uint vboId;
		public uint iboId;

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
			if (vboId != 0) Rlgl.UnloadVertexBuffer(vboId);
			unsafe
			{
				fixed (LibRenderVertex* ptr = vertices)
				{
					vboId = Rlgl.LoadVertexBuffer(ptr, vertices.Length * LibRenderVertex.SizeInBytes, isDynamic);
				}
			}
			vertexCount = vertices.Length;
		}

		public void SetIBO(ushort[] indices, bool isDynamic)
		{
			if (iboId != 0) Rlgl.UnloadVertexBuffer(iboId);
			unsafe
			{
				fixed (ushort* ptr = indices)
				{
					iboId = Rlgl.LoadVertexBufferElement(ptr, indices.Length * sizeof(ushort), isDynamic);
				}
			}
		}

		public void SetIBO(uint[] indices, bool isDynamic)
		{
			if (iboId != 0) Rlgl.UnloadVertexBuffer(iboId);
			unsafe
			{
				fixed (uint* ptr = indices)
				{
					iboId = Rlgl.LoadVertexBufferElement(ptr, indices.Length * sizeof(uint), isDynamic);
				}
			}
		}


		public unsafe void Draw(int drawMode, int start, int count)




		{
			Rlgl.EnableVertexArray(vboId);
			Rlgl.EnableVertexBufferElement(iboId);
			
			// Setup Attributes
			// Assumes a fixed layout for OpenBVE shaders
			int stride = LibRenderVertex.SizeInBytes;
			Rlgl.SetVertexAttribute(0, 3, 0x1406, false, stride, (void*)0); // Position
			Rlgl.EnableVertexAttribute(0);
			Rlgl.SetVertexAttribute(1, 3, 0x1406, false, stride, (void*)12); // Normal
			Rlgl.EnableVertexAttribute(1);
			Rlgl.SetVertexAttribute(2, 2, 0x1406, false, stride, (void*)24); // UV
			Rlgl.EnableVertexAttribute(2);
			Rlgl.SetVertexAttribute(3, 4, 0x1406, false, stride, (void*)32); // Color
			Rlgl.EnableVertexAttribute(3);
			Rlgl.SetVertexAttribute(4, 3, 0x1404, false, stride, (void*)48); // MatrixChain
			Rlgl.EnableVertexAttribute(4);


			Rlgl.DrawVertexArrayElements(start, count, (void*)0);

			
			Rlgl.DisableVertexAttribute(0);
			Rlgl.DisableVertexAttribute(1);
			Rlgl.DisableVertexAttribute(2);
			Rlgl.DisableVertexAttribute(3);
			Rlgl.DisableVertexAttribute(4);
			Rlgl.DisableVertexArray();
			Rlgl.DisableVertexBufferElement();
		}

		public void UnBind()
		{
		}

		public void Dispose()
		{
			if (disposed) return;
			if (vboId != 0) Rlgl.UnloadVertexBuffer(vboId);
			if (iboId != 0) Rlgl.UnloadVertexBuffer(iboId);
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
		public static void CreateVAO(this StaticBackground background, VertexLayout vertexLayout, BaseRenderer renderer)
		{
			// background cylinder
			float y0, y1;
			if (background.KeepAspectRatio)
			{
				double hh = Math.PI * background.BackgroundImageDistance * background.Texture.Height / (background.Texture.Width * background.Repetition);
				y0 = (float)(-0.5 * hh);
				y1 = (float)(1.5 * hh);
			}
			else
			{
				y0 = (float)(-0.125 * background.BackgroundImageDistance);
				y1 = (float)(0.375 * background.BackgroundImageDistance);
			}

			const int n = 32;
			LibRenderVertex[] vertices = new LibRenderVertex[n * 4];
			ushort[] indices = new ushort[n * 6];

			double angleValue = 2.61799387799149 - 3.14159265358979 / n;
			const double angleIncrement = 6.28318530717958 / n;

			float textureStart = 0.5f * (float)background.Repetition / n;
			float textureIncrement = -(float)background.Repetition / n;
			double textureX = textureStart;

			for (int i = 0; i < n; i++)
			{
				float x0 = (float)(background.BackgroundImageDistance * Math.Cos(angleValue));
				float z0 = (float)(background.BackgroundImageDistance * Math.Sin(angleValue));
				float x1 = (float)(background.BackgroundImageDistance * Math.Cos(angleValue + angleIncrement));
				float z1 = (float)(background.BackgroundImageDistance * Math.Sin(angleValue + angleIncrement));

				vertices[i * 4 + 0] = new LibRenderVertex { Position = new Vector3f(x0, y1, z0), UV = new Vector2f((float)textureX, 0.005f), Color = Color128.White };
				vertices[i * 4 + 1] = new LibRenderVertex { Position = new Vector3f(x0, y0, z0), UV = new Vector2f((float)textureX, 0.995f), Color = Color128.White };
				vertices[i * 4 + 2] = new LibRenderVertex { Position = new Vector3f(x1, y0, z1), UV = new Vector2f((float)(textureX + textureIncrement), 0.995f), Color = Color128.White };
				vertices[i * 4 + 3] = new LibRenderVertex { Position = new Vector3f(x1, y1, z1), UV = new Vector2f((float)(textureX + textureIncrement), 0.005f), Color = Color128.White };

				indices[i * 6 + 0] = (ushort)(i * 4 + 0);
				indices[i * 6 + 1] = (ushort)(i * 4 + 1);
				indices[i * 6 + 2] = (ushort)(i * 4 + 2);
				indices[i * 6 + 3] = (ushort)(i * 4 + 0);
				indices[i * 6 + 4] = (ushort)(i * 4 + 2);
				indices[i * 6 + 5] = (ushort)(i * 4 + 3);

				angleValue += angleIncrement;
				textureX += textureIncrement;
			}

			background.VAO = new VertexArrayObject();
			((VertexArrayObject)background.VAO).SetVBO(vertices, false);
			((VertexArrayObject)background.VAO).SetIBO(indices, false);
		}
	}
}

