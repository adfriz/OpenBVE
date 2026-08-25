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
//LOSS OF USE, DATA, OR PROFITS; HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
//WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
//ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
//POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using LibRender2;
using LibRender2.Objects;
using LibRender2.Trains;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Textures;
using OpenBveApi.Trains;
using OpenTK.Graphics.OpenGL;
using RouteManager2.VirtualCameras;
using TrainManager;
using TrainManager.Car;
using TrainManager.Trains;
using Vector3 = OpenBveApi.Math.Vector3;

namespace OpenBve.Graphics.Renderers
{
	/// <summary>Renders the world as seen by virtual cameras defined in the route, and applies the result as textures onto materials marked as camera receivers.</summary>
	/// <remarks>
	/// All currently relevant camera feeds are packed into sub-tiles of a single shared atlas framebuffer using a shelf packer,
	/// so that receivers sample their feed directly from one large texture. The atlas auto-scales from 1024 upwards (2048, 4096)
	/// only when the active feeds do not fit, and shrinks back down when possible. Only cameras which are active (Always /
	/// StopOnly / Distance) AND referenced by at least one visible receiver occupy atlas space, so distant CCTV costs nothing.
	/// </remarks>
	internal class VirtualCameraRenderer
	{
		/// <summary>The side length of the smallest atlas created</summary>
		private const int MinimumAtlasSize = 1024;
		/// <summary>The side length of the largest atlas created</summary>
		private const int MaximumAtlasSize = 4096;

		private readonly BaseRenderer renderer;
		private readonly List<CameraFeed> feeds = new List<CameraFeed>();

		// Shared camera atlas render target
		private int atlasFramebuffer;
		private int atlasColorTexture;
		private int atlasDepthBuffer;
		private int atlasSize;
		/// <summary>Whether an atlas currently exists</summary>
		private bool atlasCreated;
		/// <summary>The feeds packed into the current atlas layout</summary>
		private readonly List<CameraFeed> packedFeeds = new List<CameraFeed>();
		/// <summary>A hash of the indices and resolutions of the packed feeds, used to detect layout changes cheaply</summary>
		private long packedSignature;

		/// <summary>The texture wrapper handed to receiver materials; points at the shared atlas texture</summary>
		private Texture atlasTexture;

		private Texture blackTexture;

		// Reusable temporary collections (to avoid per-frame allocations)
		private readonly List<FaceState> visibleFaces = new List<FaceState>();
		private readonly List<FaceState> opaqueFaces = new List<FaceState>();
		private readonly List<FaceState> alphaFaces = new List<FaceState>();
		private readonly Dictionary<MeshMaterial[], HashSet<int>> receiverMaterials = new Dictionary<MeshMaterial[], HashSet<int>>();
		/// <summary>Every receiver material ever seen, so that stale feeds can be reset before rendering into the atlas (avoids read/write feedback)</summary>
		private readonly Dictionary<MeshMaterial[], HashSet<int>> allReceiverMaterials = new Dictionary<MeshMaterial[], HashSet<int>>();
		/// <summary>The feeds required for the current frame (reused list)</summary>
		private readonly List<CameraFeed> requiredFeeds = new List<CameraFeed>();
		/// <summary>The receiver indexes referenced by visible faces (reused set)</summary>
		private readonly HashSet<int> receiverIndexes = new HashSet<int>();
		/// <summary>Scratch storage for the merged camera set (reused list)</summary>
		private readonly List<VirtualCameraData> mergedScratch = new List<VirtualCameraData>();
		/// <summary>Object pool for merged camera data instances</summary>
		private readonly List<VirtualCameraData> dataPool = new List<VirtualCameraData>();
		/// <summary>The number of pool entries used by the current frame's merged set</summary>
		/// <summary>Reusable draw buffers specification for framebuffer rendering</summary>
		private static readonly DrawBuffersEnum[] ColorAttachment0 = new[] { DrawBuffersEnum.ColorAttachment0 };
		/// <summary>Scratch array for saving the current viewport</summary>
		private readonly int[] viewportScratch = new int[4];

		internal VirtualCameraRenderer(BaseRenderer renderer)
		{
			this.renderer = renderer;
		}

		/// <summary>Releases all OpenGL resources held by this renderer.</summary>
		internal void Reset()
		{
			feeds.Clear();

			packedFeeds.Clear();
			packedSignature = 0;
			DisposeAtlas();
			if (blackTexture != null && blackTexture.OpenGlTextures.Length > 0 && blackTexture.OpenGlTextures[0].Valid)
			{
				GL.DeleteTexture(blackTexture.OpenGlTextures[0].Name);
				blackTexture.OpenGlTextures[0].Valid = false;
			}
			blackTexture = null;
			renderer.CameraAtlasRects.Clear();
			allReceiverMaterials.Clear();
			receiverIndexes.Clear();
		}

