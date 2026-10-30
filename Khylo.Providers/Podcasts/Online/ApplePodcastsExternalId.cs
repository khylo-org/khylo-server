using Khylo.Controller.Entities;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Podcasts.Online;

/// <summary>
/// A podcast's id in Apple's podcast directory.
/// </summary>
public class ApplePodcastsExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => PodcastDirectory.ProviderName;

    /// <inheritdoc />
    public string Key => PodcastDirectory.IdKey;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => null;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is Podcast;
}
