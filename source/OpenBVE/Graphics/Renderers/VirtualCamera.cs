//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2026, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
//ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
//ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using LibRender2;
using LibRender2.Objects;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Textures;
using OpenTK.Graphics.OpenGL;
using RouteManager2.VirtualCameras;
using Vector3 = OpenBveApi.Math.Vector3;

namespace OpenBve.Graphics.Renderers
{
	/// <summary>Renders the world as seen by virtual cameras defined in the route, and applies the result as textures onto materials marked as camera receivers.</summary>
	internal class VirtualCameraRenderer
	{
		private readonly BaseRenderer renderer;
		private readonly List<CameraFeed> feeds = new List<CameraFeed>();
		private VirtualCameraData[] lastCameras;
		private Texture blackTexture;

		// Reusable temporary collections (to avoid per-frame allocations)
		private readonly List<FaceState> visibleFaces = new List<FaceState>();
		private readonly List<FaceState> opaqueFaces = new List<FaceState>();
		private readonly List<FaceState> alphaFaces = new List<FaceState>();
		private readonly Dictionary<MeshMaterial[], HashSet<int>> receiverMaterials = new Dictionary<MeshMaterial[], HashSet<int>>();

		internal VirtualCameraRenderer(BaseRenderer renderer)
		{
			this.renderer = renderer;
		}

		/// <summary>Releases all OpenGL resources held by this renderer.</summary>
		internal void Reset()
		{
			foreach (CameraFeed feed in feeds)
			{
				feed.Dispose();
			}
			feeds.Clear();
			lastCameras = null;
			if (blackTexture != null && blackTexture.OpenGlTextures.Length > 0 && blackTexture.OpenGlTextures[0].Valid)
			{
				GL.DeleteTexture(blackTexture.OpenGlTextures[0].Name);
				blackTexture.OpenGlTextures[0].Valid = false;
			}
			blackTexture = null;
		}

		/// <summary>Evaluates which cameras are active, renders their feeds and applies them to the visible receiver materials.</summary>
		/// <param name="cameras">The virtual cameras defined in the current route.</param>
		/// <param name="trainTrackPosition">The current track position of the player's train.</param>
		/// <param name="stoppedAtStation">Whether the player's train is currently stopped at a station.</param>
		internal void Update(VirtualCameraData[] cameras, double trainTrackPosition, bool stoppedAtStation)
		{
			if (cameras == null || cameras.Length == 0)
			{
				if (feeds.Count > 0)
				{
					foreach (CameraFeed feed in feeds)
					{
						feed.Dispose();
					}
					feeds.Clear();
					lastCameras = null;
				}
				return;
			}

			// Recreate the render targets if the camera set has changed (e.g. a new route was loaded)
			if (!ReferenceEquals(lastCameras, cameras))
			{
				foreach (CameraFeed feed in feeds)
				{
					feed.Dispose();
				}
				feeds.Clear();
				foreach (VirtualCameraData camera in cameras)
				{
					CameraFeed feed = new CameraFeed(camera);
					feed.Create(this);
					feeds.Add(feed);
				}
				lastCameras = cameras;
			}

			// Collect the receiver materials from the currently visible faces
			lock (renderer.VisibleObjects.LockObject)
			{
				visibleFaces.Clear();
				visibleFaces.AddRange(renderer.VisibleObjects.OpaqueFaces);
				visibleFaces.AddRange(renderer.VisibleObjects.AlphaFaces);
				visibleFaces.AddRange(renderer.VisibleObjects.OverlayOpaqueFaces);
				visibleFaces.AddRange(renderer.VisibleObjects.OverlayAlphaFaces);

				opaqueFaces.Clear();
				opaqueFaces.AddRange(renderer.VisibleObjects.OpaqueFaces);

				alphaFaces.Clear();
				alphaFaces.AddRange(renderer.VisibleObjects.AlphaFaces);
			}

			receiverMaterials.Clear();
			HashSet<int> receiverIndexes = new HashSet<int>();
			foreach (FaceState face in visibleFaces)
			{
				if (face.Object == null || face.Object.Prototype == null || face.Object.Prototype.Mesh == null)
				{
					continue;
				}
				MeshFace faceData = face.Face;
				MeshMaterial[] materials = face.Object.Prototype.Mesh.Materials;
				if (materials == null || faceData.Material < 0 || faceData.Material >= materials.Length)
				{
					continue;
				}
				MeshMaterial material = materials[faceData.Material];
				if (material.CameraReceiverIndex > 0)
				{
					if (!receiverMaterials.TryGetValue(materials, out HashSet<int> indices))
					{
						indices = new HashSet<int>();
						receiverMaterials.Add(materials, indices);
					}
					if (indices.Add(faceData.Material))
					{
						receiverIndexes.Add(material.CameraReceiverIndex);
					}
				}
			}

			if (receiverMaterials.Count == 0)
			{
				return;
			}

			// Evaluate which cameras are active
			bool[] active = new bool[feeds.Count];
			for (int i = 0; i < feeds.Count; i++)
			{
				active[i] = EvaluateActivity(feeds[i].Camera, trainTrackPosition, stoppedAtStation);
			}

			// Render a feed for every active camera that is used by at least one visible receiver
			for (int i = 0; i < feeds.Count; i++)
			{
				feeds[i].Active = false;
				if (active[i] && receiverIndexes.Contains(feeds[i].Index))
				{
					RenderCameraView(feeds[i]);
					feeds[i].Active = true;
				}
			}

			// Apply the feeds to the receiver materials
			Texture off = GetBlackTexture();
			foreach (KeyValuePair<MeshMaterial[], HashSet<int>> pair in receiverMaterials)
			{
				MeshMaterial[] materials = pair.Key;
				foreach (int materialIndex in pair.Value)
				{
					MeshMaterial material = materials[materialIndex];
					Texture texture = off;
					for (int j = 0; j < feeds.Count; j++)
					{
						if (feeds[j].Index == material.CameraReceiverIndex && feeds[j].Active)
						{
							texture = feeds[j].DaytimeTexture;
							break;
						}
					}
					material.DaytimeTexture = texture;
					material.NighttimeTexture = texture;
					materials[materialIndex] = material;
				}
			}
		}

