#nullable disable

namespace Khylo.Controller.Providers
{
    /// <summary>
    /// The lookup info for comics.
    /// </summary>
    public class ComicInfo : ItemLookupInfo
    {
        /// <summary>
        /// Gets or sets the name of the series the comic belongs to.
        /// </summary>
        public string SeriesName { get; set; }

        /// <summary>
        /// Gets or sets the issue number.
        /// </summary>
        public string IssueNumber { get; set; }

        /// <summary>
        /// Gets or sets the volume number.
        /// </summary>
        public int? Volume { get; set; }
    }
}
