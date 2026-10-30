using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Api;
using Khylo.Common.Configuration;
using Khylo.Controller.LiveTv;
using Khylo.Model.LiveTv;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// Picking which channels of an IPTV playlist are brought into Live TV, so a provider's tens of
/// thousands of entries don't all end up in the guide.
/// </summary>
[Route("LiveTv/TunerHosts/{tunerId}")]
[Authorize(Policy = Policies.RequiresElevation)]
public class LiveTvChannelSelectionController : BaseKhyloApiController
{
    private const string LiveTvConfigKey = "livetv";

    // Big playlists take a minute to download; picking channels opens and saves them several times
    private static readonly TimeSpan _playlistCacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, (DateTime LoadedUtc, List<ChannelInfo> Channels)> _playlistCache = new(StringComparer.Ordinal);

    private readonly ITunerHostManager _tunerHostManager;
    private readonly IConfigurationManager _configurationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveTvChannelSelectionController"/> class.
    /// </summary>
    /// <param name="tunerHostManager">The tuner host manager.</param>
    /// <param name="configurationManager">The configuration manager.</param>
    public LiveTvChannelSelectionController(ITunerHostManager tunerHostManager, IConfigurationManager configurationManager)
    {
        _tunerHostManager = tunerHostManager;
        _configurationManager = configurationManager;
    }

    /// <summary>
    /// Gets a tuner's playlist: every group with its size, and the channels of the live groups,
    /// each with whether it's currently imported. On-demand groups (movies and series some
    /// providers list as channels, often hundreds of thousands) come as counts only.
    /// </summary>
    /// <param name="tunerId">The tuner id.</param>
    /// <param name="refresh">Download the playlist again instead of using the copy from the last few minutes.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The playlist.</response>
    /// <response code="404">No playlist tuner with that id.</response>
    /// <response code="502">The playlist couldn't be downloaded.</response>
    /// <returns>The playlist's groups and live channels.</returns>
    [HttpGet("Playlist")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PlaylistInfo>> GetPlaylist([FromRoute, Required] string tunerId, [FromQuery] bool refresh, CancellationToken cancellationToken)
    {
        var (tuner, host) = FindPlaylistTuner(tunerId);
        if (tuner is null || host is null)
        {
            return NotFound();
        }

        try
        {
            var channels = await LoadPlaylist(tuner, host, refresh, cancellationToken).ConfigureAwait(false);
            return Ok(BuildPlaylistInfo(tuner, channels));
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, "The provider's playlist couldn't be downloaded.");
        }
    }

