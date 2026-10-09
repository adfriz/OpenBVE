using OpenBveApi.Math;
using OpenBveApi.Routes;
using System;
using System.Collections.Generic;

namespace OpenBveApi.Trains
{
	/// <summary>An abstract train car</summary>
	public class AbstractCar
	{
		/// <summary>Front axle about which the car pivots</summary>
		public AbstractAxle FrontAxle;

		/// <summary>Rear axle about which the car pivots</summary>
		public AbstractAxle RearAxle;

		/// <summary>The width of the car in meters</summary>
		public double Width;

		/// <summary>The height of the car in meters</summary>
		public double Height;

		/// <summary>The length of the car in meters</summary>
		public double Length;

		/// <summary>The Up vector</summary>
		public Vector3 Up;

		/// <summary>The current speed of this car</summary>
		/// <remarks>Default units are km/h</remarks>
		public double CurrentSpeed;

		/// <summary>The textual description for this car</summary>
		public string Description;

		/// <summary>The empty mass of the car in kilograms</summary>
		public double EmptyMass;

		/// <summary>Returns the current mass of the car including cargo in kilograms</summary>
		public double CurrentMass => EmptyMass + CargoMass;

		/// <summary>The current mass of any cargo in the car in kilograms</summary>
		public double CargoMass;

		/// <summary>Visual-only body roll in radians (positive = lean right). Updated every frame, safe for addons to read.</summary>
		public double PhysicsMotionRollAngle;

		/// <summary>Visual-only body lateral offset in meters (positive = right).</summary>
		public double PhysicsMotionSway;

		/// <summary>Visual-only body vertical offset in meters (positive = up).</summary>
		public double PhysicsMotionBounce;

		/// <summary>Visual-only body pitch in radians (positive = nose up).</summary>
		public double PhysicsMotionPitchAngle;

		/// <summary>Visual-only body longitudinal offset in meters (positive = forwards).</summary>
		public double PhysicsMotionShift;

		/// <summary>Contains the current brightness values</summary>
		public Brightness Brightness;

		/// <summary>The driving wheel sets on the car</summary>
		public List<Wheels> DrivingWheels = new List<Wheels>();

		/// <summary>The trailing wheel sets on the car</summary>
		public List<Wheels> TrailingWheels = new List<Wheels>();

		/// <summary>Returns the available power supplies to this car</summary>
		public virtual Dictionary<PowerSupplyTypes, PowerSupply> AvailablePowerSupplies => new Dictionary<PowerSupplyTypes, PowerSupply>();

		/// <summary>Creates the in-world co-ordinates for a sound attached to this car</summary>
		public virtual void CreateWorldCoordinates(Vector3 Car, out Vector3 Position, out Vector3 Direction)
		{
			Position = Vector3.Zero;
			Direction = Vector3.Zero;
		}

		/// <summary>Gets the track position of this car</summary>
		public virtual double TrackPosition => 0.0;

		/// <summary>The index of the car within the train</summary>
		public virtual int Index
		{
			// A single car is by itself a train, hence index zero
			get => 0;
			set => throw new NotSupportedException("Cannot set the index of a single car");
		}

		/// <summary>Call this method to reverse (flip) the car</summary>
		public virtual void Reverse(bool flipInterior = false)
		{

		}

		/// <summary>Opens the car doors</summary>
		public virtual void OpenDoors(bool Left, bool Right)
		{

		}

		/// <summary>Uncouples the car</summary>
		public virtual void Uncouple(bool Front, bool Rear)
		{

		}

		/// <summary>Derails this car</summary>
		public virtual void Derail()
		{

		}
	}
}
