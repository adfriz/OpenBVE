using System.Collections.Generic;
using System.Linq;
using OpenBveApi.FunctionScripting;
using OpenBveApi.Hosts;
using OpenBveApi.Math;
using OpenBveApi.Trains;

namespace OpenBveApi.Objects.ObjectTypes
{
	/// <summary>An object using a hiearchy of animated parts</summary>
	public class HierarchyAnimatedObject
	{
		internal HostInterface currentHost;
		/// <summary>The animation hiearchy, containing the transformation and rotation matricies</summary>
		public Dictionary<string, HierarchyEntry> HierarchyParts;
		/// <summary>The actual objects to be animated</summary>
		public HiearchyObject[] Objects;

		/// <summary>Updates the animated object</summary>
		public void Update(bool IsPartOfTrain, AbstractTrain Train, int CarIndex, int SectionIndex, double TrackPosition, Vector3 Position, Vector3 Direction, Vector3 Up, Vector3 Side, bool UpdateFunctions, bool Show, double TimeElapsed, bool EnableDamping, bool IsTouch = false, dynamic Camera = null)
		{
			for (int i = 0; i < HierarchyParts.Count; i++)
			{
				string key = HierarchyParts.ElementAt(i).Key;
				HierarchyParts[key].FunctionScript.ExecuteScript(Train, CarIndex, Position, TrackPosition, SectionIndex, IsPartOfTrain, TimeElapsed, -1);
			}

			for (int i = 0; i < Objects.Length; i++)
			{
				Objects[i].Update();
				if (Show)
				{
					if (Camera != null)
					{
						currentHost.ShowObject(Objects[i].State, ObjectType.Overlay);
					}
					else
					{
						currentHost.ShowObject(Objects[i].State, ObjectType.Dynamic);
					}
				}
				else
				{
					currentHost.HideObject(Objects[i].State);
				}
			}
		}
	}

	/// <summary>An object stored within a hierarchy of animated parts</summary>
	public class HiearchyObject
	{
		/// <summary>Holds a reference to the root object</summary>
		private readonly HierarchyAnimatedObject rootObject;
		/// <summary>Contains the hiearchy list</summary>
		public readonly string[] Hiearchy;
		/// <summary>The object state to be transformed</summary>
		public readonly ObjectState State;
		
		/// <summary>Creates a new hierarchy object</summary>
		public HiearchyObject(HierarchyAnimatedObject RootObject, string[] hiearchy, ObjectState state)
		{
			rootObject = RootObject;
			Hiearchy = hiearchy;
			State = state;
		}

		/// <summary>Updates the final object state</summary>
		internal void Update()
		{
			Matrix4D translation = Matrix4D.NoTransformation;
			Matrix4D rotation = Matrix4D.NoTransformation;
			for (int i = 0; i < Hiearchy.Length; i++)
			{
				translation += rootObject.HierarchyParts[Hiearchy[i]].CurrentTranslationMatrix;
				rotation += rootObject.HierarchyParts[Hiearchy[i]].CurrentRotationMatrix;
			}

			State.Translation = translation;
			State.Rotate = rotation;
		}

	}

	/// <summary>An animation hiearchy entry</summary>
	public class HierarchyEntry
	{
		/// <summary>The name of this entry</summary>
		public readonly string Name;
		/// <summary>The controlling function script</summary>
		public readonly FunctionScript FunctionScript;
		/// <summary>The list of translation matricies</summary>
		public readonly Matrix4D[] TranslationMatricies;
		/// <summary>The list of rotation matricies</summary>
		public readonly Matrix4D[] RotationMatricies;
		
		/// <summary>Creates a new hieararchy entry</summary>
		public HierarchyEntry(HostInterface host, string partName, string functionScript, Matrix4D[] translationMatricies, Matrix4D[] rotationMatricies)
		{
			Name = partName;
			FunctionScript = new FunctionScript(host, functionScript, true);
			TranslationMatricies = translationMatricies;
			RotationMatricies = rotationMatricies;
		}

		/// <summary>Gets the current matrix</summary>
		public Matrix4D CurrentTranslationMatrix => TranslationMatricies[(int)FunctionScript.LastResult % TranslationMatricies.Length];
		/// <summary>Gets the current matrix</summary>
		public Matrix4D CurrentRotationMatrix => RotationMatricies[(int)FunctionScript.LastResult % RotationMatricies.Length];
	}
}
