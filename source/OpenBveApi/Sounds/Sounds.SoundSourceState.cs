namespace OpenBveApi.Sounds
{
	/// <summary>Represents the state of a sound source.</summary>
	public enum SoundSourceState
	{
		/// <summary>The sound will start playing once in audible range. The internal sound handle is not yet valid.</summary>
		PlayPending,
		/// <summary>The sound is playing and the internal source handle is valid.</summary>
		Playing,
		/// <summary>The sound will stop playing. The internal sound handle is still valid.</summary>
		StopPending,
		/// <summary>The sound has stopped and will be removed from the list of sound sources. The internal source handle is not valid any longer.</summary>
		Stopped,
		/// <summary>The sound is not yet in audible range, but is paused</summary>
		PausePending,
		/// <summary>The sound is paused</summary>
		Paused,
		/// <summary>The sound is pending resuming</summary>
		ResumePending
	}
}
