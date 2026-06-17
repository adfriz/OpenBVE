using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using LibRender2.Textures;
using OpenBveApi.Graphics;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Routes;
using OpenBveApi.Textures;

namespace LibRender2.Objects
{
	public class VisibleObjectLibrary
	{
		private readonly BaseRenderer renderer;

		public readonly QuadTree quadTree;

		public readonly ConcurrentDictionary<ObjectState, byte> Objects;
		private readonly List<FaceState> myOpaqueFaces;
		private readonly List<FaceState> myAlphaFaces;
		private readonly List<FaceState> myOverlayOpaqueFaces;
		private List<FaceState> myOverlayAlphaFaces;
		public readonly ReadOnlyCollection<FaceState> OpaqueFaces;  // StaticOpaque and DynamicOpaque
		public readonly ReadOnlyCollection<FaceState> OverlayOpaqueFaces;
		public readonly ReadOnlyCollection<FaceState> AlphaFaces;  // DynamicAlpha
		public ReadOnlyCollection<FaceState> OverlayAlphaFaces;

		public readonly object LockObject = new object();

		private double[] distanceBuffer = new double[1024];
		private FaceState[] sortedFacesBuffer = new FaceState[1024];

		internal VisibleObjectLibrary(BaseRenderer Renderer)
		{
			renderer = Renderer;
			// Note: .Net has no Concurrent HashSet, so use a dictionary with a byte value instead
			// previous approach used a List and Contains()
			Objects = new ConcurrentDictionary<ObjectState, byte>();
			myOpaqueFaces = new List<FaceState>();
			myAlphaFaces = new List<FaceState>();
			myOverlayOpaqueFaces = new List<FaceState>();
			myOverlayAlphaFaces = new List<FaceState>();

			OpaqueFaces = myOpaqueFaces.AsReadOnly();
			AlphaFaces = myAlphaFaces.AsReadOnly();
			OverlayOpaqueFaces = myOverlayOpaqueFaces.AsReadOnly();
			OverlayAlphaFaces = myOverlayAlphaFaces.AsReadOnly();
			quadTree = new QuadTree(renderer.currentOptions.ViewingDistance);
		}

		private bool AddObject(ObjectState state)
		{
			return state.Prototype != null && Objects.TryAdd(state, 0);
		}

		private void RemoveObject(ObjectState state)
		{
			lock (LockObject)
			{
				if (Objects.TryRemove(state, out _))
				{
					myOpaqueFaces.RemoveAll(x => x.Object == state);
					myAlphaFaces.RemoveAll(x => x.Object == state);
					myOverlayOpaqueFaces.RemoveAll(x => x.Object == state);
					myOverlayAlphaFaces.RemoveAll(x => x.Object == state);
				}	
			}
			
		}

		public void Clear()
		{
			lock (LockObject)
			{
				Objects.Clear();
				myOpaqueFaces.Clear();
				myAlphaFaces.Clear();
				myOverlayOpaqueFaces.Clear();
				myOverlayAlphaFaces.Clear();
				renderer.StaticObjectStates.Clear();
				renderer.DynamicObjectStates.Clear();
			}
		}

		public void ShowObject(ObjectState State, ObjectType Type)
		{
			bool result = AddObject(State);
			
			if (!result)
			{
				return;
			}

			foreach (MeshFace face in State.Prototype.Mesh.Faces)
			{
				OpenGlTextureWrapMode wrap = OpenGlTextureWrapMode.ClampClamp;

				if (State.Prototype.Mesh.Materials[face.Material].DaytimeTexture != null || State.Prototype.Mesh.Materials[face.Material].NighttimeTexture != null)
				{
					if (State.Prototype.Mesh.Materials[face.Material].WrapMode == null)
					{
						/*
						 * If the object does not have a stored wrapping mode determine it now. However:
						 * https://github.com/leezer3/OpenBVE/issues/971
						 *
						 * Unfortunately, there appear to be X objects in the wild which expect a non-default wrapping mode
						 * which means the best fast exit we can do is to check for RepeatRepeat....
						 *
						 */
						for (int i = 0; i < face.Vertices.Length; i++)
						{
							int v = face.Vertices[i].Index;
							if (State.Prototype.Mesh.Vertices[v].TextureCoordinates.X < 0.0f || State.Prototype.Mesh.Vertices[v].TextureCoordinates.X > 1.0f)
							{
								wrap |= OpenGlTextureWrapMode.RepeatClamp;
							}

							if (State.Prototype.Mesh.Vertices[v].TextureCoordinates.Y < 0.0f || State.Prototype.Mesh.Vertices[v].TextureCoordinates.Y > 1.0f)
							{
								wrap |= OpenGlTextureWrapMode.ClampRepeat;
							}

							if (wrap == OpenGlTextureWrapMode.RepeatRepeat)
							{
								break;
							}
						}
						State.Prototype.Mesh.Materials[face.Material].WrapMode = wrap;
					}
				}

				bool alpha = false;

				if (Type == ObjectType.Overlay && renderer.Camera.CurrentRestriction != CameraRestrictionMode.NotAvailable)
				{
					alpha = true;
				}
				else if (State.Prototype.Mesh.Materials[face.Material].Color.A != 255)
				{
					alpha = true;
				}
				else if (State.Prototype.Mesh.Materials[face.Material].BlendMode == MeshMaterialBlendMode.Additive)
				{
					alpha = true;
				}
				else if (State.Prototype.Mesh.Materials[face.Material].GlowAttenuationData != 0)
				{
					alpha = true;
				}
				else
				{
					if (State.Prototype.Mesh.Materials[face.Material].DaytimeTexture != null)
					{
						if (CheckTextureTransparency(State.Prototype.Mesh.Materials[face.Material].DaytimeTexture.Origin))
						{
							alpha = true;
						}
					}

					if (State.Prototype.Mesh.Materials[face.Material].NighttimeTexture != null)
					{
						if (CheckTextureTransparency(State.Prototype.Mesh.Materials[face.Material].NighttimeTexture.Origin))
						{
							alpha = true;
						}
					}
				}
				
				List<FaceState> list;

				switch (Type)
				{
					case ObjectType.Static:
					case ObjectType.Dynamic:
						list = alpha ? myAlphaFaces : myOpaqueFaces;
						break;
					case ObjectType.Overlay:
						list = alpha ? myOverlayAlphaFaces : myOverlayOpaqueFaces;
						break;
					default:
						throw new ArgumentOutOfRangeException(nameof(Type), Type, null);
				}

				lock (LockObject)
				{
					if (!alpha)
					{
						/*
						 * If an opaque face, itinerate through the list to see if the prototype is present in the list
						 * When the new renderer is in use, this prevents re-binding the VBO as it is simply re-drawn with
						 * a different translation matrix
						 * NOTE: The shader isn't currently smart enough to do depth discards, so if this changes may need to
						 * be revisited
						 */
						if (list.Count == 0)
						{
							list.Add(new FaceState(State, face, renderer));
						}
						else
						{
							for (int i = 0; i < list.Count; i++)
							{

								if (list[i].Object.Prototype == State.Prototype)
								{
									list.Insert(i, new FaceState(State, face, renderer));
									break;
								}

								if (i == list.Count - 1)
								{
									list.Add(new FaceState(State, face, renderer));
									break;
								}
							}
						}
					}
					else
					{
						/*
						 * Alpha faces should be inserted at the end of the list- We're going to sort it anyway so it makes no odds
						 */
						list.Add(new FaceState(State, face, renderer));
					}
				}
			}
		}

