using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Net;
using Khylo.Controller.Entities;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Podcasts.Online;

/// <summary>
/// Podcast details found online: the show is matched in Apple's podcast directory by its folder's name
/// (or its feed, when one is known), then its own RSS feed gives the description, maker, website and
/// categories. Also powers "Identify" for podcasts.
/// </summary>
public class ApplePodcastsProvider : IRemoteMetadataProvider<Podcast, PodcastInfo>, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplePodcastsProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public ApplePodcastsProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public string Name => PodcastDirectory.ProviderName;

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PodcastInfo searchInfo, CancellationToken cancellationToken)
    {
        var listings = await PodcastDirectory.SearchAsync(_httpClientFactory.CreateClient(NamedClient.Default), searchInfo.Name, cancellationToken).ConfigureAwait(false);
        return listings.Select(l =>
        {
            var result = new RemoteSearchResult { Name = l.Name, ImageUrl = l.ArtworkUrl, SearchProviderName = Name, Overview = l.Author is null ? null : "by " + l.Author };
            result.SetProviderId(PodcastDirectory.IdKey, l.Id);
            return result;
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<MetadataResult<Podcast>> GetMetadata(PodcastInfo info, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient(NamedClient.Default);
        var id = info.GetProviderId(PodcastDirectory.IdKey);
        PodcastListing? listing = null;
        if (!string.IsNullOrEmpty(id))
        {
            listing = await PodcastDirectory.LookupAsync(http, id, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // A show with this name (or this name plus a word or two): a wrong match is worse than none
            var found = await PodcastDirectory.SearchAsync(http, info.Name, cancellationToken).ConfigureAwait(false);
            listing = found.FirstOrDefault(l => PodcastDirectory.SameName(l.Name, info.Name))
                ?? found.FirstOrDefault(l => PodcastDirectory.CloseName(l.Name, info.Name));
        }

        // A feed given in the folder (feed.url) is the show's own; otherwise the directory's
        var feedUrl = string.IsNullOrEmpty(info.FeedUrl) ? listing?.FeedUrl : info.FeedUrl;
        var feed = string.IsNullOrEmpty(feedUrl) ? null : await PodcastDirectory.ReadFeedAsync(http, feedUrl, cancellationToken).ConfigureAwait(false);
        if (listing is null && feed is null)
        {
            return new MetadataResult<Podcast> { HasMetadata = false };
        }

        var podcast = new Podcast
        {
            Name = !string.IsNullOrEmpty(id) ? listing?.Name ?? feed?.Title : null,
            FeedUrl = feedUrl,
            Overview = feed?.Description,
            Author = feed?.Author ?? listing?.Author,
            WebsiteUrl = feed?.Website,
        };
        if (listing is not null)
        {
            podcast.SetProviderId(PodcastDirectory.IdKey, listing.Id);
        }

        foreach (var genre in (feed?.Categories ?? []).Concat(listing?.Genre is null ? [] : [listing.Genre]).Distinct().Take(4))
        {
            podcast.AddGenre(genre);
        }

        return new MetadataResult<Podcast> { Item = podcast, HasMetadata = true };
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
}