		/// <summary>Disposes the shared atlas framebuffer and texture.</summary>
		private void DisposeAtlas()
		{
			if (atlasFramebuffer != 0)
			{
				GL.DeleteFramebuffer(atlasFramebuffer);
				atlasFramebuffer = 0;
			}
			if (atlasColorTexture != 0)
			{
				GL.DeleteTexture(atlasColorTexture);
				atlasColorTexture = 0;
			}
			if (atlasDepthBuffer != 0)
			{
				GL.DeleteRenderbuffer(atlasDepthBuffer);
				atlasDepthBuffer = 0;
			}
			atlasTexture = null;
			atlasCreated = false;
		}

		/// <summary>Evaluates which cameras are active, renders their feeds and applies them to the visible receiver materials.</summary>
		/// <param name="cameras">The virtual cameras defined in the current route.</param>
		/// <param name="playerTrain">The player's train, from which cameras attached to individual cars are collected.</param>
		/// <param name="trainTrackPosition">The current track position of the player's train.</param>
		/// <param name="stoppedAtStation">Whether the player's train is currently stopped at a station.</param>
		/// <param name="timeElapsed">The time elapsed since the last frame, used to throttle feed updates by FeedFPS.</param>
		internal void Update(VirtualCameraData[] cameras, TrainBase playerTrain, double trainTrackPosition, bool stoppedAtStation, double timeElapsed)
		{
			// Build the merged camera set for this frame: route cameras plus any cameras attached to the player train.
			// A camera attached to the train overrides a route camera with the same index. Fills mergedScratch.
			int mergedCount = MergeCameras(cameras, playerTrain);

			if (mergedCount == 0)
			{
				if (feeds.Count > 0 || atlasCreated)
				{
					feeds.Clear();
						packedFeeds.Clear();
					packedSignature = 0;
					DisposeAtlas();
					renderer.CameraAtlasRects.Clear();
				}
				return;
			}

			// Rebuild the feed entries if the camera set has changed (e.g. a new route was loaded, or the train changed)
			bool sameSet = feeds.Count == mergedCount;
			if (sameSet)
			{
				for (int i = 0; i < mergedCount; i++)
				{
					VirtualCameraData m = mergedScratch[i];
					if (feeds[i].Index != m.Index || feeds[i].TileWidth != Math.Max(1, m.RenderWidth) || feeds[i].TileHeight != Math.Max(1, m.RenderHeight))
					{
						sameSet = false;
						break;
					}
				}
			}
			if (!sameSet)
			{
				feeds.Clear();
				packedFeeds.Clear();
				packedSignature = 0;
				for (int i = 0; i < mergedCount; i++)
				{
					feeds.Add(new CameraFeed(mergedScratch[i]));
				}
			}
			else
			{
				// Refresh the camera data in place so that attached cameras track the current car position and orientation
				for (int i = 0; i < feeds.Count && i < mergedCount; i++)
				{
					feeds[i].Camera = mergedScratch[i];
				}
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
						// Also track it persistently so it can be reset on frames where it is not visible
						if (!allReceiverMaterials.TryGetValue(materials, out HashSet<int> allIndices))
						{
							allIndices = new HashSet<int>();
							allReceiverMaterials.Add(materials, allIndices);
						}
						allIndices.Add(faceData.Material);
					}
				}
			}

			// Determine the feeds actually required this frame: active cameras referenced by at least one visible receiver.
			// Everything else occupies no atlas space and costs no render time.
			requiredFeeds.Clear();
			for (int i = 0; i < feeds.Count; i++)
			{
				feeds[i].Active = false;
				if (EvaluateActivity(feeds[i].Camera, trainTrackPosition, stoppedAtStation) && receiverIndexes.Contains(feeds[i].Index))
				{
					requiredFeeds.Add(feeds[i]);
				}
			}

			// Nothing to render this frame: release the atlas entirely so the feature costs nothing
			if (requiredFeeds.Count == 0)
			{
				if (atlasCreated || packedFeeds.Count > 0)
				{
					packedFeeds.Clear();
					packedSignature = 0;
					DisposeAtlas();
					renderer.CameraAtlasRects.Clear();
				}
				return;
			}

