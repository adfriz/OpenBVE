using System;
using System.Collections.Generic;
using System.Linq;
using OpenBveApi.Colors;
using OpenBveApi.Hosts;
using OpenBveApi.Math;
using OpenBveApi.World;

namespace OpenBveApi.Objects
{
	// <summary>Represents a static (e.g. non-animated) object within the world</summary>
	/// <inheritdoc />
	public partial class StaticObject : UnifiedObject
	{
		/// <summary>Whether the object is optimized</summary>
		private bool isOptimized;
		/// <summary>The mesh of the object</summary>
		public Mesh Mesh;
		/// <summary>The starting track position, for static objects only.</summary>
		public float StartingTrackDistance;
		/// <summary>The ending track position, for static objects only.</summary>
		public float EndingTrackDistance;
		/// <summary>Whether the object is dynamic, i.e. not static.</summary>
		public bool Dynamic;
		/// <summary> Stores the author for this object.</summary>
		public string Author;
		/// <summary> Stores the copyright information for this object.</summary>
		public string Copyright;

		private readonly HostInterface currentHost;

		/// <summary>Creates a new empty object</summary>
		public StaticObject(HostInterface host)
		{
			currentHost = host;
			Mesh = new Mesh();
		}

		/// <summary>Creates a clone of this object.</summary>
		/// <param name="daytimeTexture">The replacement daytime texture</param>
		/// <param name="nighttimeTexture">The replacement nighttime texture</param>
		/// <returns></returns>
		public StaticObject Clone(Textures.Texture daytimeTexture, Textures.Texture nighttimeTexture) //Prefix is required or else MCS barfs
		{
			StaticObject cloneResult = new StaticObject(currentHost)
			{
				StartingTrackDistance = StartingTrackDistance,
				EndingTrackDistance = EndingTrackDistance,
				Dynamic = Dynamic,
				Mesh = {Vertices = new VertexTemplate[Mesh.Vertices.Length]},
				isOptimized = isOptimized
			};
			// vertices
			for (int j = 0; j < Mesh.Vertices.Length; j++)
			{
				cloneResult.Mesh.Vertices[j] = Mesh.Vertices[j].Clone();
			}

			// faces
			cloneResult.Mesh.Faces = new MeshFace[Mesh.Faces.Length];
			for (int j = 0; j < Mesh.Faces.Length; j++)
			{
				cloneResult.Mesh.Faces[j].Flags = Mesh.Faces[j].Flags;
				cloneResult.Mesh.Faces[j].Material = Mesh.Faces[j].Material;
				cloneResult.Mesh.Faces[j].Vertices = new MeshFaceVertex[Mesh.Faces[j].Vertices.Length];
				for (int k = 0; k < Mesh.Faces[j].Vertices.Length; k++)
				{
					cloneResult.Mesh.Faces[j].Vertices[k] = Mesh.Faces[j].Vertices[k];
				}
			}

			// materials
			cloneResult.Mesh.Materials = new MeshMaterial[Mesh.Materials.Length];
			for (int j = 0; j < Mesh.Materials.Length; j++)
			{
				cloneResult.Mesh.Materials[j] = Mesh.Materials[j];
				cloneResult.Mesh.Materials[j].DaytimeTexture = daytimeTexture ?? Mesh.Materials[j].DaytimeTexture;
				cloneResult.Mesh.Materials[j].NighttimeTexture = nighttimeTexture ?? Mesh.Materials[j].NighttimeTexture;
			}

			return cloneResult;
		}

		/// <summary>Creates a clone of this object.</summary>
		public override UnifiedObject Clone()
		{
			StaticObject cloneResult = new StaticObject(currentHost)
			{
				StartingTrackDistance = StartingTrackDistance,
				EndingTrackDistance = EndingTrackDistance,
				Dynamic = Dynamic,
				Mesh = {Vertices = new VertexTemplate[Mesh.Vertices.Length]},
				isOptimized = isOptimized
			};
			// vertices
			for (int j = 0; j < Mesh.Vertices.Length; j++)
			{
				cloneResult.Mesh.Vertices[j] = Mesh.Vertices[j].Clone();
			}

			// faces
			cloneResult.Mesh.Faces = new MeshFace[Mesh.Faces.Length];
			for (int j = 0; j < Mesh.Faces.Length; j++)
			{
				cloneResult.Mesh.Faces[j].Flags = Mesh.Faces[j].Flags;
				cloneResult.Mesh.Faces[j].Material = Mesh.Faces[j].Material;
				cloneResult.Mesh.Faces[j].Vertices = new MeshFaceVertex[Mesh.Faces[j].Vertices.Length];
				for (int k = 0; k < Mesh.Faces[j].Vertices.Length; k++)
				{
					cloneResult.Mesh.Faces[j].Vertices[k] = Mesh.Faces[j].Vertices[k];
				}
			}

			// materials
			cloneResult.Mesh.Materials = new MeshMaterial[Mesh.Materials.Length];
			for (int j = 0; j < Mesh.Materials.Length; j++)
			{
				cloneResult.Mesh.Materials[j] = Mesh.Materials[j];
			}

			return cloneResult;
		}

	}
}


