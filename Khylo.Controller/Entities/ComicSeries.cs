#nullable disable

#pragma warning disable CS1591

using System.Text.Json.Serialization;
using Khylo.Data.Enums;

namespace Khylo.Controller.Entities
{
    /// <summary>
    /// Represents a comic series containing issues and volumes.
    /// </summary>
    [Common.RequiresSourceSerialisation]
    public class ComicSeries : Folder
    {
        [JsonIgnore]
        public override bool SupportsAddingToPlaylist => false;

        [JsonIgnore]
        public override bool SupportsDateLastMediaAdded => true;

        [JsonIgnore]
        public override bool SupportsInheritedParentImages => false;

        [JsonIgnore]
        public override bool SupportsPeople => true;

        /// <summary>
        /// Gets or sets the comic publisher.
        /// </summary>
        public string Publisher { get; set; }

        /// <summary>
        /// Gets or sets publication status (e.g., Continuing, Ended).
        /// </summary>
        public string Status { get; set; }

        /// <inheritdoc />
        public override bool CanDelete() => true;

        /// <inheritdoc />
        public override UnratedItem GetBlockUnratedType() => UnratedItem.Book;
    }
}
