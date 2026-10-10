using System;

namespace TrainManager.Car.Systems
{
	/// <summary>
	/// How the car body sits on its springs. Leans into curves and springs back out,
	/// shakes on jointed straight track, kicks over turnouts, nods under the brakes and
	/// settles a little when the passengers pile in.
	/// Purely cosmetic - CarBase applies the results to the body it draws, and the
	/// physics never sees them.
	/// </summary>
	public class PhysicsMotion
	{
		// Angles in radians, the rest in meters
		public double RollAngle;
		public double Sway;
		public double Bounce;
		public double PitchAngle;
		public double Shift;

		private double rollPos;
		private double rollVel;
		private double swayPos;
		private double swayVel;
		private double bouncePos;
		private double bounceVel;
		private double pitchPos;
		private double pitchVel;
		private double shiftPos;
		private double shiftVel;

		private double leanRoll;
		private double leanSway;
		private double loadRoll;
		private double loadSink;

		private double prevRadius;
		private double prevLeanRoll;
		private double recentDecel;
		private double brakePitch;
		private double brakeShift;
		private MotionState motion = MotionState.Stopped;
		private bool jointsInitialised;
		private long lastFrontCell;
		private long lastRearCell;
		private bool prevDoorsOpen;
		private double prevCargoMass;
		private double loadKickCooldown;

		/// <summary>Which way the train is going, as far as the body is concerned.</summary>
		/// <remarks>
		/// Stopping and running backwards sweep through the same speeds, so the only way
		/// to tell them apart is to remember which way we were already travelling. Getting
		/// this wrong makes the car kick as though it had stopped when the driver simply
		/// changed direction.
		/// </remarks>
		private enum MotionState
		{
			Stopped,
			RollingForward,
			RollingReverse
		}

		private readonly Random rng;
		private bool hasSpareGaussian;
		private double spareGaussian;

		// Precomputed noise sigmas (velocity drive per sqrt(s))
		private readonly double noiseRoll;
		private readonly double noiseSway;
		private readonly double noiseBounce;
		// Precomputed peak velocities
		private readonly double curveRollPeakVel;
		private readonly double curveSwayPeakVel;
		private readonly double frogRollPeakVel;
		private readonly double frogSwayPeakVel;
		private readonly double jointRollPeakVel;
		private readonly double jointSwayPeakVel;
		private readonly double loadKickRollVel;
		private readonly double loadKickSinkVel;

		private const double DegToRad = Math.PI / 180.0;
		private const double Gravity = 9.80665;

		public PhysicsMotion(int seed)
		{
			rng = new Random(seed != 0 ? seed : 1);
			noiseRoll = NoiseForStd(PhysicsMotionTuning.StraightRollStdDeg * DegToRad, PhysicsMotionTuning.RollFrequencyHz, PhysicsMotionTuning.RollDamping);
			noiseSway = NoiseForStd(PhysicsMotionTuning.StraightSwayStdM, PhysicsMotionTuning.SwayFrequencyHz, PhysicsMotionTuning.SwayDamping);
			noiseBounce = NoiseForStd(PhysicsMotionTuning.StraightBounceStdM, PhysicsMotionTuning.BounceFrequencyHz, PhysicsMotionTuning.BounceDamping);
			curveRollPeakVel = PeakToVelocity(PhysicsMotionTuning.PeakRollDeg * DegToRad, PhysicsMotionTuning.RollFrequencyHz, PhysicsMotionTuning.RollDamping);
			curveSwayPeakVel = PeakToVelocity(PhysicsMotionTuning.PeakSwayM, PhysicsMotionTuning.SwayFrequencyHz, PhysicsMotionTuning.SwayDamping);
			frogRollPeakVel = PeakToVelocity(PhysicsMotionTuning.FrogRollDeg * DegToRad, PhysicsMotionTuning.RollFrequencyHz, PhysicsMotionTuning.RollDamping);
			frogSwayPeakVel = PeakToVelocity(PhysicsMotionTuning.FrogSwayM, PhysicsMotionTuning.SwayFrequencyHz, PhysicsMotionTuning.SwayDamping);
			jointRollPeakVel = PeakToVelocity(PhysicsMotionTuning.JointRollDeg * DegToRad, PhysicsMotionTuning.RollFrequencyHz, PhysicsMotionTuning.RollDamping);
			jointSwayPeakVel = PeakToVelocity(PhysicsMotionTuning.JointSwayM, PhysicsMotionTuning.SwayFrequencyHz, PhysicsMotionTuning.SwayDamping);
			loadKickRollVel = PeakToVelocity(PhysicsMotionTuning.LoadKickRollDeg * DegToRad, PhysicsMotionTuning.RollFrequencyHz, PhysicsMotionTuning.RollDamping);
			loadKickSinkVel = PeakToVelocity(PhysicsMotionTuning.LoadKickSinkM, PhysicsMotionTuning.BounceFrequencyHz, PhysicsMotionTuning.BounceDamping);
		}