		public void HideObject(ObjectState State)
		{
			RemoveObject(State);
		}

		private bool CheckTextureTransparency(TextureOrigin origin)
		{
			if (origin == null)
			{
				return false;
			}
			Texture texture = TextureManager.GetTextureFromCache(origin);
			return texture != null && texture.GetTransparencyType() == TextureTransparencyType.Alpha;
		}

		public List<FaceState> GetSortedPolygons(bool overlay = false)
		{
			if (overlay)
			{
				myOverlayAlphaFaces = GetSortedPolygons(myOverlayAlphaFaces.AsReadOnly());
				OverlayAlphaFaces = myOverlayAlphaFaces.AsReadOnly();
				return OverlayAlphaFaces.ToList();
			}
			return GetSortedPolygons(AlphaFaces);
		}

		private List<FaceState> GetSortedPolygons(ReadOnlyCollection<FaceState> faces)
		{
			int count = faces.Count;
			if (count == 0)
			{
				return new List<FaceState>();
			}

			// Ensure reusable buffers are large enough
			if (count > distanceBuffer.Length)
			{
				int newSize = Math.Max(distanceBuffer.Length * 2, count);
				Array.Resize(ref distanceBuffer, newSize);
				Array.Resize(ref sortedFacesBuffer, newSize);
			}

			for (int i = 0; i < count; i++)
			{
				sortedFacesBuffer[i] = faces[i];
			}

			// Cache camera variables to avoid repeating property lookup overhead
			Vector3 camPos = renderer.Camera.AbsolutePosition;
			Vector3 camDir = renderer.Camera.AbsoluteDirection;

			// Use parallel sorting only for large face sets due to task scheduling overhead
			if (count > 1000)
			{
				Parallel.For(0, count, i =>
				{
					distanceBuffer[i] = CalculateFaceDistance(sortedFacesBuffer[i], camPos, camDir);
				});
			}
			else
			{
				for (int i = 0; i < count; i++)
				{
					distanceBuffer[i] = CalculateFaceDistance(sortedFacesBuffer[i], camPos, camDir);
				}
			}

			// Sort only the active range
			Array.Sort(distanceBuffer, sortedFacesBuffer, 0, count);

			List<FaceState> result = new List<FaceState>(count);
			for (int i = 0; i < count; i++)
			{
				result.Add(sortedFacesBuffer[i]);
				sortedFacesBuffer[i] = null; // Prevent memory leaks/rooting objects
			}
			return result;
		}

		// Calculate face depth using cached camera info and fast inverse multiplication centroid logic
		private double CalculateFaceDistance(FaceState faceState, Vector3 camPos, Vector3 camDir)
		{
			var vertices = faceState.Face.Vertices;
			int vertCount = vertices.Length;
			if (vertCount < 1)
			{
				return 0.0;
			}

			Vector3 centroid = Vector3.Zero;
			var meshVerts = faceState.Object.Prototype.Mesh.Vertices;
			for (int j = 0; j < vertCount; j++)
			{
				centroid += meshVerts[vertices[j].Index].Coordinates;
			}

			centroid *= (1.0 / vertCount);
			centroid.Z = -centroid.Z;

			centroid.Transform(faceState.Object.ModelMatrix, false);
			centroid.Z = -centroid.Z;

			return -Vector3.Dot(centroid - camPos, camDir);
		}
	}
}
