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
    /// Represents a comic book or manga issue (CBZ, CBR, CBT, PDF).
    /// </summary>
    [Common.RequiresSourceSerialisation]
    public class Comic : BaseItem, IHasLookupInfo<ComicInfo>, IHasSeries
    {
        [JsonIgnore]
        public override MediaType MediaType => MediaType.Comic;

        public override bool SupportsPlayedStatus => true;

        public override bool SupportsPositionTicksResume => true;

        [JsonIgnore]
        public override bool SupportsPeople => true;

        /// <summary>
        /// Gets or sets total page count.
        /// </summary>
        public int? PageCount { get; set; }

        /// <summary>
        /// Gets or sets the comic issue number (string to support 0.5, annuals, specials).
        /// </summary>
        public string IssueNumber { get; set; }

        /// <summary>
        /// Gets or sets the comic volume.
        /// </summary>
        public int? Volume { get; set; }

        /// <summary>
        /// Gets or sets the publisher name.
        /// </summary>
        public string Publisher { get; set; }

        [JsonIgnore]
        public string SeriesPresentationUniqueKey { get; set; }

        [JsonIgnore]
        public string SeriesName { get; set; }

        [JsonIgnore]
        public Guid SeriesId { get; set; }

        public string FindSeriesSortName() => SeriesName;

        public string FindSeriesName() => SeriesName;

        public string FindSeriesPresentationUniqueKey() => SeriesPresentationUniqueKey;

        public Guid FindSeriesId() => SeriesId;

        /// <inheritdoc />
        public override bool CanDownload() => IsFileProtocol;

        /// <inheritdoc />
        public override UnratedItem GetBlockUnratedType() => UnratedItem.Book;

        public ComicInfo GetLookupInfo()
        {
            var info = GetItemLookupInfo<ComicInfo>();

            if (string.IsNullOrEmpty(SeriesName))
            {
                info.SeriesName = GetParents().Select(i => i.Name).FirstOrDefault();
            }
            else
            {
                info.SeriesName = SeriesName;
            }

            info.IssueNumber = IssueNumber;
            info.Volume = Volume;

            return info;
        }
    }
}
