#nullable disable

namespace Khylo.Controller.Providers
{
    /// <summary>
    /// The lookup info for podcasts.
    /// </summary>
    public class PodcastInfo : ItemLookupInfo
    {
        /// <summary>
        /// Gets or sets the podcast RSS/Atom feed URL.
        /// </summary>
        public string FeedUrl { get; set; }
    }
}
