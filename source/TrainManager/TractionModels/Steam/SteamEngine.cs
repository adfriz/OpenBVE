using System;
using OpenBveApi.Interface;
using TrainManager.Car;
using TrainManager.Handles;
using TrainManager.Trains;

namespace TrainManager.TractionModels.Steam
{
	/// <summary>The traction model for a simplistic steam engine</summary>
	public class SteamEngine : AbstractTractionModel
	{
		/// <summary>The jerk applied when the acceleration increases</summary>
		private readonly double JerkPowerUp;
		/// <summary>The jerk applied when the acceleration decreases</summary>
		private readonly double JerkPowerDown;
		/// <summary>The boiler</summary>
		public readonly Boiler Boiler;
		/// <summary>The cylinder chest</summary>
		public readonly CylinderChest CylinderChest;
		/// <summary>The tender</summary>
		public readonly Tender Tender;
		/// <summary>Gets the current acceleration output</summary>
		private double PowerOutput
		{
			get
			{
				Regulator regulator = Car.baseTrain.Handles.Power as Regulator;
				// ReSharper disable once PossibleNullReferenceException
				int curve = (int)Math.Ceiling(regulator.Ratio * (AccelerationCurves.Length - 1));
				return AccelerationCurves[curve].GetAccelerationOutput(Car.CurrentSpeed, 1.0);
			}
		}

		public SteamEngine(CarBase car, double jerkPowerUp, double jerkPowerDown) : base(car)
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
			 *			0.005psi standing pressure loss (leakage etc.)
			 *			0.2psi base stroke pressure, before reduction due to regulator / cutoff
			 */
			CylinderChest = new CylinderChest(this, 0.005, 0.2);
			/*
			 * Cutoff
			 *			75% max forwards
			 *			50% max reverse
			 *			10% around zero where cutoff is ineffective (due to standing resistance etc.)
			 */
			Car.baseTrain.Handles.Reverser = new Cutoff(Car.baseTrain, 75, -50, 10);
			/*
			 * Tender:
			 *		Coal capacity of 40T
			 *		Water capacity of 88,000L (~19,200 gallons)
			 */
			Tender = new Tender(40000, 40000, 88000, 88000);
			

			JerkPowerUp = jerkPowerUp;
			JerkPowerDown = jerkPowerDown;
		}

		private double lastTrackPosition;