		public PhysicsMotion() : this(Environment.TickCount)
		{
		}

		public void Reset()
		{
			RollAngle = Sway = Bounce = PitchAngle = Shift = 0.0;
			rollPos = rollVel = swayPos = swayVel = bouncePos = bounceVel = 0.0;
			pitchPos = pitchVel = shiftPos = shiftVel = 0.0;
			leanRoll = leanSway = loadRoll = loadSink = 0.0;
			prevRadius = prevLeanRoll = recentDecel = 0.0;
			brakePitch = brakeShift = 0.0;
			motion = MotionState.Stopped;
			loadKickCooldown = 0.0;
			jointsInitialised = false;
			hasSpareGaussian = false;
			prevDoorsOpen = false;
			prevCargoMass = 0.0;
		}

		/// <summary>Called when an axle crosses a point-sound spot (joint or switch).</summary>
		public void OnPointTrigger(bool isSwitch, double speedMps, double scale)
		{
			if (scale <= 0.0) return;
			double speedKmh = Math.Abs(speedMps) * 3.6;
			if (speedKmh < PhysicsMotionTuning.JointMinSpeedKmh) return;
			// Lorentzian falloff with speed (full effect slow, minimum retained fast)
			double r = speedKmh / PhysicsMotionTuning.TurnoutFalloffKmh;
			double factor = PhysicsMotionTuning.TurnoutMinSpeedFactor +
				(1.0 - PhysicsMotionTuning.TurnoutMinSpeedFactor) / (1.0 + r * r);
			factor *= scale;
			double side = rng.NextDouble() < 0.5 ? -1.0 : 1.0;
			if (isSwitch)
			{
				// Sway takes the opposite sign to roll (matches the render convention)
				rollVel += frogRollPeakVel * factor * side;
				swayVel -= frogSwayPeakVel * factor * side;
				bounceVel += PhysicsMotionTuning.FrogBounceVel * factor;
			}
			else
			{
				// Plain rail joint from route point sound
				rollVel += jointRollPeakVel * factor * side;
				swayVel -= jointSwayPeakVel * factor * side;
				bounceVel += PhysicsMotionTuning.JointBounceVel * factor;
			}
		}

