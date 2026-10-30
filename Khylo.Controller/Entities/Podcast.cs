#nullable disable

#pragma warning disable CS1591

using System;
using System.Text.Json.Serialization;
using Khylo.Controller.Providers;
using Khylo.Data.Enums;

namespace Khylo.Controller.Entities
{
    /// <summary>
    /// Represents a syndicated podcast series or channel.
    /// </summary>
    [Common.RequiresSourceSerialisation]
    public class Podcast : Folder, IHasLookupInfo<PodcastInfo>
    {
        [JsonIgnore]
        public override bool SupportsAddingToPlaylist => true;

        [JsonIgnore]
        public override bool SupportsDateLastMediaAdded => true;

        [JsonIgnore]
        public override bool SupportsInheritedParentImages => false;

        [JsonIgnore]
        public override bool SupportsPeople => true;

        /// <summary>
        /// Gets or sets the RSS or Atom syndication feed URL.
        /// </summary>
        public string FeedUrl { get; set; }

        /// <summary>
        /// Gets or sets the podcast website URL.
        /// </summary>
        public string WebsiteUrl { get; set; }

        /// <summary>
        /// Gets or sets the author / creator of the podcast.
        /// </summary>
        public string Author { get; set; }

        /// <summary>
        /// Gets or sets the last feed synchronization timestamp.
        /// </summary>
        public DateTime? LastSyncDate { get; set; }

        /// <inheritdoc />
        public override bool CanDelete() => true;

        /// <inheritdoc />
        public override UnratedItem GetBlockUnratedType() => UnratedItem.Other;

        public PodcastInfo GetLookupInfo()
        {
            var info = GetItemLookupInfo<PodcastInfo>();
            info.FeedUrl = FeedUrl;
            return info;
        }
    }
}
