﻿using TrainManager.Car;
using TrainManager.Handles;
using TrainManager.Power;

namespace TrainManager.TractionModels.Steam
{
	/// <summary>The traction model for a simplistic steam engine</summary>
	public class SteamEngine : AbstractTractionModel
	{
		/// <summary>The boiler</summary>
		public readonly Boiler Boiler;
		/// <summary>The cylinder chest</summary>
		public readonly CylinderChest CylinderChest;
		/// <summary>The acceleration curves</summary>
		public AccelerationCurve[] AccelerationCurves;
		/// <summary>Gets the current acceleration output</summary>
		public double PowerOutput
		{
			get
			{
				Regulator regulator = Car.baseTrain.Handles.Power as Regulator;
				// ReSharper disable once PossibleNullReferenceException
				int curve = (int)(regulator.Ratio * AccelerationCurves.Length);
				return AccelerationCurves[curve].GetAccelerationOutput(Car.CurrentSpeed, 1.0);
			}
		}

		public SteamEngine(CarBase car) : base(car)
		{
			/* todo: generic parameters- load from config
			 * Fudged average numbers here at the minute, based upon a hypothetical large tender loco
			 *
			 * Boiler: 
			 *			2000L starting level
			 *			3000L capacity
			 *			200psi starting pressure
			 *			240psi absolute max pressure
			 *			220psi blowoff pressure
			 *			120psi minimum working pressure
			 *			1L water ==> 4.15psi steam ==> divide by 60min for rate / min ==> divide by 60 for rate /s [CONSTANT, BUT THIS DEPENDS ON BOILER SIZING??]
			 */
			Boiler = new Boiler(this, 2000, 3000, 200, 240, 220, 120, 0.00152);
			/*
			 * Cylinder Chest
			 *			5psi standing pressure loss (leakage etc.)
			 *			20psi base stroke pressure, before reduction due to regulator / cutoff
			 */
			CylinderChest = new CylinderChest(this, 5, 20);
			/*
			 * Cutoff
			 *			75% max forwards
			 *			50% max reverse
			 *			10% around zero where cutoff is ineffective (due to standing resistance etc.)
			 */
			Car.baseTrain.Handles.Reverser = new Cutoff(Car.baseTrain, 75, -50, 10);
		}

		private double lastTrackPosition;

		public override void Update(double timeElapsed, out double Speed)
		{
			// update the boiler pressure and associated gubbins first
			Boiler.Update(timeElapsed);
			// get the distance travelled & convert to piston strokes
			CylinderChest.Update(timeElapsed, Car.FrontAxle.Follower.TrackPosition - lastTrackPosition);
			lastTrackPosition = Car.FrontAxle.Follower.TrackPosition;
			Speed = 0; // TODO
		}
	}
}