		public void Update(double dt, double signedSpeedMps, double acceleration, double frontRadius, double rearRadius,
			double frontCant, double rearCant, double gauge, double accuracy,
			bool leftDoorsOpen, bool rightDoorsOpen, double cargoMass,
			double frontTrackPos, double rearTrackPos, double scale)
		{
			if (dt <= 0.0 || scale <= 0.0)
			{
				if (scale <= 0.0)
				{
					// Disabled: decay to neutral quickly
					DecayToZero(dt <= 0.0 ? 0.016 : Math.Min(dt, 0.05));
				}
				Publish();
				return;
			}
			// A long frame still gets integrated, just not all of it. Stepping in 5 ms slices
			// keeps this stable however far the time is cut back to.
			if (dt > 0.25) dt = 0.25;

			double speedAbs = Math.Abs(signedSpeedMps);
			double speedKmh = speedAbs * 3.6;

			// ---- Curve: cant deficiency -> sustained lean + entry/exit kicks ----
			double curR = CombineRadius(frontRadius, rearRadius);
			double avgCant = 0.5 * (frontCant + rearCant);
			double deficiencyMm = 0.0;
			if (curR != 0.0 && speedAbs >= 0.1 && gauge > 0.1)
			{
				double eqCant = signedSpeedMps * signedSpeedMps * gauge / (Gravity * curR);
				deficiencyMm = (eqCant - avgCant) * 1000.0;
			}
			double targetLeanRollDeg = 0.0;
			double targetLeanSwayM = 0.0;
			if (curR != 0.0 && speedAbs >= PhysicsMotionTuning.MinSpeedMps && Math.Abs(deficiencyMm) >= PhysicsMotionTuning.MinDeficiencyMm)
			{
				double k = Tanh(deficiencyMm / PhysicsMotionTuning.SaturationMm);
				targetLeanRollDeg = PhysicsMotionTuning.LeanRollDeg * k;
				// Sway opposes roll (same render convention as the switch kicks)
				targetLeanSwayM = -PhysicsMotionTuning.LeanSwayM * k;
			}
			double filterRate = Math.PI * 2.0 * PhysicsMotionTuning.InputFilterHz;
			double kf = 1.0 - Math.Exp(-dt * filterRate);
			leanRoll += (targetLeanRollDeg * DegToRad - leanRoll) * kf;
			leanSway += (targetLeanSwayM - leanSway) * kf;

			// Entry / exit / S-curve detection (radius is stepwise per element, so edges are clean)
			// Sway takes the opposite sign to roll, same convention as above
			// A bogie barely notices a curve at walking pace, so the jolt fades in with speed
			// rather than arriving at full strength the moment the axle crosses over.
			double kick = Clamp(speedAbs / PhysicsMotionTuning.MinSpeedMps, 0.0, 1.0) * scale;
			if (prevRadius == 0.0 && curR != 0.0)
			{
				double dir = targetLeanRollDeg != 0.0 ? Math.Sign(targetLeanRollDeg) : -Math.Sign(curR);
				rollVel += curveRollPeakVel * dir * kick;
				swayVel -= curveSwayPeakVel * dir * kick;
			}
			else if (prevRadius != 0.0 && curR == 0.0)
			{
				double dir = prevLeanRoll != 0.0 ? Math.Sign(prevLeanRoll) : Math.Sign(prevRadius);
				rollVel -= curveRollPeakVel * PhysicsMotionTuning.ExitResponseFactor * dir * kick;
				swayVel += curveSwayPeakVel * PhysicsMotionTuning.ExitResponseFactor * dir * kick;
			}
			else if (prevRadius != 0.0 && curR != 0.0 && Math.Sign(prevRadius) != Math.Sign(curR))
			{
				double oldDir = prevLeanRoll != 0.0 ? Math.Sign(prevLeanRoll) : Math.Sign(prevRadius);
				rollVel -= curveRollPeakVel * PhysicsMotionTuning.ExitResponseFactor * oldDir * kick;
				swayVel += curveSwayPeakVel * PhysicsMotionTuning.ExitResponseFactor * oldDir * kick;
				double newDir = targetLeanRollDeg != 0.0 ? Math.Sign(targetLeanRollDeg) : -Math.Sign(curR);
				rollVel += curveRollPeakVel * newDir * kick;
				swayVel -= curveSwayPeakVel * newDir * kick;
			}
			prevRadius = curR;
			prevLeanRoll = targetLeanRollDeg;

			// ---- Straight-track random shake ----
			double accuracyFactor = Clamp(accuracy / 2.0, 0.0, 2.0);
			double speedFactor = 0.0;
			if (speedKmh >= PhysicsMotionTuning.StraightMinSpeedKmh)
			{
				speedFactor = 1.0 - Math.Exp(-(speedKmh - PhysicsMotionTuning.StraightMinSpeedKmh) / PhysicsMotionTuning.StraightReferenceSpeedKmh);
			}
			double gain = PhysicsMotionTuning.StraightDefaultScale * accuracyFactor * speedFactor * scale;
			if (gain > 0.0)
			{
				double root = Math.Sqrt(dt) * gain;
				rollVel += noiseRoll * root * Gaussian();
				swayVel += noiseSway * root * Gaussian();
				bounceVel += noiseBounce * root * Gaussian();
			}

			// ---- Distance-based rail joints (covers plain track with no events) ----
			if (speedKmh >= PhysicsMotionTuning.JointMinSpeedKmh && accuracy > 0.01)
			{
				double spacing = PhysicsMotionTuning.JointSpacingM;
				long fc = (long)Math.Floor(frontTrackPos / spacing);
				long rc = (long)Math.Floor(rearTrackPos / spacing);
				if (!jointsInitialised)
				{
					lastFrontCell = fc;
					lastRearCell = rc;
					jointsInitialised = true;
				}
				else
				{
					if (fc != lastFrontCell)
					{
						lastFrontCell = fc;
						DistanceJointHit(speedAbs, accuracy, scale);
					}
					if (rc != lastRearCell)
					{
						lastRearCell = rc;
						DistanceJointHit(speedAbs, accuracy, scale);
					}
				}
			}

			// ---- Braking / acceleration load transfer ----
			// Acceleration comes from the physics rather than being differenced here. Our own
			// dt is clamped, so sampling speed across it and dividing would inflate the
			// result whenever a frame ran long.
			double decel = -acceleration;
			recentDecel += (decel - recentDecel) * (1.0 - Math.Exp(-dt * 2.0));

			// Work out how we're travelling. The two thresholds are deliberately far apart:
			// one to be counted as moving and one to still count as moving, so a train
			// coasting to a halt doesn't chatter across the boundary.
			if (speedAbs <= PhysicsMotionTuning.StopSpeedMps)
			{
				motion = MotionState.Stopped;
			}
			else if (speedAbs >= PhysicsMotionTuning.RollingSpeedMps)
			{
				motion = signedSpeedMps >= 0.0 ? MotionState.RollingForward : MotionState.RollingReverse;
			}

			// Pitch and shift follow the deceleration instead of being kicked by it. An
			// emergency pull lasts many seconds, and a single impulse gave the body one jolt
			// on the first frame and then nothing while the train was still stopping hard.
			// Driving a spring with a target lets it dive, hold and rebound on its own.
			double travelDir = motion == MotionState.RollingReverse ? -1.0 : 1.0;
			double load = Tanh(-recentDecel / PhysicsMotionTuning.EmergencyDecel) * travelDir;
			double pitchTarget = -load * PhysicsMotionTuning.LoadTransferPitchDeg * DegToRad;
			double shiftTarget = load * PhysicsMotionTuning.LoadTransferShiftM;
			// Positive pitch is nose up, so braking has to push the other way. Powering on
			// squats the nose up, and the same expression covers it for free.
			if (motion == MotionState.Stopped)
			{
				pitchTarget = 0.0;
				shiftTarget = 0.0;
			}
			double settle = 1.0 - Math.Exp(-dt / PhysicsMotionTuning.LoadTransferSmoothingS);
			brakePitch += (pitchTarget - brakePitch) * settle;
			brakeShift += (shiftTarget - brakeShift) * settle;

			// ---- Passenger load tilt + levelling ----
			bool doorsOpen = leftDoorsOpen || rightDoorsOpen;
			double doorTarget = 0.0;
			if (leftDoorsOpen && !rightDoorsOpen) doorTarget = PhysicsMotionTuning.LoadRollDeg * DegToRad;
			else if (rightDoorsOpen && !leftDoorsOpen) doorTarget = -PhysicsMotionTuning.LoadRollDeg * DegToRad;
			double loadRate = doorsOpen ? 1.0 / Math.Max(0.05, PhysicsMotionTuning.LoadSmoothS) : 1.0 / Math.Max(0.1, PhysicsMotionTuning.LoadLevelTimeS);
			loadRoll += (doorTarget - loadRoll) * (1.0 - Math.Exp(-dt * loadRate));
			double sinkTarget = -PhysicsMotionTuning.LoadMaxSinkM
				* Clamp(cargoMass / PhysicsMotionTuning.LoadRatedCargoMass, 0.0, 1.0);
			loadSink += (sinkTarget - loadSink) * (1.0 - Math.Exp(-dt * loadRate));
			loadKickCooldown -= dt;
			if (loadKickCooldown <= 0.0)
			{
				bool opened = doorsOpen && !prevDoorsOpen;
				bool massJump = Math.Abs(cargoMass - prevCargoMass) > 35.0 && doorsOpen;
				if (opened || massJump)
				{
					double side = leftDoorsOpen && !rightDoorsOpen ? 1.0 : rightDoorsOpen && !leftDoorsOpen ? -1.0 : (rng.NextDouble() < 0.5 ? -1.0 : 1.0);
					rollVel += loadKickRollVel * side * scale;
					bounceVel += loadKickSinkVel * scale;
					loadKickCooldown = 0.8;
				}
			}
			prevDoorsOpen = doorsOpen;
			prevCargoMass = cargoMass;

			// ---- Integrate springs ----
			StepSpring(ref rollPos, ref rollVel, PhysicsMotionTuning.RollFrequencyHz, PhysicsMotionTuning.RollDamping, dt);
			StepSpring(ref swayPos, ref swayVel, PhysicsMotionTuning.SwayFrequencyHz, PhysicsMotionTuning.SwayDamping, dt);
			StepSpring(ref bouncePos, ref bounceVel, PhysicsMotionTuning.BounceFrequencyHz, PhysicsMotionTuning.BounceDamping, dt);
			StepSpring(ref pitchPos, ref pitchVel, PhysicsMotionTuning.PitchFrequencyHz, PhysicsMotionTuning.PitchDamping, dt);
			StepSpring(ref shiftPos, ref shiftVel, PhysicsMotionTuning.ShiftFrequencyHz, PhysicsMotionTuning.ShiftDamping, dt);

			Publish();
		}

