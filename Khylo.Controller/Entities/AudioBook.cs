#nullable disable

#pragma warning disable CA1724, CS1591

using System;
using System.Text.Json.Serialization;
using Khylo.Controller.Library;
using Khylo.Controller.Providers;
using Khylo.Data.Enums;

namespace Khylo.Controller.Entities
{
    [Common.RequiresSourceSerialisation]
    public class AudioBook : Audio.Audio, IHasSeries, IHasLookupInfo<SongInfo>
    {
        // MediaType is intentionally inherited from Audio (MediaType.Audio).
        // Playback (StreamBuilder, MediaSourceManager, clients) only treats MediaType.Audio as audio;
        // reporting MediaType.AudioBook routes audiobooks through the video pipeline and breaks playback.
        // Use the item Type (BaseItemKind.AudioBook) to distinguish audiobooks instead.

        /// <summary>
        /// Gets or sets the narrator of the audiobook.
        /// </summary>
        public string Narrator { get; set; }

        [JsonIgnore]
        public override bool SupportsPositionTicksResume => true;

        [JsonIgnore]
        public override bool SupportsPlayedStatus => true;

        [JsonIgnore]
        public string SeriesPresentationUniqueKey { get; set; }

        [JsonIgnore]
        public string SeriesName { get; set; }

        [JsonIgnore]
        public Guid SeriesId { get; set; }

        public string FindSeriesSortName()
        {
            return SeriesName;
        }

        public string FindSeriesName()
        {
            return SeriesName;
        }

        public string FindSeriesPresentationUniqueKey()
        {
            return SeriesPresentationUniqueKey;
        }

        public override double GetDefaultPrimaryImageAspectRatio()
        {
            return 0;
        }

        public Guid FindSeriesId()
        {
            return SeriesId;
        }

        public override bool CanDownload()
        {
            return IsFileProtocol;
        }

        public override UnratedItem GetBlockUnratedType()
        {
            return UnratedItem.Book;
        }

        // A part of a book in several files, found again by a scan: it learns its part number and
        // its book (books scanned before parts were grouped get them this way)
        internal override ItemUpdateType UpdateFromResolvedItem(BaseItem newItem)
        {
            var updateType = base.UpdateFromResolvedItem(newItem);

            if (newItem is AudioBook part && !part.IsInMixedFolder)
            {
                if (!IndexNumber.HasValue && part.IndexNumber.HasValue)
                {
                    IndexNumber = part.IndexNumber;
                    updateType |= ItemUpdateType.MetadataImport;
                }

                if (string.IsNullOrEmpty(Album) && !string.IsNullOrEmpty(part.Album))
                {
                    Album = part.Album;
                    updateType |= ItemUpdateType.MetadataImport;
                }
            }

            return updateType;
        }
    }
}
