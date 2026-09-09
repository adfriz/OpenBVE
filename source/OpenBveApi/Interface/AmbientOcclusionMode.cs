namespace OpenBveApi.Interface
{
    /// <summary>Available ambient occlusion modes for the post-processing chain.</summary>
    public enum AmbientOcclusionMode
    {
        /// <summary>Ambient occlusion disabled entirely.</summary>
        Off = 0,
        /// <summary>Scalable Ambient Occlusion — performance mode, tweakable.</summary>
        SAO = 1,
        /// <summary>Ground-Truth Ambient Occlusion — quality mode, tweakable.</summary>
        GTAO = 2
    }
}