		private void DistanceJointHit(double speedAbs, double accuracy, double scale)
		{
			double speedF = Clamp(speedAbs / 12.5, 0.15, 1.0);
			double accF = Clamp(accuracy / 2.0, 0.25, 1.5);
			double f = speedF * accF * scale;
			double side = rng.NextDouble() < 0.5 ? -1.0 : 1.0;
			rollVel += jointRollPeakVel * f * side;
			swayVel -= jointSwayPeakVel * f * side;
			bounceVel += PhysicsMotionTuning.JointBounceVel * f;
		}

		private void DecayToZero(double dt)
		{
			if (dt <= 0.0) return;
			StepSpring(ref rollPos, ref rollVel, PhysicsMotionTuning.RollFrequencyHz, PhysicsMotionTuning.RollDamping, dt);
			StepSpring(ref swayPos, ref swayVel, PhysicsMotionTuning.SwayFrequencyHz, PhysicsMotionTuning.SwayDamping, dt);
			StepSpring(ref bouncePos, ref bounceVel, PhysicsMotionTuning.BounceFrequencyHz, PhysicsMotionTuning.BounceDamping, dt);
			StepSpring(ref pitchPos, ref pitchVel, PhysicsMotionTuning.PitchFrequencyHz, PhysicsMotionTuning.PitchDamping, dt);
			StepSpring(ref shiftPos, ref shiftVel, PhysicsMotionTuning.ShiftFrequencyHz, PhysicsMotionTuning.ShiftDamping, dt);
			double k = 1.0 - Math.Exp(-dt * 2.0);
			leanRoll += (0.0 - leanRoll) * k;
			leanSway += (0.0 - leanSway) * k;
			brakePitch += (0.0 - brakePitch) * k;
			brakeShift += (0.0 - brakeShift) * k;
			loadRoll += (0.0 - loadRoll) * k;
			loadSink += (0.0 - loadSink) * k;
		}

