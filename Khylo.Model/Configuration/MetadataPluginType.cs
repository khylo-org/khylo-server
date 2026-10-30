#pragma warning disable CS1591

namespace Khylo.Model.Configuration
{
    /// <summary>
    /// Enum MetadataPluginType.
    /// </summary>
    public enum MetadataPluginType
    {
        LocalImageProvider,
        ImageFetcher,
        ImageSaver,
        LocalMetadataProvider,
        MetadataFetcher,
        MetadataSaver,
        SubtitleFetcher,
        LyricFetcher,
        MediaSegmentProvider,
        LocalSimilarityProvider,
        SimilarityProvider,
        SearchProvider
    }
}
