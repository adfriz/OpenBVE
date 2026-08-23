using OpenBveApi.Math;
using OpenBveApi.Objects;

namespace LibRender2.Objects
{
	/// <summary>Represents a face state within the renderer</summary>
	public class FaceState
	{
		/// <summary>The containing object</summary>
		public readonly ObjectState Object;
		/// <summary>The face to draw</summary>
		public readonly MeshFace Face;
		/// <summary>Holds the reference to the base renderer</summary>
		public readonly BaseRenderer Renderer;

		public FaceState(ObjectState _object, MeshFace face, BaseRenderer renderer)
		{
			Object = _object;
			Face = face;
			Renderer = renderer;
			if (Object.Prototype.Mesh.VAO == null)
			{
				VAOExtensions.CreateOrUpdateVAO(Object.Prototype.Mesh, Object.Prototype.Dynamic, Renderer.DefaultShader.VertexLayout, Renderer);
            }
			
        }

		public void Draw()
		{
			Renderer.RenderFace(this);
		}

		/// <summary>Computes the squared signed distance from the camera to this face's plane (primary alpha-face sorting key)
		/// and the face normal's dot product with the camera direction (secondary tiebreaker for intersecting edges).</summary>
		internal double GetSortDistance(Vector3 cameraPosition, Vector3 cameraDirection)
		{
			if (Face.Vertices.Length < 3 || Object.Prototype.Mesh.Vertices.Length < 3)
			{
				return 0.0;
			}

			Vector4 v0 = new Vector4(Object.Prototype.Mesh.Vertices[Face.Vertices[0].Index].Coordinates, 1.0);
			Vector4 v1 = new Vector4(Object.Prototype.Mesh.Vertices[Face.Vertices[1].Index].Coordinates, 1.0);
			Vector4 v2 = new Vector4(Object.Prototype.Mesh.Vertices[Face.Vertices[2].Index].Coordinates, 1.0);

			Vector4 w1 = v1 - v0;
			Vector4 w2 = v2 - v0;

			v0.Z *= -1.0;
			w1.Z *= -1.0;
			w2.Z *= -1.0;

			v0 = Vector4.Transform(v0, Object.ModelMatrix);
			w1 = Vector4.Transform(w1, Object.ModelMatrix);
			w2 = Vector4.Transform(w2, Object.ModelMatrix);

			v0.Z *= -1.0;
			w1.Z *= -1.0;
			w2.Z *= -1.0;

			Vector3 d = Vector3.Cross(w1.Xyz, w2.Xyz);
			double len = d.Norm();

			if (len != 0.0)
			{
				d /= len;
				Vector3 w0 = v0.Xyz - cameraPosition;
				double t = Vector3.Dot(d, w0);
				return -t * t;
			}

			return 0.0;
		}

		/// <summary>Computes a tiebreaker key for faces at similar plane-distances.
		/// Returns a value that sorts faces whose normals face toward the camera AFTER faces
		/// whose normals face away, which resolves the ambiguous ordering at intersecting edges.</summary>
		internal double GetIntersectTiebreaker(Vector3 cameraDirection)
		{
			if (Face.Vertices.Length < 3 || Object.Prototype.Mesh.Vertices.Length < 3)
			{
				return 0.0;
			}

			Vector4 v0 = new Vector4(Object.Prototype.Mesh.Vertices[Face.Vertices[0].Index].Coordinates, 1.0);
			Vector4 v1 = new Vector4(Object.Prototype.Mesh.Vertices[Face.Vertices[1].Index].Coordinates, 1.0);
			Vector4 v2 = new Vector4(Object.Prototype.Mesh.Vertices[Face.Vertices[2].Index].Coordinates, 1.0);

			Vector4 w1 = v1 - v0;
			Vector4 w2 = v2 - v0;

			w1.Z *= -1.0;
			w2.Z *= -1.0;

			w1 = Vector4.Transform(w1, Object.ModelMatrix);
			w2 = Vector4.Transform(w2, Object.ModelMatrix);

			w1.Z *= -1.0;
			w2.Z *= -1.0;

			Vector3 d = Vector3.Cross(w1.Xyz, w2.Xyz);
			double len = d.Norm();

			if (len != 0.0)
			{
				d /= len;
				return -Vector3.Dot(d, cameraDirection);
			}

			return 0.0;
		}
	}
}
