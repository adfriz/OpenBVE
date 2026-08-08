using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
		/// <summary>The squared camera movement (in meters squared) which invalidates the sorted face cache</summary>
		private const double CameraMovementEpsilonSquared = 0.0004;
		/// <summary>The squared camera direction vector change which invalidates the sorted face cache</summary>
		private const double CameraDirectionEpsilonSquared = 1e-8;

		private readonly BaseRenderer renderer;

		public readonly QuadTree quadTree;

		public readonly ConcurrentDictionary<ObjectState, byte> Objects;
		private readonly List<FaceState> myOpaqueFaces;
		private readonly List<FaceState> myAlphaFaces;
		private readonly List<FaceState> myAlphaAdditiveFaces;
		private readonly List<FaceState> myOverlayOpaqueFaces;
		private List<FaceState> myOverlayAlphaFaces;
		private readonly List<FaceState> myOverlayAdditiveFaces;
		public readonly ReadOnlyCollection<FaceState> OpaqueFaces;  // StaticOpaque and DynamicOpaque
		public readonly ReadOnlyCollection<FaceState> OverlayOpaqueFaces;
		public readonly ReadOnlyCollection<FaceState> AlphaFaces;  // DynamicAlpha (non-additive only)
		public ReadOnlyCollection<FaceState> OverlayAlphaFaces;
		/// <summary>The additive world faces, in unsorted insertion order</summary>
		public readonly ReadOnlyCollection<FaceState> AdditiveAlphaFaces;
		/// <summary>The additive overlay faces, in unsorted insertion order</summary>
		public readonly ReadOnlyCollection<FaceState> OverlayAdditiveFaces;

		// Pooled buffers used by the world sort (grown by doubling, never allocated per frame after warmup)
		private FaceState[] sortBuffer;
		private double[] distancesBuffer;
		private FaceState[] cachedSorted;
		private double[] cachedDistances;
		// Pooled buffers used by the overlay sort
		private FaceState[] overlaySortBuffer;
		private double[] overlayDistancesBuffer;
		private FaceState[] overlaySorted;
		private double[] overlaySortedDistances;
		// Result lists, reused across calls. NOTE: Callers must treat these as read-only.
		private readonly List<FaceState> cachedResultList = new List<FaceState>();
		private readonly List<FaceState> overlayResultList = new List<FaceState>();
		// Camera movement cache
		private Vector3 lastCameraPosition;
		private Vector3 lastCameraOrientation;
		private int listVersion;
		private int cachedListVersion = -1;
		private bool cacheValid;

		public readonly object LockObject = new object();

		internal VisibleObjectLibrary(BaseRenderer Renderer)
		{
			renderer = Renderer;
			// Note: .Net has no Concurrent HashSet, so use a dictionary with a byte value instead
			// previous approach used a List and Contains()
			Objects = new ConcurrentDictionary<ObjectState, byte>();
			myOpaqueFaces = new List<FaceState>();
			myAlphaFaces = new List<FaceState>();
			myAlphaAdditiveFaces = new List<FaceState>();
			myOverlayOpaqueFaces = new List<FaceState>();
			myOverlayAlphaFaces = new List<FaceState>();
			myOverlayAdditiveFaces = new List<FaceState>();

			OpaqueFaces = myOpaqueFaces.AsReadOnly();
			AlphaFaces = myAlphaFaces.AsReadOnly();
			OverlayOpaqueFaces = myOverlayOpaqueFaces.AsReadOnly();
			OverlayAlphaFaces = myOverlayAlphaFaces.AsReadOnly();
			AdditiveAlphaFaces = myAlphaAdditiveFaces.AsReadOnly();
			OverlayAdditiveFaces = myOverlayAdditiveFaces.AsReadOnly();
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
					int removedAlpha = myAlphaFaces.RemoveAll(x => x.Object == state);
					removedAlpha += myAlphaAdditiveFaces.RemoveAll(x => x.Object == state);
					removedAlpha += myOverlayAlphaFaces.RemoveAll(x => x.Object == state);
					removedAlpha += myOverlayAdditiveFaces.RemoveAll(x => x.Object == state);
					myOverlayOpaqueFaces.RemoveAll(x => x.Object == state);
					if (removedAlpha > 0)
					{
						// Invalidate the sorted face cache
						listVersion++;
					}
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
				myAlphaAdditiveFaces.Clear();
				myOverlayOpaqueFaces.Clear();
				myOverlayAlphaFaces.Clear();
				myOverlayAdditiveFaces.Clear();
				listVersion++;
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

				/*
				 * Constructing the FaceState also computes the cached AlphaClass (this may require loading
				 * the texture bytes in order to determine the transparency type).
				 */
				FaceState faceState = new FaceState(State, face, renderer);
				MeshMaterial material = State.Prototype.Mesh.Materials[face.Material];

				bool alpha = false;

				if (Type == ObjectType.Overlay && renderer.Camera.CurrentRestriction != CameraRestrictionMode.NotAvailable)
				{
					alpha = true;
				}
				else
				{
					switch (faceState.AlphaClass)
					{
						case FaceAlphaClass.MaterialAlpha:
						case FaceAlphaClass.Additive:
						case FaceAlphaClass.Glow:
						case FaceAlphaClass.SmoothAlpha:
							alpha = true;
							break;
						case FaceAlphaClass.Partial:
							alpha = renderer.currentOptions.TransparencyMode == TransparencyMode.Quality || renderer.currentOptions.TransparencyMode == TransparencyMode.OrderIndependent;
							break;
						case FaceAlphaClass.Opaque:
						default:
							alpha = false;
							break;
					}
				}

				List<FaceState> list;

				if (material.BlendMode == MeshMaterialBlendMode.Additive)
				{
					// Additive faces are never sorted: they are drawn in insertion order
					list = Type == ObjectType.Overlay ? myOverlayAdditiveFaces : myAlphaAdditiveFaces;
				}
				else
				{
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
							list.Add(faceState);
						}
						else
						{
							for (int i = 0; i < list.Count; i++)
							{

								if (list[i].Object.Prototype == State.Prototype)
								{
									list.Insert(i, faceState);
									break;
								}

								if (i == list.Count - 1)
								{
									list.Add(faceState);
									break;
								}
							}
						}
					}
					else
					{
						/*
						 * Alpha faces should be inserted at the end of the list- We're going to sort it anyway so it makes no odds
						 * Additive faces are also appended at the end as they are drawn in insertion order.
						 */
						list.Add(faceState);
						listVersion++;
					}
				}
			}
		}

		public void HideObject(ObjectState State)
		{
			RemoveObject(State);
		}

		/// <summary>Gets the sorted alpha faces</summary>
		/// <param name="overlay">Whether to get the sorted overlay alpha faces instead</param>
		/// <remarks>The returned list must be treated as read-only by the caller; the same cached instance may be returned on subsequent calls.</remarks>
		public List<FaceState> GetSortedPolygons(bool overlay = false)
		{
			if (overlay)
			{
				myOverlayAlphaFaces = GetSortedPolygons(myOverlayAlphaFaces.AsReadOnly(), false);
				OverlayAlphaFaces = myOverlayAlphaFaces.AsReadOnly();
				return overlayResultList;
			}
			return GetSortedPolygons(AlphaFaces, true);
		}

		private List<FaceState> GetSortedPolygons(ReadOnlyCollection<FaceState> faces, bool useCameraCache)
		{
			int count = faces.Count;

			if (count <= 1)
			{
				// A list of zero or one faces is trivially sorted.
				// Return a snapshot into the cached result list rather than the live list, as the
				// caller iterates the result outside the visibility lock.
				List<FaceState> result = useCameraCache ? cachedResultList : overlayResultList;
				result.Clear();
				for (int i = 0; i < count; i++)
				{
					result.Add(faces[i]);
				}
				return result;
			}

			if (useCameraCache && cacheValid && cachedListVersion == listVersion && IsCameraStatic())
			{
				return cachedResultList;
			}

			if (useCameraCache)
			{
				List<FaceState> result = SortIntoList(faces, count, cachedResultList, ref sortBuffer, ref distancesBuffer, ref cachedSorted, ref cachedDistances);
				cachedListVersion = listVersion;
				lastCameraPosition = renderer.Camera.AbsolutePosition;
				lastCameraOrientation = renderer.Camera.AbsoluteDirection;
				cacheValid = true;
				return result;
			}
			return SortIntoList(faces, count, overlayResultList, ref overlaySortBuffer, ref overlayDistancesBuffer, ref overlaySorted, ref overlaySortedDistances);
		}

		/// <summary>Sorts the specified faces back-to-front and returns them in the supplied reusable result list</summary>
		private List<FaceState> SortIntoList(ReadOnlyCollection<FaceState> faces, int count, List<FaceState> resultList, ref FaceState[] scratch, ref double[] scratchDistances, ref FaceState[] sorted, ref double[] sortedDistances)
		{
			EnsureBufferCapacity(ref scratch, ref scratchDistances, count);
			EnsureBufferCapacity(ref sorted, ref sortedDistances, count);
			double[] distances = scratchDistances;

			// calculate the sort key for each face in parallel.
			// The key is the negative squared signed distance from the camera to the face plane through
			// the world-space centroid of the first three vertices, falling back to the negative squared
			// distance to the centroid for degenerate faces.
			Parallel.For(0, count, i =>
			{
				if (faces[i].Face.Vertices.Length >= 3)
				{
					Vector4 v0 = new Vector4(faces[i].Object.Prototype.Mesh.Vertices[faces[i].Face.Vertices[0].Index].Coordinates, 1.0);
					Vector4 v1 = new Vector4(faces[i].Object.Prototype.Mesh.Vertices[faces[i].Face.Vertices[1].Index].Coordinates, 1.0);
					Vector4 v2 = new Vector4(faces[i].Object.Prototype.Mesh.Vertices[faces[i].Face.Vertices[2].Index].Coordinates, 1.0);
					Vector4 w1 = v1 - v0;
					Vector4 w2 = v2 - v0;
					v0.Z *= -1.0;
					w1.Z *= -1.0;
					w2.Z *= -1.0;
					v0 = Vector4.Transform(v0, faces[i].Object.ModelMatrix);
					w1 = Vector4.Transform(w1, faces[i].Object.ModelMatrix);
					w2 = Vector4.Transform(w2, faces[i].Object.ModelMatrix);
					v0.Z *= -1.0;
					w1.Z *= -1.0;
					w2.Z *= -1.0;
					// World-space centroid of the first three vertices
					Vector3 centroid = v0.Xyz + (w1.Xyz + w2.Xyz) / 3.0;
					Vector3 d = Vector3.Cross(w1.Xyz, w2.Xyz);
					double t = d.Norm();
					Vector3 w0 = centroid - renderer.Camera.AbsolutePosition;

					if (t != 0.0)
					{
						d /= t;
						t = Vector3.Dot(d, w0);
						distances[i] = -t * t;
					}
					else
					{
						// Degenerate normal: fall back to the squared centroid distance
						distances[i] = -Vector3.Dot(w0, w0);
					}
				}
				else if (faces[i].Face.Vertices.Length > 0)
				{
					// Face with fewer than three vertices: use the squared distance to the centroid of the available vertices
					Vector3 centroid = new Vector3();
					for (int j = 0; j < faces[i].Face.Vertices.Length; j++)
					{
						Vector4 v = new Vector4(faces[i].Object.Prototype.Mesh.Vertices[faces[i].Face.Vertices[j].Index].Coordinates, 1.0);
						v.Z *= -1.0;
						v = Vector4.Transform(v, faces[i].Object.ModelMatrix);
						v.Z *= -1.0;
						centroid += v.Xyz;
					}
					centroid /= faces[i].Face.Vertices.Length;
					Vector3 w0 = centroid - renderer.Camera.AbsolutePosition;
					distances[i] = -Vector3.Dot(w0, w0);
				}
				else
				{
					distances[i] = 0.0;
				}
			});

			// copy the faces into the scratch buffer
			for (int i = 0; i < count; i++)
			{
				scratch[i] = faces[i];
			}

			// stable bottom-up merge sort
			StableMergeSort(scratch, scratchDistances, sorted, sortedDistances, count);

			// update the result list
			resultList.Clear();
			for (int i = 0; i < count; i++)
			{
				resultList.Add(sorted[i]);
			}
			return resultList;
		}

		/// <summary>Stable merge sort over the face and distance buffers. On return, target[0..count) contains the faces sorted by ascending key.</summary>
		private static void StableMergeSort(FaceState[] source, double[] sourceDistances, FaceState[] target, double[] targetDistances, int count)
		{
			FaceState[] a = source;
			FaceState[] b = target;
			double[] aDistances = sourceDistances;
			double[] bDistances = targetDistances;

			for (int width = 1; width < count; width <<= 1)
			{
				for (int i = 0; i < count; i += width << 1)
				{
					int middle = i + width;
					if (middle > count) middle = count;
					int right = i + (width << 1);
					if (right > count) right = count;

					if (middle < right)
					{
						int x = i;
						int y = middle;
						int d = i;
						// The <= comparison keeps the sort stable: equal keys retain their input order
						while (x < middle && y < right)
						{
							if (aDistances[x] <= aDistances[y])
							{
								bDistances[d] = aDistances[x];
								b[d++] = a[x++];
							}
							else
							{
								bDistances[d] = aDistances[y];
								b[d++] = a[y++];
							}
						}
						while (x < middle)
						{
							bDistances[d] = aDistances[x];
							b[d++] = a[x++];
						}
						while (y < right)
						{
							bDistances[d] = aDistances[y];
							b[d++] = a[y++];
						}
					}
					else
					{
						// Single run: copy
						for (int j = i; j < right; j++)
						{
							bDistances[j] = aDistances[j];
							b[j] = a[j];
						}
					}
				}
				// swap the buffer roles for the next pass
				FaceState[] temp = a;
				a = b;
				b = temp;
				double[] tempDistances = aDistances;
				aDistances = bDistances;
				bDistances = tempDistances;
			}

			if (a != target)
			{
				// The final pass wrote into the scratch buffer: copy the result back
				Array.Copy(a, target, count);
				Array.Copy(aDistances, targetDistances, count);
			}
		}

		/// <summary>Ensures the specified buffers are large enough, growing by doubling when required</summary>
		private static void EnsureBufferCapacity(ref FaceState[] buffer, ref double[] distanceBuffer, int count)
		{
			if (buffer == null || buffer.Length < count)
			{
				buffer = new FaceState[buffer == null ? count : Math.Max(count, buffer.Length << 1)];
			}
			if (distanceBuffer == null || distanceBuffer.Length < count)
			{
				distanceBuffer = new double[distanceBuffer == null ? count : Math.Max(count, distanceBuffer.Length << 1)];
			}
		}

		/// <summary>Whether the camera has not moved (or rotated) enough to invalidate the sorted face cache</summary>
		private bool IsCameraStatic()
		{
			Vector3 position = renderer.Camera.AbsolutePosition;
			Vector3 direction = renderer.Camera.AbsoluteDirection;
			return Vector3.NormSquared(position - lastCameraPosition) < CameraMovementEpsilonSquared
				&& Vector3.NormSquared(direction - lastCameraOrientation) < CameraDirectionEpsilonSquared;
		}
	}
}