		private void Publish()
		{
			RollAngle = SoftLimit(leanRoll + rollPos + loadRoll, PhysicsMotionTuning.MaxRollDeg * DegToRad);
			Sway = SoftLimit(leanSway + swayPos, PhysicsMotionTuning.MaxSwayM);
			Bounce = Clamp(bouncePos + loadSink, -PhysicsMotionTuning.MaxBounceM - PhysicsMotionTuning.LoadMaxSinkM, PhysicsMotionTuning.MaxBounceM);
			PitchAngle = Clamp(pitchPos + brakePitch, -PhysicsMotionTuning.MaxPitchDeg * DegToRad, PhysicsMotionTuning.MaxPitchDeg * DegToRad);
			Shift = Clamp(shiftPos + brakeShift, -PhysicsMotionTuning.MaxShiftM, PhysicsMotionTuning.MaxShiftM);
		}

		private static double CombineRadius(double front, double rear)
		{
			if (front != 0.0 && rear != 0.0)
			{
				// Near-equal and opposite radii means an S-curve with the pivot between the
				// axles. Taking the sign of the sum gives zero there, which reads as "on
				// straight track" and is exactly where the body needs to move most, so fall
				// back to whichever axle is tighter.
				double combined = Math.Sqrt(Math.Abs(front * rear));
				if (Math.Abs(front) >= Math.Abs(rear))
				{
					return combined * Math.Sign(front);
				}
				return combined * Math.Sign(rear);
			}
			if (front != 0.0) return front;
			if (rear != 0.0) return rear;
			return 0.0;
		}