		public override void Update(double TimeElapsed, out double Speed)
		{
			Cutoff cutoff = Car.baseTrain.Handles.Reverser as Cutoff;
			// update the boiler pressure and associated gubbins first
			Boiler.Update(TimeElapsed);
			// get the distance travelled & convert to piston strokes
			CylinderChest.Update(TimeElapsed, Car.FrontAxle.Follower.TrackPosition - lastTrackPosition);
			lastTrackPosition = Car.FrontAxle.Follower.TrackPosition;

			double adjustedFrictionBrakeAcceleration = FrictionBrakeAcceleration;
			double adjustedPowerRollingCouplerAcceleration = PowerRollingCouplerAcceleration;
			// power
			double wheelspin = 0.0;
			double wheelSlipAccelerationMotorFront = 0.0;
			double wheelSlipAccelerationMotorRear = 0.0;
			double wheelSlipAccelerationBrakeFront = 0.0;
			double wheelSlipAccelerationBrakeRear = 0.0;
			if (!Car.Derailed)
			{
				wheelSlipAccelerationMotorFront = Car.FrontAxle.CriticalWheelSlipAccelerationForElectricMotor(TrainManagerBase.CurrentRoute.Atmosphere.AccelerationDueToGravity);
				wheelSlipAccelerationMotorRear = Car.RearAxle.CriticalWheelSlipAccelerationForElectricMotor(TrainManagerBase.CurrentRoute.Atmosphere.AccelerationDueToGravity);
				wheelSlipAccelerationBrakeFront = Car.FrontAxle.CriticalWheelSlipAccelerationForFrictionBrake(TrainManagerBase.CurrentRoute.Atmosphere.AccelerationDueToGravity);
				wheelSlipAccelerationBrakeRear = Car.RearAxle.CriticalWheelSlipAccelerationForFrictionBrake(TrainManagerBase.CurrentRoute.Atmosphere.AccelerationDueToGravity);
			}

			if (DecelerationDueToTraction == 0.0)
			{
				double a;
				if (cutoff.Actual != 0 & Car.baseTrain.Handles.Power.Actual > 0 & !Car.baseTrain.Handles.HoldBrake.Actual & !Car.baseTrain.Handles.EmergencyBrake.Actual)
				{
					// target acceleration
					a = PowerOutput;

					BveReAdhesionDevice reAdhesionDevice = Car.ReAdhesionDevice as BveReAdhesionDevice;
					if (reAdhesionDevice != null)
					{
						// readhesion device
						if (a > reAdhesionDevice.MaximumAccelerationOutput)
						{
							a = reAdhesionDevice.MaximumAccelerationOutput;
						}
					}

					// wheel slip
					if (a < wheelSlipAccelerationMotorFront)
					{
						Car.FrontAxle.CurrentWheelSlip = false;
					}
					else
					{
						Car.FrontAxle.CurrentWheelSlip = true;
						wheelspin += (double)Car.baseTrain.Handles.Reverser.Actual * a * Car.CurrentMass;
					}

					if (a < wheelSlipAccelerationMotorRear)
					{
						Car.RearAxle.CurrentWheelSlip = false;
					}
					else
					{
						Car.RearAxle.CurrentWheelSlip = true;
						wheelspin += (double)Car.baseTrain.Handles.Reverser.Actual * a * Car.CurrentMass;
					}

					// Update readhesion device
					Car.ReAdhesionDevice.Update(a);
					// Update constant speed device

					Car.ConstSpeed.Update(ref a, Car.baseTrain.Specs.CurrentConstSpeed, (ReverserPosition)Car.baseTrain.Handles.Reverser.Actual);

					// finalize
					if (wheelspin != 0.0) a = 0.0;
				}
				else
				{
					a = 0.0;
					Car.FrontAxle.CurrentWheelSlip = false;
					Car.RearAxle.CurrentWheelSlip = false;
				}


				if (!Car.Derailed)
				{
					if (MotorAcceleration < a)
					{
						if (MotorAcceleration < 0.0)
						{
							MotorAcceleration += Car.CarBrake.JerkDown * TimeElapsed;
						}
						else
						{
							MotorAcceleration += JerkPowerUp * TimeElapsed;
						}

						if (MotorAcceleration > a)
						{
							MotorAcceleration = a;
						}
					}
					else
					{
						MotorAcceleration -= JerkPowerDown * TimeElapsed;
						if (MotorAcceleration < a)
						{
							MotorAcceleration = a;
						}
					}
				}
				else
				{
					MotorAcceleration = 0.0;
				}
			}

			// brake
			bool wheellock = wheelspin == 0.0 & Car.Derailed;
			if (!Car.Derailed & wheelspin == 0.0)
			{
				double a;
				// motor
				if (DecelerationDueToTraction != 0.0)
				{
					a = -DecelerationDueToTraction;
					if (MotorAcceleration > a)
					{
						if (MotorAcceleration > 0.0)
						{
							MotorAcceleration -= JerkPowerDown * TimeElapsed;
						}
						else
						{
							MotorAcceleration -= Car.CarBrake.JerkUp * TimeElapsed;
						}

						if (MotorAcceleration < a)
						{
							MotorAcceleration = a;
						}
					}
					else
					{
						MotorAcceleration += Car.CarBrake.JerkDown * TimeElapsed;
						if (MotorAcceleration > a)
						{
							MotorAcceleration = a;
						}
					}
				}

				// brake
				a = DecelerationDueToBrake;
				if (Car.CurrentSpeed >= -0.01 & Car.CurrentSpeed <= 0.01)
				{
					double rf = Car.FrontAxle.Follower.WorldDirection.Y;
					double rr = Car.RearAxle.Follower.WorldDirection.Y;
					double ra = Math.Abs(0.5 * (rf + rr) *
					                     TrainManagerBase.CurrentRoute.Atmosphere.AccelerationDueToGravity);
					if (a > ra) a = ra;
				}

				double factor = Car.EmptyMass / Car.CurrentMass;
				if (a >= wheelSlipAccelerationBrakeFront)
				{
					wheellock = true;
				}
				else
				{
					adjustedFrictionBrakeAcceleration += 0.5 * a * factor;
				}

				if (a >= wheelSlipAccelerationBrakeRear)
				{
					wheellock = true;
				}
				else
				{
					adjustedFrictionBrakeAcceleration += 0.5 * a * factor;
				}
			}
			else if (Car.Derailed)
			{
				adjustedFrictionBrakeAcceleration += TrainBase.CoefficientOfGroundFriction * TrainManagerBase.CurrentRoute.Atmosphere.AccelerationDueToGravity;
			}

			// motor
			if (Car.baseTrain.Handles.Reverser.Actual != 0)
			{
				double factor = Car.EmptyMass / Car.CurrentMass;
				if (MotorAcceleration > 0.0)
				{
					adjustedPowerRollingCouplerAcceleration += (double) Car.baseTrain.Handles.Reverser.Actual * MotorAcceleration * factor;
				}
				else
				{
					double a = -MotorAcceleration;
					if (a >= wheelSlipAccelerationMotorFront)
					{
						Car.FrontAxle.CurrentWheelSlip = true;
					}
					else if (!Car.Derailed)
					{
						adjustedFrictionBrakeAcceleration += 0.5 * a * factor;
					}

					if (a >= wheelSlipAccelerationMotorRear)
					{
						Car.RearAxle.CurrentWheelSlip = true;
					}
					else
					{
						adjustedFrictionBrakeAcceleration += 0.5 * a * factor;
					}
				}
			}
			else
			{
				MotorAcceleration = 0.0;
			}

			
			// perceived speed
			{
				double target;
				if (wheellock)
				{
					target = 0.0;
				}
				else if (wheelspin == 0.0)
				{
					target = Car.CurrentSpeed;
				}
				else
				{
					target = Car.CurrentSpeed + wheelspin / 2500.0;
				}

				double diff = target - Car.Specs.PerceivedSpeed;
				double rate = (diff < 0.0 ? 5.0 : 1.0) * TrainManagerBase.CurrentRoute.Atmosphere.AccelerationDueToGravity *
				              TimeElapsed;
				rate *= 1.0 - 0.7 / (diff * diff + 1.0);
				double factor = rate * rate;
				factor = 1.0 - factor / (factor + 1000.0);
				rate *= factor;
				if (diff >= -rate & diff <= rate)
				{
					Car.Specs.PerceivedSpeed = target;
				}
				else
				{
					Car.Specs.PerceivedSpeed += rate * Math.Sign(diff);
				}
			}
			// calculate new speed
			{
				int d = Math.Sign(Car.CurrentSpeed);
				double a = adjustedPowerRollingCouplerAcceleration;
				double b = adjustedFrictionBrakeAcceleration;
				if (Math.Abs(a) < b)
				{
					if (Math.Sign(a) == d)
					{
						if (d == 0)
						{
							Speed = 0.0;
						}
						else
						{
							double c = (b - Math.Abs(a)) * TimeElapsed;
							if (Math.Abs(Car.CurrentSpeed) > c)
							{
								Speed = Car.CurrentSpeed - d * c;
							}
							else
							{
								Speed = 0.0;
							}
						}
					}
					else
					{
						double c = (Math.Abs(a) + b) * TimeElapsed;
						if (Math.Abs(Car.CurrentSpeed) > c)
						{
							Speed = Car.CurrentSpeed - d * c;
						}
						else
						{
							Speed = 0.0;
						}
					}
				}
				else
				{
					Speed = Car.CurrentSpeed + (a - b * d) * TimeElapsed;
				}
			}
			if (Sounds != null)
			{
				Sounds.Update(TimeElapsed);
			}
		}

