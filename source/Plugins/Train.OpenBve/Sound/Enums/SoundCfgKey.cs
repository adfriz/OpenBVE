namespace Train.OpenBve
{
	/// <summary>The keys in a sound.cfg file</summary>
	internal enum SoundCfgKey
	{
		Unknown = 0,
		// brake
		BcReleaseHigh,
		BcRelease,
		BcReleaseFull,
		Emergency,
		EmergencyRelease,
		BpDecomp,
		// compressor
		Attack,
		Loop,
		Release,
		// suspension
		Left,
		Right,
		// horn
		PrimaryStart,
		PrimaryEnd,
		PrimaryRelease,
		PrimaryLoop,
		Primary,
		SecondaryStart,
		SecondaryEnd,
		SecondaryRelease,
		SecondaryLoop,
		Secondary,
		MusicStart,
		MusicEnd,
		MusicRelease,
		MusicLoop,
		Music,
		// door
		OpenLeft,
		OpenRight,
		CloseLeft,
		CloseRight,
		// buzzer
		Correct,
		// pilot lamp
		On,
		Off,
		// brake handle
		Apply,
		ApplyFast,
		// **release** duplicated above
		ReleaseFast,
		Min,
		Max,
		// master controller
		Up,
		UpFast,
		Down,
		DownFast,
		// **min** and **max** duplicated above
		// reverser + breaker **on** and **off** duplicated above
		//others
		Noise,
		Shoe,
		Halt,
		// windscreen
		RainDrop,
		WetWipe,
		DryWipe,
		Switch
	}
}