			// Reset every known receiver material to black BEFORE touching the atlas.
			// Receiver textures point at the atlas itself once applied, so they must never be bound
			// while the atlas framebuffer is active (read/write feedback loop).
			Texture off = GetBlackTexture();
			foreach (KeyValuePair<MeshMaterial[], HashSet<int>> pair in allReceiverMaterials)
			{
				MeshMaterial[] materials = pair.Key;
				foreach (int materialIndex in pair.Value)
				{
					MeshMaterial material = materials[materialIndex];
					material.DaytimeTexture = off;
					material.NighttimeTexture = off;
					materials[materialIndex] = material;
				}
			}

			// While rendering the camera views, no receiver may remap its UVs onto the atlas
			renderer.CameraAtlasRects.Clear();

			// Re-pack the atlas if the set of required feeds has changed
			long signature = ComputeSignature(requiredFeeds);
			if (signature != packedSignature)
			{
				PackAtlas(requiredFeeds, signature);
			}

			// Render every packed feed, throttled by the camera's FeedFPS setting. When a feed is not updated
			// this frame it still displays its most recently rendered image.
			for (int i = 0; i < packedFeeds.Count; i++)
			{
				CameraFeed feed = packedFeeds[i];
				if (!feed.Packed)
				{
					continue;
				}
				feed.SecondsSinceLastRender += Math.Max(0.0, timeElapsed);
				double interval = 1.0 / (double)Math.Max(1, feed.Camera.FeedFPS);
				if (feed.SecondsSinceLastRender >= interval)
				{
					RenderCameraView(feed);
					feed.SecondsSinceLastRender = feed.SecondsSinceLastRender - interval;
				}
				feed.Active = true;
			}

			// Unbind the shared atlas framebuffer before the main scene is rendered
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

			// Publish the atlas sub-rectangles of the packed feeds so that receiver faces
			// sample their own tile of the shared atlas texture
			renderer.CameraAtlasRects.Clear();
			for (int i = 0; i < packedFeeds.Count; i++)
			{
				renderer.CameraAtlasRects[packedFeeds[i].Index] = packedFeeds[i].AtlasRect;
			}