		public override void HandleKeyDown(Translations.Command command)
		{
			switch (command)
			{
				case Translations.Command.Blowers:
					if (Boiler.Blowers.Active)
					{
						if (Boiler.Blowers.StartSound != null)
						{
							Boiler.Blowers.StartSound.Play(Car, false);
						}
					}
					else
					{
						if (Boiler.Blowers.StopSound != null)
						{
							Boiler.Blowers.StopSound.Play(Car, false);
						}
					}

					Boiler.Blowers.Active = !Boiler.Blowers.Active;
					break;
				case Translations.Command.LiveSteamInjector:
					if (Boiler.LiveSteamInjector.Active)
					{
						if (Boiler.LiveSteamInjector.StartSound != null)
						{
							Boiler.LiveSteamInjector.StartSound.Play(Car, false);
						}
					}
					else
					{
						if (Boiler.LiveSteamInjector.StopSound != null)
						{
							Boiler.LiveSteamInjector.StopSound.Play(Car, false);
						}
					}

					Boiler.LiveSteamInjector.Active = !Boiler.LiveSteamInjector.Active;
					break;
				case Translations.Command.ExhaustSteamInjector:
					if (Boiler.ExhaustSteamInjector.Active)
					{
						if (Boiler.ExhaustSteamInjector.StartSound != null)
						{
							Boiler.ExhaustSteamInjector.StartSound.Play(Car, false);
						}
					}
					else
					{
						if (Boiler.ExhaustSteamInjector.StopSound != null)
						{
							Boiler.ExhaustSteamInjector.StopSound.Play(Car, false);
						}
					}

					Boiler.ExhaustSteamInjector.Active = !Boiler.ExhaustSteamInjector.Active;
					break;

			}
		}
	}
}