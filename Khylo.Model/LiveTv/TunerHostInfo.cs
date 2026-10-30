#nullable disable
#pragma warning disable CS1591

namespace Khylo.Model.LiveTv
{
    public class TunerHostInfo
    {
        public TunerHostInfo()
        {
            AllowHWTranscoding = true;
            IgnoreDts = true;
            ReadAtNativeFramerate = false;
            AllowStreamSharing = true;
            AllowFmp4TranscodingContainer = false;
            FallbackMaxStreamingBitrate = 30000000;
            IncludedGroups = [];
            IncludedChannels = [];
            ExcludedChannels = [];
        }

        public string Id { get; set; }

        public string Url { get; set; }

        public string Type { get; set; }

        public string DeviceId { get; set; }

        public string FriendlyName { get; set; }

        public bool ImportFavoritesOnly { get; set; }

        public bool AllowHWTranscoding { get; set; }

        public bool AllowFmp4TranscodingContainer { get; set; }

        public bool AllowStreamSharing { get; set; }

        public int FallbackMaxStreamingBitrate { get; set; }

        public bool EnableStreamLooping { get; set; }

        public string Source { get; set; }

        public int TunerCount { get; set; }

        public string UserAgent { get; set; }

        public bool IgnoreDts { get; set; }

        public bool ReadAtNativeFramerate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether only picked channels of a playlist are imported
        /// (see <see cref="TunerChannelSelection"/>); when false every channel is imported.
        /// </summary>
        public bool EnableChannelSelection { get; set; }

        /// <summary>
        /// Gets or sets the playlist groups whose channels are imported.
        /// </summary>
        public string[] IncludedGroups { get; set; }

        /// <summary>
        /// Gets or sets channels imported even though their group isn't.
        /// </summary>
        public string[] IncludedChannels { get; set; }

        /// <summary>
        /// Gets or sets channels left out even though their group is imported.
        /// </summary>
        public string[] ExcludedChannels { get; set; }
    }
}
