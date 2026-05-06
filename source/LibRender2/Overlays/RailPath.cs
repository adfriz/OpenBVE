using LibRender2.GraphicsCore;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenBveApi.Colors;
using OpenBveApi.Hosts;
using OpenBveApi.Math;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Overlays
{
	/// <summary>A rendered rail path overlay</summary>
	public class RailPath
	{
		private readonly HostInterface currentHost;

		private readonly RendererCore Renderer;
		/// <summary>The length of a track block</summary>
		private readonly double BlockLength;
		/// <summary>The overlay color to render</summary>
		public Color24 Color;
		/// <summary>The line width to use</summary>
		internal int LineWidth;
		/// <summary>The key of the rail</summary>
		internal readonly int RailKey;
		/// <summary>A textual description of the rail's purpose</summary>
		public string Description;

		public bool Display;

		/// <summary>Whether the path is visible at the current camera location</summary>
		public bool CurrentlyVisible()
		{
			return Display && Visible(Renderer.CameraTrackFollower.TrackPosition, out _);
		}

		/// <summary>Whether the path is visible at the specified location</summary>
		/// <param name="trackPosition">The track position to check</param>
		/// <returns></returns>
		public bool Visible(double trackPosition)
		{
			return Display && Visible(trackPosition, out _);
		}

		/// <summary>Whether this path is visible at the specified location</summary>
		/// <param name="trackPosition">The track position to check</param>
		/// <param name="startElement">Returns the starting visible element number</param>
		private bool Visible(double trackPosition, out int startElement)
		{
			startElement = 0;
			if (Display)
			{
				for (int e = 0; e < currentHost.Tracks[RailKey].Elements.Length; e++)
				{
					if (Math.Abs(currentHost.Tracks[RailKey].Elements[e].StartingTrackPosition -trackPosition) <= 25.0)
					{
						startElement = e;
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>Renders the path overlay</summary>
		public void Render()
		{
			double halfDistance = (Math.Max(Renderer.currentOptions.ViewingDistance, 1000) / 2.0) * 1.1;
			int numElements = (int)(halfDistance / BlockLength);
			if (!Display || !Visible(Renderer.CameraTrackFollower.TrackPosition, out int startElement))
			{
				return;
			}
			int firstElement = Math.Max(0, startElement - numElements);
			int lastElement = Math.Min(currentHost.Tracks[RailKey].Elements.Length, startElement + numElements);
			if (lastElement < firstElement)
			{
				return;
			}

			List<List<Vector3>> sections = new List<List<Vector3>>{ new List<Vector3>() };
			bool lastElementInvalid = false;
			
			for (int e = firstElement; e < lastElement; e++)
			{
				if (currentHost.Tracks[RailKey].Elements[e].WorldPosition != Vector3.Zero)
				{
					bool invalidElement = currentHost.Tracks[RailKey].Elements[e].InvalidElement;
					
					if (invalidElement && lastElementInvalid)
					{
						if(sections.Last().Count > 0) sections.Add(new List<Vector3>());
					}
					else
					{
						sections.Last().Add(new Vector3(currentHost.Tracks[RailKey].Elements[e].WorldPosition.X, currentHost.Tracks[RailKey].Elements[e].WorldPosition.Y + 0.5, currentHost.Tracks[RailKey].Elements[e].WorldPosition.Z));
					}
					lastElementInvalid = invalidElement;
				}
			}

			foreach (var points in sections)
			{
				if (points.Count < 2) continue;
				
				LibRenderVertex[] vertices = new LibRenderVertex[points.Count];
				for (int j = 0; j < points.Count; j++)
				{
					vertices[j] = new LibRenderVertex(new Vector3f((float)points[j].X, (float)points[j].Y, (float)points[j].Z))
					{
						Color = new Color128(Color.R / 255.0f, Color.G / 255.0f, Color.B / 255.0f, 1.0f)
					};
				}

				using (VertexArrayObject vao = new VertexArrayObject())
				{
					using (VertexBufferObject vbo = new VertexBufferObject(vertices, BufferUsageHint.StreamDraw))
					{
						vao.Bind();
						vbo.Bind();
						vbo.BufferData();
						vbo.SetAttribute(Renderer.DefaultShader.VertexLayout);
						
						Renderer.DefaultShader.Activate();
						Renderer.DefaultShader.SetCurrentProjectionMatrix(Renderer.CurrentProjectionMatrix);
						Renderer.DefaultShader.SetCurrentModelViewMatrix(Renderer.CurrentViewMatrix);
						Renderer.DefaultShader.SetIsLight(false);
						Renderer.DefaultShader.DisableTexturing();

						GL.LineWidth(LineWidth);
						GL.DrawArrays(PrimitiveType.LineStrip, 0, vertices.Length);
						GL.LineWidth(1.0f);
						
						Renderer.DefaultShader.Deactivate();
					}
				}
			}
		}


		public RailPath(HostInterface host, RendererCore renderer, int key, double blockLength, Color24 color)
		{
			currentHost = host;
			Renderer = renderer;
			RailKey = key;
			BlockLength = blockLength;
			Color = color;
			LineWidth = 2;
			Display = true;
			Description = host.Tracks[RailKey].Name;
		}

	}
}
