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

namespace Khylo.Providers.Books.OpenLibrary;

/// <summary>
/// Audiobook details from Open Library, found by the book's title (the album tag, which every part of a
/// book in several files carries) and its author.
/// </summary>
public class OpenLibraryAudioBookProvider : IRemoteMetadataProvider<AudioBook, SongInfo>, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenLibraryAudioBookProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public OpenLibraryAudioBookProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public string Name => OpenLibrarySource.ProviderName;

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SongInfo searchInfo, CancellationToken cancellationToken)
        => await OpenLibrarySource.SearchAsync(_httpClientFactory.CreateClient(NamedClient.Default), BookTitle.Clean(Title(searchInfo)), Author(searchInfo), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<MetadataResult<AudioBook>> GetMetadata(SongInfo info, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient(NamedClient.Default);
        var id = info.GetProviderId(OpenLibrarySource.IdKey);
        int? year = null;
        // The search also gives the year the book first came out
        {
            var found = await OpenLibrarySource.FindAsync(http, Title(info), Author(info), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(id) || found?.Id == id)
            {
                if (string.IsNullOrEmpty(id))
                {
                    id = found?.Id;
                }

                year = found?.Year;
            }
        }

        var work = string.IsNullOrEmpty(id) ? null : await OpenLibrarySource.GetWorkAsync(http, id, cancellationToken).ConfigureAwait(false);
        if (work is null)
        {
            return new MetadataResult<AudioBook> { HasMetadata = false };
        }

        // The part keeps its own name (a chapter); the book's details go on every part
        var book = new AudioBook { Overview = work.Description, ProductionYear = year };
        book.SetProviderId(OpenLibrarySource.IdKey, work.Id);
        foreach (var genre in work.Genres)
        {
            book.AddGenre(genre);
        }

        return new MetadataResult<AudioBook> { Item = book, HasMetadata = true };
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);

    private static string Title(SongInfo info) => string.IsNullOrWhiteSpace(info.Album) ? info.Name : info.Album;

    private static string? Author(SongInfo info) => info.AlbumArtists.Count > 0 ? info.AlbumArtists[0] : info.Artists.Count > 0 ? info.Artists[0] : null;
}
