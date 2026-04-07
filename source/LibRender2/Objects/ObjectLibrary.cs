using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

		private readonly List<ObjectState> myObjects;
		private readonly HashSet<ObjectState> objectsSet;
		private readonly Dictionary<int, List<FaceState>> opaqueBuckets;
		private readonly List<FaceState> myOpaqueFacesFlattened;
		private readonly List<FaceState> myAlphaFaces;
		private readonly List<FaceState> myOverlayOpaqueFacesFlattened;
		private readonly List<FaceState> myOverlayAlphaFaces;
		
		public readonly ReadOnlyCollection<ObjectState> Objects;
		public readonly ReadOnlyCollection<FaceState> OpaqueFaces;  
		public readonly ReadOnlyCollection<FaceState> OverlayOpaqueFaces;
		public readonly ReadOnlyCollection<FaceState> AlphaFaces;  
		public readonly ReadOnlyCollection<FaceState> OverlayAlphaFaces;

		private bool opaqueChanged = true;
		private bool overlayOpaqueChanged = true;

		public readonly object LockObject = new object();

		internal VisibleObjectLibrary(BaseRenderer Renderer)
		{
			renderer = Renderer;
			myObjects = new List<ObjectState>();
			objectsSet = new HashSet<ObjectState>();
			opaqueBuckets = new Dictionary<int, List<FaceState>>();
			myOpaqueFacesFlattened = new List<FaceState>();
			myAlphaFaces = new List<FaceState>();
			myOverlayOpaqueFacesFlattened = new List<FaceState>();
			myOverlayAlphaFaces = new List<FaceState>();

			Objects = myObjects.AsReadOnly();
			OpaqueFaces = myOpaqueFacesFlattened.AsReadOnly();
			AlphaFaces = myAlphaFaces.AsReadOnly();
			OverlayOpaqueFaces = myOverlayOpaqueFacesFlattened.AsReadOnly();
			OverlayAlphaFaces = myOverlayAlphaFaces.AsReadOnly();

			quadTree = new QuadTree(renderer.currentOptions.ViewingDistance);
		}

		private void RemoveObject(ObjectState state)
		{
			lock (LockObject)
			{
				if (objectsSet.Remove(state))
				{
					myObjects.Remove(state);
					foreach (var bucket in opaqueBuckets.Values)
					{
						if (bucket.RemoveAll(x => x.Object == state) > 0)
						{
							opaqueChanged = true;
							overlayOpaqueChanged = true;
						}
					}
					myAlphaFaces.RemoveAll(x => x.Object == state);
					myOverlayAlphaFaces.RemoveAll(x => x.Object == state);
				}	
			}
		}

		public void Clear()
		{
			lock (LockObject)
			{
				myObjects.Clear();
				objectsSet.Clear();
				opaqueBuckets.Clear();
				myOpaqueFacesFlattened.Clear();
				myAlphaFaces.Clear();
				myOverlayOpaqueFacesFlattened.Clear();
				myOverlayAlphaFaces.Clear();
				renderer.StaticObjectStates.Clear();
				renderer.DynamicObjectStates.Clear();
				opaqueChanged = true;
				overlayOpaqueChanged = true;
			}
		}

		public void ShowObject(ObjectState State, ObjectType Type)
		{
			lock (LockObject)
			{
				if (State.Prototype == null) return;
				if (objectsSet.Add(State))
				{
					myObjects.Add(State);
				}
				else
				{
					return;
				}

				foreach (MeshFace face in State.Prototype.Mesh.Faces)
				{
					MeshMaterial material = State.Prototype.Mesh.Materials[face.Material];
					
					if ((material.DaytimeTexture != null || material.NighttimeTexture != null) && material.WrapMode == null)
					{
						OpenGlTextureWrapMode wrap = OpenGlTextureWrapMode.ClampClamp;
						foreach (VertexTemplate vertex in State.Prototype.Mesh.Vertices)
						{
							if (vertex.TextureCoordinates.X < 0.0f || vertex.TextureCoordinates.X > 1.0f) wrap |= OpenGlTextureWrapMode.RepeatClamp;
							if (vertex.TextureCoordinates.Y < 0.0f || vertex.TextureCoordinates.Y > 1.0f) wrap |= OpenGlTextureWrapMode.ClampRepeat;
							if (wrap == OpenGlTextureWrapMode.RepeatRepeat) break;
						}
						State.Prototype.Mesh.Materials[face.Material].WrapMode = wrap;
					}

					bool alpha = false;
					if (Type == ObjectType.Overlay && renderer.Camera.CurrentRestriction != CameraRestrictionMode.NotAvailable)
					{
						alpha = true;
					}
					else if (material.Color.A != 255 || material.BlendMode == MeshMaterialBlendMode.Additive || material.GlowAttenuationData != 0)
					{
						alpha = true;
					}
					else if (material.DaytimeTexture != null)
					{
						Texture tex;
						if (!TextureManager.textureCache.TryGetValue(material.DaytimeTexture.Origin, out tex))
						{
							material.DaytimeTexture.Origin.GetTexture(out tex);
						}
						if (tex != null && (tex.GetTransparencyType() == TextureTransparencyType.Alpha || (tex.GetTransparencyType() == TextureTransparencyType.Partial && renderer.currentOptions.TransparencyMode == TransparencyMode.Quality)))
						{
							alpha = true;
						}
					}

					FaceState fs = new FaceState(State, face, renderer);
					if (alpha)
					{
						if (Type == ObjectType.Overlay) myOverlayAlphaFaces.Add(fs);
						else myAlphaFaces.Add(fs);
					}
					else
					{
						int hash = material.GetHashCode();
						if (!opaqueBuckets.ContainsKey(hash)) opaqueBuckets.Add(hash, new List<FaceState>());
						opaqueBuckets[hash].Add(fs);
						opaqueChanged = true;
						if (Type == ObjectType.Overlay) overlayOpaqueChanged = true;
					}
				}
			}
		}

		public List<FaceState> GetOpaqueFaces(bool overlay = false)
		{
			lock (LockObject)
			{
				bool changed = overlay ? overlayOpaqueChanged : opaqueChanged;
				List<FaceState> flattened = overlay ? myOverlayOpaqueFacesFlattened : myOpaqueFacesFlattened;

				if (changed)
				{
					flattened.Clear();
					foreach (var bucket in opaqueBuckets.Values)
					{
						// Sort bucket by Mesh to ensure instances of the same mesh are consecutive
						bucket.Sort((a, b) => a.Object.Prototype.Mesh.GetHashCode().CompareTo(b.Object.Prototype.Mesh.GetHashCode()));
						flattened.AddRange(bucket);
					}
					if (overlay) overlayOpaqueChanged = false;
					else opaqueChanged = false;
				}
				return flattened;
			}
		}

		public void HideObject(ObjectState State)
		{
			RemoveObject(State);
		}

		private readonly List<FaceState> myAlphaFacesSorted = new List<FaceState>();
		private readonly List<FaceState> myOverlayAlphaFacesSorted = new List<FaceState>();

		public List<FaceState> GetSortedPolygons(bool overlay = false)
		{
			lock (LockObject)
			{
				List<FaceState> source = overlay ? myOverlayAlphaFaces : myAlphaFaces;
				List<FaceState> result = overlay ? myOverlayAlphaFacesSorted : myAlphaFacesSorted;
				
				if (source.Count == 0)
				{
					result.Clear();
					return result;
				}

				result.Clear();
				result.AddRange(source);

				Vector3 cameraPos = renderer.Camera.AbsolutePosition;
				double cameraDistSq = (cameraPos - lastCameraSortPosition).SizeSquared();
				
				if (cameraDistSq > 0.25 || source.Count != lastSortCount) 
				{
					for (int i = 0; i < result.Count; i++)
					{
						if (result[i].Face.Vertices.Length >= 3)
						{
							Vector3 v0 = result[i].Object.Prototype.Mesh.Vertices[result[i].Face.Vertices[0].Index].Coordinates;
							v0.Z *= -1.0;
							v0.Rotate(result[i].Object.Rotate);
							v0 += result[i].Object.WorldPosition;

							double dx = v0.X - cameraPos.X;
							double dy = v0.Y - cameraPos.Y;
							double dz = v0.Z - cameraPos.Z;
							result[i].Distance = dx * dx + dy * dy + dz * dz;
						}
					}
					result.Sort((a, b) => b.Distance.CompareTo(a.Distance));
					lastCameraSortPosition = cameraPos;
					lastSortCount = source.Count;
				}
				return result; 
			}
		}

		private Vector3 lastCameraSortPosition = new Vector3(double.MaxValue, 0, 0);
		private int lastSortCount = -1;
	}
}