    /// <summary>
    /// Downloads the playlist of a tuner that hasn't been added yet, to check it works and pick its
    /// channels before anything is imported.
    /// </summary>
    /// <param name="tuner">The tuner as it would be saved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The playlist.</response>
    /// <response code="400">The tuner type doesn't use a playlist, or no address was given.</response>
    /// <response code="502">The playlist couldn't be downloaded or read.</response>
    /// <returns>The playlist's groups and live channels.</returns>
    [HttpPost("/LiveTv/TunerHosts/TestPlaylist")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PlaylistInfo>> TestPlaylist([FromBody, Required] TunerHostInfo tuner, CancellationToken cancellationToken)
    {
        var host = FindPlaylistHost(tuner.Type);
        if (host is null || string.IsNullOrWhiteSpace(tuner.Url))
        {
            return BadRequest("Enter a playlist address.");
        }

        // Nothing is picked yet
        tuner.EnableChannelSelection = true;
        tuner.IncludedGroups = [];
        tuner.IncludedChannels = [];
        tuner.ExcludedChannels = [];

        List<ChannelInfo> channels;
        try
        {
            channels = await LoadPlaylist(tuner, host, true, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // The message never includes the address, which often holds the provider login
            var reason = ex.StatusCode is null
                ? "Couldn't reach the provider. Check the address."
                : $"The provider answered {(int)ex.StatusCode}. Check the address, username and password.";
            return StatusCode(StatusCodes.Status502BadGateway, reason);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status502BadGateway, "The provider took too long to answer. Try again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or FormatException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, "Couldn't read that playlist. Check the address or file.");
        }

        if (channels.Count == 0)
        {
            return StatusCode(StatusCodes.Status502BadGateway, "The playlist has no channels Khylo can play.");
        }

        return Ok(BuildPlaylistInfo(tuner, channels));
    }

    /// <summary>
    /// Saves which of a tuner's playlist channels are brought into Live TV, then refreshes the guide.
    /// </summary>
    /// <param name="tunerId">The tuner id.</param>
    /// <param name="selection">The groups and channels to import.</param>
    /// <response code="204">Selection saved.</response>
    /// <response code="404">No playlist tuner with that id.</response>
    /// <returns>No content.</returns>
    [HttpPost("ChannelSelection")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetChannelSelection([FromRoute, Required] string tunerId, [FromBody, Required] TunerChannelSelection selection)
    {
        var (tuner, _) = FindPlaylistTuner(tunerId);
        if (tuner is null)
        {
            return NotFound();
        }

        tuner.EnableChannelSelection = selection.EnableChannelSelection;
        tuner.IncludedGroups = selection.IncludedGroups.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        tuner.IncludedChannels = selection.IncludedChannels.Distinct(StringComparer.Ordinal).ToArray();
        tuner.ExcludedChannels = selection.ExcludedChannels.Distinct(StringComparer.Ordinal).ToArray();

        // Saving with a changed data source re-imports the channels and refreshes the guide
        await _tunerHostManager.SaveTunerHost(tuner).ConfigureAwait(false);
        return NoContent();
    }

    private static PlaylistInfo BuildPlaylistInfo(TunerHostInfo tuner, List<ChannelInfo> channels)
    {
        var kinds = channels.ToDictionary(c => c, PlaylistChannelSelector.GetKind);
        var groups = channels
            .GroupBy(c => c.ChannelGroup ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PlaylistGroupInfo
            {
                Name = g.First().ChannelGroup ?? string.Empty,
                ChannelCount = g.Count(),
                Kind = g.GroupBy(c => kinds[c]).OrderByDescending(k => k.Count()).First().Key
            })
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var onDemandGroups = groups
            .Where(g => g.Kind == PlaylistChannelKind.OnDemand)
            .Select(g => g.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new PlaylistInfo
        {
            Groups = groups,
            TotalCount = channels.Count,
            Channels = channels
                .Where(c => !onDemandGroups.Contains(c.ChannelGroup ?? string.Empty))
                .Select(c => new PlaylistChannelInfo
                {
                    Id = c.Id,
                    Name = c.Name ?? string.Empty,
                    Number = c.Number,
                    Group = c.ChannelGroup ?? string.Empty,
                    LogoUrl = c.ImageUrl,
                    Kind = kinds[c],
                    IsIncluded = PlaylistChannelSelector.IsIncluded(tuner, c)
                })
                .ToList()
        };
    }

    // Keyed by address rather than tuner id, so a playlist tested while adding a tuner is reused
    // when its channels are picked again later
    private static async Task<List<ChannelInfo>> LoadPlaylist(TunerHostInfo tuner, IPlaylistTunerHost host, bool refresh, CancellationToken cancellationToken)
    {
        var cacheKey = tuner.Url + "|" + tuner.UserAgent;
        if (!refresh && _playlistCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.LoadedUtc < _playlistCacheLifetime)
        {
            return cached.Channels;
        }

        var channels = await host.GetAllPlaylistChannels(tuner, cancellationToken).ConfigureAwait(false);
        _playlistCache[cacheKey] = (DateTime.UtcNow, channels);
        return channels;
    }

    private IPlaylistTunerHost? FindPlaylistHost(string? type)
        => _tunerHostManager.TunerHosts
            .FirstOrDefault(h => string.Equals(h.Type, type, StringComparison.OrdinalIgnoreCase)) as IPlaylistTunerHost;

    private (TunerHostInfo? Tuner, IPlaylistTunerHost? Host) FindPlaylistTuner(string tunerId)
    {
        var config = (LiveTvOptions)_configurationManager.GetConfiguration(LiveTvConfigKey);
        var tuner = config.TunerHosts.FirstOrDefault(t => string.Equals(t.Id, tunerId, StringComparison.OrdinalIgnoreCase));
        if (tuner is null)
        {
            return (null, null);
        }

        return (tuner, FindPlaylistHost(tuner.Type));
    }
}
