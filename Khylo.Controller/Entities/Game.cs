#nullable disable

#pragma warning disable CS1591

using System;
using System.Linq;
using System.Text.Json.Serialization;
using Khylo.Controller.Providers;
using Khylo.Data.Enums;

namespace Khylo.Controller.Entities
{
    /// <summary>
    /// Represents a playable video game ROM image.
    /// Excludes disc ISOs in Phase 1 per user scope directive.
    /// </summary>
    [Common.RequiresSourceSerialisation]
    public class Game : BaseItem, IHasLookupInfo<GameInfo>
    {
        [JsonIgnore]
        public override MediaType MediaType => MediaType.Game;

        public override bool SupportsPlayedStatus => true;

        public override bool SupportsPositionTicksResume => false;

        [JsonIgnore]
        public override bool SupportsPeople => true;

        /// <summary>
        /// Gets or sets the target platform or console system name (e.g. NES, SNES, Genesis, GBA).
        /// </summary>
        public string Platform { get; set; }

        /// <summary>
        /// Gets or sets the ROM checksum / hash (CRC32, MD5, SHA1).
        /// </summary>
        public string RomChecksum { get; set; }

        /// <summary>
        /// Gets or sets the game developer studio.
        /// </summary>
        public string Developer { get; set; }

        /// <summary>
        /// Gets or sets the game publisher.
        /// </summary>
        public string Publisher { get; set; }

        /// <summary>
        /// Gets or sets the supported number of players.
        /// </summary>
        public int? Players { get; set; }

        /// <inheritdoc />
        public override bool CanDownload() => IsFileProtocol;

        /// <inheritdoc />
        public override UnratedItem GetBlockUnratedType() => UnratedItem.Game;

        public GameInfo GetLookupInfo()
        {
            var info = GetItemLookupInfo<GameInfo>();

            info.Platform = Platform;
            info.RomChecksum = RomChecksum;

            return info;
        }
    }
}