			// Apply the atlas texture to the receiver materials of required feeds that are actually packed;
			// feeds dropped due to atlas overflow stay black
			foreach (KeyValuePair<MeshMaterial[], HashSet<int>> pair in receiverMaterials)
			{
				MeshMaterial[] materials = pair.Key;
				foreach (int materialIndex in pair.Value)
				{
					MeshMaterial material = materials[materialIndex];
					Texture texture = off;
					for (int j = 0; j < requiredFeeds.Count; j++)
					{
						if (requiredFeeds[j].Index == material.CameraReceiverIndex && requiredFeeds[j].Packed)
						{
							texture = atlasTexture;
							break;
						}
					}
					material.DaytimeTexture = texture;
					material.NighttimeTexture = texture;
					materials[materialIndex] = material;
				}
			}
		}

		/// <summary>Computes a cheap order-independent hash over the indices and resolutions of the supplied feeds.</summary>
		private static long ComputeSignature(List<CameraFeed> list)
		{
			ulong hash = 14695981039346656037UL;
			for (int i = 0; i < list.Count; i++)
			{
				unchecked
				{
					hash ^= (ulong)((long)list[i].Index * 397L + list[i].TileWidth * 31L + list[i].TileHeight);
					hash *= 1099511628211UL;
				}
			}
			return unchecked((long)hash);
		}

		/// <summary>Packs the supplied feeds into the smallest suitable atlas (1024, 2048 or 4096 px squared) using a shelf packer.</summary>
		/// <remarks>Tiles are placed in rows sorted by descending height, which keeps wasted space minimal. When the feeds
		/// cannot fit into the maximum atlas size, the largest tiles are dropped until the remainder fits.</remarks>
		private void PackAtlas(List<CameraFeed> list, long signature)
		{
			// Mark everything unpacked first
			for (int i = 0; i < packedFeeds.Count; i++)
			{
				packedFeeds[i].Packed = false;
				packedFeeds[i].Active = false;
			}
			packedFeeds.Clear();

			// Sort a working copy by tile height (descending), the order the shelf packer consumes
			List<CameraFeed> sorted = new List<CameraFeed>(list);
			sorted.Sort((a, b) =>
			{
				int result = b.TileHeight.CompareTo(a.TileHeight);
				return result != 0 ? result : b.TileWidth.CompareTo(a.TileWidth);
			});

			int size = MinimumAtlasSize;
			bool fits = false;
			while (!fits)
			{
				fits = TryPack(sorted, size);
				if (!fits)
				{
					if (size >= MaximumAtlasSize)
						{
							// Drop the largest remaining tile and retry within the maximum atlas size,
							// so that the numerous small feeds are preserved
							if (sorted.Count > 1)
							{
								sorted.RemoveAt(0);
							}
							else
							{
								break;
							}
						}
					else
					{
						size *= 2;
					}
				}
			}

			// Recreate the render target only when the atlas dimensions actually change
			if (!atlasCreated || atlasSize != size)
			{
				DisposeAtlas();
				CreateRenderTarget(size);
			}

			// Clear the whole atlas so stale pixels from the previous layout never leak into new tiles,
			// then force an immediate re-render of every packed feed
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, atlasFramebuffer);
			GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
			GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

			double defaultInterval = 1.0 / 24.0;
			foreach (CameraFeed feed in packedFeeds)
			{
				feed.Packed = true;
				feed.SecondsSinceLastRender = defaultInterval;
			}

			packedSignature = signature;
		}

		/// <summary>Attempts to shelf-pack the sorted feeds into an atlas of the given size. On success the tile assignments are stored.</summary>
		private bool TryPack(List<CameraFeed> sorted, int size)
		{
			int x = 0, y = 0, rowHeight = 0;
			foreach (CameraFeed feed in sorted)
			{
				if (feed.TileWidth > size || feed.TileHeight > size)
				{
					return false;
				}
				if (x + feed.TileWidth > size)
				{
					x = 0;
					y += rowHeight;
					rowHeight = 0;
				}
				if (y + feed.TileHeight > size)
				{
					return false;
				}
				x += feed.TileWidth;
				if (feed.TileHeight > rowHeight)
				{
					rowHeight = feed.TileHeight;
				}
			}

			// The layout fits: commit the placements
			x = 0;
			y = 0;
			rowHeight = 0;
			const double inset = 0.5;
			foreach (CameraFeed feed in sorted)
			{
				if (x + feed.TileWidth > size)
				{
					x = 0;
					y += rowHeight;
					rowHeight = 0;
				}
				feed.TileX = x;
				feed.TileY = y;
				feed.AtlasRect = new Vector4(
					(x + inset) / size,
					(y + inset) / size,
					(feed.TileWidth - 2 * inset) / size,
					(feed.TileHeight - 2 * inset) / size);
				x += feed.TileWidth;
				if (feed.TileHeight > rowHeight)
				{
					rowHeight = feed.TileHeight;
				}
				packedFeeds.Add(feed);
			}
			atlasSize = size;
			return true;
		}

		/// <summary>Creates the shared atlas render target of the given size.</summary>
		private void CreateRenderTarget(int size)
		{
			atlasSize = size;
			GL.GenFramebuffers(1, out atlasFramebuffer);
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, atlasFramebuffer);

			GL.GenTextures(1, out atlasColorTexture);
			GL.BindTexture(TextureTarget.Texture2D, atlasColorTexture);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, size, size, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, atlasColorTexture, 0);

			GL.GenRenderbuffers(1, out atlasDepthBuffer);
			GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, atlasDepthBuffer);
			GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, size, size);
			GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, atlasDepthBuffer);

			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

			// The texture wrapper handed to receiver materials points at the shared atlas image
			byte[] black = new byte[] { 0, 0, 0, 255 };
			atlasTexture = new Texture(size, size, OpenBveApi.Textures.PixelFormat.RGBAlpha, black, null);
			BindGlTextureName(atlasTexture, atlasColorTexture);

			atlasCreated = true;
		}

		/// <summary>Determines whether a camera should render a feed in the current state.</summary>
		private bool EvaluateActivity(VirtualCameraData camera, double trainTrackPosition, bool stoppedAtStation)
		{
			// A camera attached to the train always sits at the train's own track position
			double cameraTrackPosition = camera.AttachedToTrain ? camera.TrackPosition : camera.Position.Z;
			switch (camera.ActiveMode)
			{
				case VirtualCameraActiveMode.StopOnly:
					return stoppedAtStation && Math.Abs(trainTrackPosition - cameraTrackPosition) <= camera.ActivationDistance;
				case VirtualCameraActiveMode.Distance:
					return Math.Abs(trainTrackPosition - cameraTrackPosition) <= camera.ActivationDistance;
				case VirtualCameraActiveMode.Always:
				default:
					return true;
			}
		}

		/// <summary>Merges the route cameras with the cameras attached to the player's train into mergedScratch. Attached cameras override route cameras with the same index.</summary>
		/// <returns>The number of merged cameras.</returns>
		private int MergeCameras(VirtualCameraData[] cameras, TrainBase playerTrain)
		{
			mergedScratch.Clear();
			int attachedCount = BuildAttachedCameras(playerTrain);
			bool hasRoute = cameras != null && cameras.Length > 0;
			if (!hasRoute && attachedCount == 0)
			{
				return 0;
			}
			if (hasRoute)
			{
				for (int i = 0; i < cameras.Length; i++)
				{
					VirtualCameraData camera = cameras[i];
					bool overridden = false;
					for (int j = 0; j < attachedCount; j++)
					{
						if (mergedScratch[j].Index == camera.Index)
						{
							overridden = true;
							break;
						}
					}
					if (!overridden)
					{
						mergedScratch.Add(camera);
					}
				}
			}
			return mergedScratch.Count;
		}

		/// <summary>Collects the virtual cameras defined in the player train's car sections into mergedScratch, computing their world position and orientation for the current frame. Pooled, allocation-free.</summary>
		/// <returns>The number of attached cameras added to mergedScratch.</returns>
		private int BuildAttachedCameras(TrainBase playerTrain)
		{
			if (playerTrain == null || playerTrain.Cars == null)
			{
				return 0;
			}
			int count = 0;
			for (int c = 0; c < playerTrain.Cars.Length; c++)
			{
				CarBase car = playerTrain.Cars[c];
				if (car == null || car.CarSections == null)
				{
					continue;
				}
				foreach (var pair in car.CarSections)
				{
					CarSection section = pair.Value;
					if (section == null || section.VirtualCameras == null)
					{
						continue;
					}
					for (int i = 0; i < section.VirtualCameras.Length; i++)
					{
						AnimatedVirtualCamera vcam = section.VirtualCameras[i];
						if (vcam == null)
						{
							continue;
						}
						// Convert the car-frame offset into a world position and heading
						car.CreateWorldCoordinates(vcam.Offset, out Vector3 position, out Vector3 direction);
						double worldYaw = Math.Atan2(direction.X, direction.Z);
						double worldPitch = Math.Asin(Math.Max(-1.0, Math.Min(1.0, direction.Y)));
						VirtualCameraData data;
						if (count < dataPool.Count)
						{
							data = dataPool[count];
						}
						else
						{
							data = new VirtualCameraData();
							dataPool.Add(data);
						}
						count++;
						data.Index = vcam.Index;
						data.Position = position;
						data.Yaw = worldYaw + vcam.Yaw;
						data.Pitch = worldPitch + vcam.Pitch;
						data.Roll = vcam.Roll;
						data.FieldOfView = vcam.FieldOfView;
						data.RenderWidth = vcam.RenderWidth;
						data.RenderHeight = vcam.RenderHeight;
						data.ActiveMode = (VirtualCameraActiveMode)vcam.ActiveMode;
						data.ActivationDistance = vcam.ActivationDistance;
						data.FeedFPS = vcam.FeedFPS;
						data.AttachedToTrain = true;
						data.CarIndex = c;
						data.Offset = vcam.Offset;
						data.TrackPosition = car.TrackPosition;
						mergedScratch.Add(data);
					}
				}
			}
			return count;
		}

		/// <summary>Renders the world as seen by a single virtual camera into its tile of the shared atlas framebuffer.</summary>
		private void RenderCameraView(CameraFeed feed)
		{
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
			int[] savedViewport = viewportScratch;
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

			// Bind the shared atlas framebuffer and clear this camera's tile
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, atlasFramebuffer);
			GL.DrawBuffers(1, ColorAttachment0);
			GL.Viewport(feed.TileX, feed.TileY, feed.TileWidth, feed.TileHeight);
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

		/// <summary>Represents a single virtual camera feed occupying one tile of the shared camera atlas.</summary>
		private sealed class CameraFeed
		{
			internal VirtualCameraData Camera;
			/// <summary>The unique index of the underlying camera</summary>
			internal readonly int Index;
			/// <summary>The width of this feed's tile in pixels</summary>
			internal readonly int TileWidth;
			/// <summary>The height of this feed's tile in pixels</summary>
			internal readonly int TileHeight;
			/// <summary>The tile origin within the atlas, in pixels</summary>
			internal int TileX;
			internal int TileY;
			/// <summary>The normalized sub-rectangle of this feed within the atlas (xy = offset, zw = scale)</summary>
			internal Vector4 AtlasRect;
			/// <summary>Whether this feed currently occupies a slot in the atlas</summary>
			internal bool Packed;
			internal double SecondsSinceLastRender;
			internal bool Active;

			internal CameraFeed(VirtualCameraData camera)
			{
				Camera = camera;
				Index = camera.Index;
				TileWidth = Math.Max(1, camera.RenderWidth);
				TileHeight = Math.Max(1, camera.RenderHeight);
				AtlasRect = new Vector4(0.0, 0.0, 1.0, 1.0);
			}
		}
	}
}