		/// <summary>Determines whether a camera should render a feed in the current state.</summary>
		private bool EvaluateActivity(VirtualCameraData camera, double trainTrackPosition, bool stoppedAtStation)
		{
			switch (camera.ActiveMode)
			{
				case VirtualCameraActiveMode.StopOnly:
					return stoppedAtStation && Math.Abs(trainTrackPosition - camera.Position.Z) <= camera.ActivationDistance;
				case VirtualCameraActiveMode.Distance:
					return Math.Abs(trainTrackPosition - camera.Position.Z) <= camera.ActivationDistance;
				case VirtualCameraActiveMode.Always:
				default:
					return true;
			}
		}

		/// <summary>Renders the world as seen by a single virtual camera into its framebuffer.</summary>
		private void RenderCameraView(CameraFeed feed)
		{
			if (!feed.FramebufferComplete)
			{
				return;
			}
			VirtualCameraData camera = feed.Camera;

			// Compute the camera orientation from yaw / pitch / roll (all in radians)
			Vector3 direction = new Vector3(Math.Cos(camera.Pitch) * Math.Sin(camera.Yaw), Math.Sin(camera.Pitch), Math.Cos(camera.Pitch) * Math.Cos(camera.Yaw));
			direction.Normalize();
			Vector3 side = Vector3.Cross(direction, new Vector3(0.0, 1.0, 0.0));
			if (side.NormSquared() < 1e-9)
			{
				side = new Vector3(1.0, 0.0, 0.0);
			}
			side.Normalize();
			Vector3 up = Vector3.Cross(side, direction);
			up.Normalize();
			if (camera.Roll != 0.0)
			{
				double cosRoll = Math.Cos(camera.Roll);
				double sinRoll = Math.Sin(camera.Roll);
				up = up * cosRoll + side * sinRoll;
				up.Normalize();
			}

			// Save the renderer state we are about to modify
			Matrix4D savedProjection = renderer.CurrentProjectionMatrix;
			Matrix4D savedView = renderer.CurrentViewMatrix;
			Matrix4D savedTranslation = renderer.Camera.TranslationMatrix;
			int[] savedViewport = new int[4];
			GL.GetInteger(GetPName.Viewport, savedViewport);

			// Apply the virtual camera state
			// The view matrix is mirrored vertically so that the feed appears upright when sampled,
			// as OpenBVE stores textures with V=0 at the top of the image.
			renderer.Camera.TranslationMatrix = Matrix4D.CreateTranslation(-camera.Position.X, -camera.Position.Y, camera.Position.Z);
			Matrix4D viewMatrix = Matrix4D.LookAt(Vector3.Zero, new Vector3(direction.X, direction.Y, -direction.Z), new Vector3(up.X, up.Y, -up.Z));
			viewMatrix = Matrix4D.Scale(1.0, -1.0, 1.0) * viewMatrix;
			renderer.CurrentViewMatrix = viewMatrix;
			double aspect = (double)camera.RenderWidth / (double)camera.RenderHeight;
			renderer.CurrentProjectionMatrix = Matrix4D.CreatePerspectiveFieldOfView(camera.FieldOfView, aspect, 0.05, 1000.0);

			// Bind the framebuffer and clear it
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, feed.Framebuffer);
			GL.DrawBuffers(1, new[] { DrawBuffersEnum.ColorAttachment0 });
			GL.Viewport(0, 0, feed.Width, feed.Height);
			GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
			GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

			// Set up the shader
			renderer.DefaultShader.Activate();
			renderer.ResetShader(renderer.DefaultShader);
			renderer.DefaultShader.SetCurrentProjectionMatrix(renderer.CurrentProjectionMatrix);

			// The mirrored view matrix reverses triangle winding, so cull the back faces instead of the front faces
			renderer.ResetOpenGlState();
			GL.CullFace(CullFaceMode.Back);

