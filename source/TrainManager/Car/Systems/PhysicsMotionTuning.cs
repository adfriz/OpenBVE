namespace TrainManager.Car.Systems
{
	/// <summary>Default tuning for the visual-only physics motion (carbody feel).</summary>
	/// <remarks>
	/// Values are modelled on measured vehicle behaviour (body natural frequencies,
	/// damping and peak amplitudes). All cars share these defaults so existing
	/// addons work with no edits; per-car Scale / Enable flags adjust them.
	/// Angles in degrees here are converted to radians inside PhysicsMotion.
	/// Sources (all independent engineering references):
	/// - Body frequencies / damping: soft secondary suspensions place rigid-body
	///   modes from 1.5 Hz down to below 1 Hz:
	///   https://rosap.ntl.bts.gov/view/dot/11695/dot_11695_DS1.pdf
	///   Rigid bounce/pitch/roll modes around 1 Hz:
	///   https://pmc.ncbi.nlm.nih.gov/articles/PMC8914648
	///   EMU model natural modes: sway 0.46 Hz, bounce 0.81 Hz, pitch 2.08 Hz:
	///   https://www.kais99.org/jkais/journal/Vol25No01/vol25no01p16.pdf
	///   Hunting-stability analyses work with damping ratios around 5%:
	///   https://www.china-simulation.com/CN/10.16182/j.issn1004731x.joss.20-0237
	/// - Amplitudes: tuned starting points - verify against cab-ride footage of the
	///   stock being modelled. Straight outputs (~0.05-0.26 m/s^2 RMS) sit inside the
	///   ISO 2631 "comfortable" band (<= 0.315 m/s^2):
	///   https://pmc.ncbi.nlm.nih.gov/articles/PMC10304163
	///   Measured vertical track irregularity of 1.524 mm RMS anchors bounce scale:
	///   https://ntrs.nasa.gov/api/citations/19720003315/downloads/19720003315.pdf
	/// - Curve lean from equilibrium cant minus applied cant:
	///   https://www.thepwi.org/wp-content/uploads/2022/04/Journal-2022_04-Vol140-Pt2_Back-to-basics-Equilibrium-cant.pdf
	///   https://railwaytrackblog.com/2015/10/29/11-82_cant-deficiency-un-compensated-acceleration-pway/
	/// - Joint spacing 25 m is one standard rail length (rails supplied 12-25 m):
	///   https://railroadrails.com/railroad-rail-specification
	///   On joints as periodic weak points before welded rail:
	///   https://onlinepubs.trb.org/Onlinepubs/trr/1991/1289/1289-002.pdf
	/// - Peak-to-velocity (first peak of a damped spring) and white-noise-driven
	///   spring variance are standard vibration-theory results.
	/// </remarks>
	public static class PhysicsMotionTuning
	{
		// Body natural frequencies / damping (shared by curve, switch and straight)
		public const double RollFrequencyHz = 0.75;
		public const double RollDamping = 0.10;
		public const double SwayFrequencyHz = 0.85;
		public const double SwayDamping = 0.10;
		public const double BounceFrequencyHz = 2.10;
		public const double BounceDamping = 0.30;
		public const double PitchFrequencyHz = 0.72;
		public const double PitchDamping = 0.14;
		public const double ShiftFrequencyHz = 0.78;
		public const double ShiftDamping = 0.28;

		// Output caps (visual only, never affects physics)
		public const double MaxRollDeg = 3.0;
		public const double MaxSwayM = 0.085;
		public const double MaxBounceM = 0.012;
		public const double MaxPitchDeg = 0.65;
		public const double MaxShiftM = 0.040;

		// Curve: sustained lean + entry jolt from cant deficiency
		public const double LeanRollDeg = 1.20;
		public const double LeanSwayM = 0.030;
		public const double PeakRollDeg = 0.40;
		public const double PeakSwayM = 0.012;
		public const double SaturationMm = 50.0;
		public const double InputFilterHz = 1.6;
		public const double MinSpeedMps = 4.0;
		public const double MinDeficiencyMm = 5.0;
		public const double ExitResponseFactor = 0.62;

		// Straight: random track-unevenness shake
		public const double StraightDefaultScale = 0.70;
		public const double StraightRollStdDeg = 0.12;
		public const double StraightSwayStdM = 0.004;
		public const double StraightBounceStdM = 0.0015;
		public const double StraightMinSpeedKmh = 3.0;
		public const double StraightReferenceSpeedKmh = 50.0;

		// Turnout (switch frog) impacts
		public const double TurnoutMinSpeedFactor = 0.45;
		public const double TurnoutFalloffKmh = 60.0;
		public const double ToeRollDeg = 0.20;
		public const double ToeSwayM = 0.005;
		public const double ToeBounceVel = 0.038;
		public const double FrogRollDeg = 0.60;
		public const double FrogSwayM = 0.015;
		public const double FrogBounceVel = 0.128;

		// Rail joints (plain track, distance based + point sounds)
		public const double JointSpacingM = 25.0;
		public const double JointRollDeg = 0.12;
		public const double JointSwayM = 0.003;
		public const double JointBounceVel = 0.025;

		// Braking / stop nod
		public const double StopMinDecel = 0.75;
		public const double EmergencyDecel = 1.15;
		public const double EmergencyJerk = 2.0;
		public const double StopPitchPeakDeg = 0.45;
		public const double StopShiftPeakM = 0.026;
		public const double EmergencyPitchPeakDeg = 0.18;
		public const double EmergencyShiftPeakM = 0.020;

		// Passenger load tilt + levelling valve
		public const double LoadRollDeg = 0.12;
		public const double LoadLevelTimeS = 3.5;
		public const double LoadSmoothS = 0.30;
		public const double LoadKickRollDeg = 0.10;
		public const double LoadKickSinkM = 0.002;
		public const double LoadMaxRollDeg = 0.80;
		public const double LoadMaxSinkM = 0.010;
	}
}
