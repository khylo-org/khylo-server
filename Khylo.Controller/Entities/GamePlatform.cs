#nullable disable

#pragma warning disable CS1591

using System.Text.Json.Serialization;
using Khylo.Data.Enums;

namespace Khylo.Controller.Entities
{
    /// <summary>
    /// Represents a video game platform or console system folder (e.g. "Nintendo Entertainment System", "Super Nintendo", "Sega Genesis").
    /// </summary>
    [Common.RequiresSourceSerialisation]
    public class GamePlatform : Folder
    {
        [JsonIgnore]
        public override bool SupportsAddingToPlaylist => false;

        [JsonIgnore]
        public override bool SupportsDateLastMediaAdded => true;

        [JsonIgnore]
        public override bool SupportsInheritedParentImages => false;

        [JsonIgnore]
        public override bool SupportsPeople => false;

        /// <summary>
        /// Gets or sets the manufacturer / company name (e.g., Nintendo, Sega, Sony, Microsoft).
        /// </summary>
        public string Manufacturer { get; set; }

        /// <summary>
        /// Gets or sets the platform generation or release year.
        /// </summary>
        public int? Generation { get; set; }

        /// <inheritdoc />
        public override bool CanDelete() => true;

        /// <inheritdoc />
        public override UnratedItem GetBlockUnratedType() => UnratedItem.Game;
    }
}
