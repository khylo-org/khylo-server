namespace Khylo.Model.Configuration
{
    /// <summary>
    /// An enum representing client playback skip behaviors for detected segments (intro, credits).
    /// </summary>
    public enum PlaybackSkipMode
    {
        /// <summary>
        /// Skip functionality is disabled.
        /// </summary>
        Disabled = 0,

        /// <summary>
        /// Display a prompt/button on the client to allow skipping.
        /// </summary>
        ShowButton = 1,

        /// <summary>
        /// Automatically jump past the detected segment.
        /// </summary>
        AutoSkip = 2
    }
}