			Vector3 lightPosition = new Vector3(renderer.Lighting.OptionLightPosition.X, renderer.Lighting.OptionLightPosition.Y, -renderer.Lighting.OptionLightPosition.Z);
			lightPosition.Transform(viewMatrix);
			if (renderer.OptionLighting)
			{
				renderer.DefaultShader.SetIsLight(true);
				renderer.DefaultShader.SetLightPosition(lightPosition);
				renderer.DefaultShader.SetLightAmbient(renderer.Lighting.OptionAmbientColor);
				renderer.DefaultShader.SetLightDiffuse(renderer.Lighting.OptionDiffuseColor);
				renderer.DefaultShader.SetLightSpecular(renderer.Lighting.OptionSpecularColor);
				renderer.DefaultShader.SetLightModel(renderer.Lighting.LightModel);
			}
			else
			{
				renderer.DefaultShader.SetIsLight(false);
			}
			renderer.DefaultShader.SetTexture(0);

			// Render the scene
			foreach (FaceState face in opaqueFaces)
			{
				face.Draw();
			}

			renderer.SetBlendFunc();
			renderer.SetAlphaFunc(AlphaFunction.Greater, 0.0f);
			GL.DepthMask(false);
			foreach (FaceState face in alphaFaces)
			{
				face.Draw();
			}
			GL.DepthMask(true);
			renderer.UnsetBlendFunc();

			// Restore the renderer state
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
			GL.Viewport(savedViewport[0], savedViewport[1], savedViewport[2], savedViewport[3]);
			renderer.CurrentProjectionMatrix = savedProjection;
			renderer.CurrentViewMatrix = savedView;
			renderer.Camera.TranslationMatrix = savedTranslation;
			renderer.LastBoundTexture = null;
		}

		/// <summary>Returns a 1x1 black texture used to display receiver screens when their camera is not active.</summary>
		private Texture GetBlackTexture()
		{
			if (blackTexture == null)
			{
				byte[] black = new byte[] { 0, 0, 0, 255 };
				blackTexture = new Texture(1, 1, OpenBveApi.Textures.PixelFormat.RGBAlpha, black, null);
				int handle;
				GL.GenTextures(1, out handle);
				GL.BindTexture(TextureTarget.Texture2D, handle);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 1, 1, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, black);
				GL.BindTexture(TextureTarget.Texture2D, 0);
				BindGlTextureName(blackTexture, handle);
			}
			return blackTexture;
		}

		/// <summary>Points all wrap mode entries of a texture at an existing OpenGL texture name.</summary>
		private void BindGlTextureName(Texture texture, int handle)
		{
			OpenGlTexture[] glTextures = texture.OpenGlTextures;
			for (int i = 0; i < glTextures.Length; i++)
			{
				glTextures[i].Name = handle;
				glTextures[i].Valid = true;
			}
		}

		/// <summary>Represents a single camera feed render target.</summary>
		private sealed class CameraFeed : IDisposable
		{
			internal readonly VirtualCameraData Camera;
			internal readonly int Index;
			internal readonly int Width;
			internal readonly int Height;
			internal int Framebuffer;
			internal int ColorTexture;
			internal int DepthBuffer;
			internal bool FramebufferComplete;
			internal Texture DaytimeTexture;
			internal Texture NighttimeTexture;
			internal bool Active;
			private bool disposed;

			internal CameraFeed(VirtualCameraData camera)
			{
				Camera = camera;
				Index = camera.Index;
				Width = camera.RenderWidth;
				Height = camera.RenderHeight;
			}

			internal void Create(VirtualCameraRenderer owner)
			{
				GL.GenFramebuffers(1, out Framebuffer);
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);

				GL.GenTextures(1, out ColorTexture);
				GL.BindTexture(TextureTarget.Texture2D, ColorTexture);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, Width, Height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
				GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, ColorTexture, 0);

				GL.GenRenderbuffers(1, out DepthBuffer);
				GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, DepthBuffer);
				GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, Width, Height);
				GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, DepthBuffer);

				FramebufferComplete = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete;
				GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

				// The texture wrappers are kept per camera so that the renderer's LastBoundTexture
				// caching remains stable across frames; the actual image is updated in place each frame.
				byte[] black = new byte[] { 0, 0, 0, 255 };
				DaytimeTexture = new Texture(1, 1, OpenBveApi.Textures.PixelFormat.RGBAlpha, black, null);
				NighttimeTexture = new Texture(1, 1, OpenBveApi.Textures.PixelFormat.RGBAlpha, black, null);
				owner.BindGlTextureName(DaytimeTexture, ColorTexture);
				owner.BindGlTextureName(NighttimeTexture, ColorTexture);
			}

			public void Dispose()
			{
				if (!disposed)
				{
					if (ColorTexture != 0)
					{
						GL.DeleteTexture(ColorTexture);
					}
					if (DepthBuffer != 0)
					{
						GL.DeleteRenderbuffer(DepthBuffer);
					}
					if (Framebuffer != 0)
					{
						GL.DeleteFramebuffer(Framebuffer);
					}
					disposed = true;
				}
			}
		}
	}
}