		private static void StepSpring(ref double pos, ref double vel, double freqHz, double damping, double dt)
		{
			double omega = Math.PI * 2.0 * freqHz;
			int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.005));
			double h = dt / steps;
			for (int i = 0; i < steps; i++)
			{
				double acc = -omega * omega * pos - 2.0 * damping * omega * vel;
				vel += acc * h;
				pos += vel * h;
			}
		}

		private static double PeakToVelocity(double peak, double freqHz, double damping)
		{
			double omega = Math.PI * 2.0 * freqHz;
			double zeta = Math.Min(Math.Max(damping, 0.0), 0.999);
			double root = Math.Sqrt(1.0 - zeta * zeta);
			double atten = zeta > 0.0 ? Math.Exp(-zeta / root * Math.Atan(root / zeta)) : 1.0;
			return peak * omega / atten;
		}

		private static double NoiseForStd(double std, double freqHz, double damping)
		{
			double omega = Math.PI * 2.0 * freqHz;
			return std * Math.Sqrt(4.0 * Math.Max(damping, 0.001) * omega * omega * omega);
		}

		private static double SoftLimit(double value, double limit)
		{
			double a = Math.Abs(value);
			double knee = limit * 0.72;
			if (a <= knee) return value;
			double room = limit - knee;
			double c = knee + room * (1.0 - Math.Exp(-(a - knee) / room));
			if (c > limit) c = limit;
			return value < 0.0 ? -c : c;
		}

		private static double Clamp(double v, double min, double max)
		{
			if (v < min) return min;
			if (v > max) return max;
			return v;
		}

		private static double Tanh(double v)
		{
			if (v > 20.0) return 1.0;
			if (v < -20.0) return -1.0;
			double e2 = Math.Exp(2.0 * v);
			return (e2 - 1.0) / (e2 + 1.0);
		}

		private double Gaussian()
		{
			if (hasSpareGaussian)
			{
				hasSpareGaussian = false;
				return spareGaussian;
			}
			double u1 = Math.Max(1e-12, rng.NextDouble());
			double u2 = rng.NextDouble();
			double r = Math.Sqrt(-2.0 * Math.Log(u1));
			double t = Math.PI * 2.0 * u2;
			spareGaussian = r * Math.Sin(t);
			hasSpareGaussian = true;
			return r * Math.Cos(t);
		}
	}
}
