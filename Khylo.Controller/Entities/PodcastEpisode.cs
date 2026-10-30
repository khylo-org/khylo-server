#nullable disable

#pragma warning disable CA1724, CS1591

using System;
using System.Text.Json.Serialization;
using Khylo.Data.Enums;

namespace Khylo.Controller.Entities
{
    /// <summary>
    /// Represents an individual audio episode of a podcast.
    /// </summary>
    [Common.RequiresSourceSerialisation]
    public class PodcastEpisode : Audio.Audio
    {
        // MediaType is intentionally inherited from Audio (MediaType.Audio) so playback treats episodes as audio.
        // Use the item Type to distinguish podcast episodes.

        [JsonIgnore]
        public override bool SupportsPositionTicksResume => true;

        [JsonIgnore]
        public override bool SupportsPlayedStatus => true;

        /// <summary>
        /// Gets or sets the episode number.
        /// </summary>
        public int? EpisodeNumber { get; set; }

        /// <summary>
        /// Gets or sets the season or cycle number.
        /// </summary>
        public int? SeasonNumber { get; set; }

        /// <summary>
        /// Gets or sets the enclosure URL for remote downloading / streaming.
        /// </summary>
        public string EnclosureUrl { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this episode contains explicit content.
        /// </summary>
        public bool IsExplicit { get; set; }

        public override bool CanDownload() => IsFileProtocol;

        public override UnratedItem GetBlockUnratedType() => UnratedItem.Other;
    }
}
