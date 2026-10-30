using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Common.Net;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Movies;
using Khylo.Controller.Entities.TV;
using Khylo.Controller.Library;
using Khylo.Controller.LiveChannels;
using Khylo.Data.Enums;
using Khylo.Database.Implementations.Entities;
using Khylo.Model.Entities;
using Khylo.Model.Globalization;
using Khylo.Model.LiveChannels;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Implementations.LiveChannels;

/// <summary>
/// Builds and serves Khylo live channels. Each channel is a persisted, deterministic schedule laid
/// out ahead of time from an in-memory snapshot of the movie and TV libraries; no media is played,
/// generated or transcoded until a viewer tunes in.
/// </summary>
public sealed class LiveChannelManager : ILiveChannelManager, IDisposable
{
    private const int UpNextCount = 12;
    private const int MaxDetailTitles = 120;
    private const int MinimumSeries = 4;
    private const int MinimumMovies = 10;
    private const int MinimumCollectionMovies = 4;

    /// <summary>
    /// How far ahead schedules are laid out.
    /// </summary>
    private static readonly TimeSpan _horizon = TimeSpan.FromHours(48);

    /// <summary>
    /// Schedules are extended again once less than this is left.
    /// </summary>
    private static readonly TimeSpan _minimumAhead = TimeSpan.FromHours(24);

    private static readonly TimeSpan _snapshotLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan _snapshotMinimumAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _minimumMovieRuntime = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan _minimumEpisodeRuntime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _viewingGrace = TimeSpan.FromHours(1);
    private static readonly TimeSpan _logoRetryAfter = TimeSpan.FromDays(7);
    private static readonly Dictionary<string, string> _logoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/svg+xml"] = ".svg",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp"
    };

    private static readonly TimeSpan _minimumChannelRuntime = TimeSpan.FromHours(12);
    private static readonly TimeSpan _minimumCollectionRuntime = TimeSpan.FromHours(6);

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly ILogger<LiveChannelManager> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly ILocalizationManager _localizationManager;
    private readonly ILiveChannelViewingRegistry _viewingRegistry;
    private readonly ILiveChannelLogoProvider _logoProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _logosPath;
    private readonly ConcurrentDictionary<Guid, Lazy<Task<string?>>> _logoTasks = new();
    private readonly string _definitionsPath;
    private readonly string _settingsPath;
    private readonly string _schedulesPath;

    private readonly object _definitionsLock = new();
    private readonly object _snapshotLock = new();
    private readonly ConcurrentDictionary<Guid, object> _channelLocks = new();
    private readonly ConcurrentDictionary<Guid, LiveChannelScheduleState> _states = new();

    private List<LiveChannelDefinition> _definitions;
    private LiveChannelSettings _settings;
    private HashSet<string> _disabledKeys;
    private IReadOnlyList<LiveChannelDefinition> _dynamicDefinitions = Array.Empty<LiveChannelDefinition>();
    private IReadOnlyList<LiveChannelTitle>? _snapshot;
    private Dictionary<Guid, LiveChannelContent> _contentByChannel = new();
    private DateTime _snapshotBuiltUtc = DateTime.MinValue;
    private volatile bool _libraryChanged;
    private volatile bool _contentDirty;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveChannelManager"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="localizationManager">The localization manager.</param>
    /// <param name="viewingRegistry">The channel viewing registry.</param>
    /// <param name="logoProvider">Finds network logos.</param>
    /// <param name="httpClientFactory">The HTTP client factory, for downloading logos.</param>
    /// <param name="appPaths">The application paths.</param>
    public LiveChannelManager(
        ILogger<LiveChannelManager> logger,
        ILibraryManager libraryManager,
        ILocalizationManager localizationManager,
        ILiveChannelViewingRegistry viewingRegistry,
        ILiveChannelLogoProvider logoProvider,
        IHttpClientFactory httpClientFactory,
        IApplicationPaths appPaths)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _localizationManager = localizationManager;
        _viewingRegistry = viewingRegistry;
        _logoProvider = logoProvider;
        _httpClientFactory = httpClientFactory;
        _logosPath = Path.Combine(appPaths.CachePath, "khylo", "live-channel-logos");
        _definitionsPath = Path.Combine(appPaths.ConfigurationDirectoryPath, "khylo_live_channels.json");
        _settingsPath = Path.Combine(appPaths.ConfigurationDirectoryPath, "khylo_live_channels_settings.json");
        _schedulesPath = Path.Combine(appPaths.DataPath, "khylo", "live-channels");
        _definitions = LoadDefinitions(out var legacyDisabled);
        _settings = LoadSettings(legacyDisabled);
        _disabledKeys = new HashSet<string>(_settings.DisabledChannels, StringComparer.Ordinal);

        _libraryManager.ItemAdded += OnLibraryChanged;
        _libraryManager.ItemRemoved += OnLibraryChanged;
        _libraryManager.ItemUpdated += OnCollectionUpdated;
    }

    /// <inheritdoc/>
    public bool IsEnabled => _settings.Enabled;

    /// <inheritdoc/>
    public void SetEnabled(bool enabled)
    {
        lock (_definitionsLock)
        {
            if (_settings.Enabled == enabled)
            {
                return;
            }

            _settings.Enabled = enabled;
            SaveSettings();
        }

        _logger.LogInformation("Khylo live channels turned {State}", enabled ? "on" : "off");
    }

    /// <inheritdoc/>
    public void SetChannelsEnabled(IReadOnlyCollection<Guid> channelIds, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(channelIds);
        var changed = false;
        lock (_definitionsLock)
        {
            var ids = channelIds.ToHashSet();
            foreach (var definition in _definitions.Concat(_dynamicDefinitions).Where(d => ids.Contains(d.Id)))
            {
                var key = GetKey(definition);
                changed |= enabled ? _disabledKeys.Remove(key) : _disabledKeys.Add(key);
            }

            if (changed)
            {
                _settings.DisabledChannels = _disabledKeys.Order(StringComparer.Ordinal).ToArray();
                SaveSettings();
            }
        }

        // Category channels share the library, so turning one on or off moves titles between them
        if (changed)
        {
            _contentDirty = true;
        }
    }

    /// <inheritdoc/>
    public LiveChannelAdminDto GetAdminStatus(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var contentByChannel = GetContentByChannel();
        var month = DateTime.Now.Month;
        var channels = new List<LiveChannelAdminChannelDto>();

        // Other people's personal collection channels stay private, even from administrators
        foreach (var definition in GetDefinitions().Where(d => IsVisibleTo(d, user)))
        {
            contentByChannel.TryGetValue(definition.Id, out var content);
            var enabled = IsChannelEnabled(definition);
            var isSeasonal = definition.Criteria.ActiveMonths.Length > 0;
            LiveChannelAdminState state;
            if (!enabled)
            {
                state = LiveChannelAdminState.Off;
            }
            else if (content is null || !content.IsAvailable)
            {
                state = LiveChannelAdminState.NotEnoughContent;
            }
            else if (isSeasonal && !definition.Criteria.ActiveMonths.Contains(month))
            {
                state = LiveChannelAdminState.OutOfSeason;
            }
            else
            {
                state = LiveChannelAdminState.OnAir;
            }

            channels.Add(new LiveChannelAdminChannelDto
            {
                Id = definition.Id,
                Key = definition.AutoKey,
                Number = definition.Number,
                Name = definition.Name,
                Group = isSeasonal ? LiveChannelCatalog.SeasonalCategory : definition.Category,
                Description = definition.Description,
                Enabled = enabled,
                State = state,
                TitleCount = content?.Units.Count ?? 0,
                MinimumTitles = content?.MinimumTitles ?? 0,
                Hours = Math.Round(TimeSpan.FromTicks(content?.TotalTicks ?? 0).TotalHours, 1),
                MinimumHours = content?.MinimumRuntime.TotalHours ?? 0,
                ActiveMonths = definition.Criteria.ActiveMonths
            });
        }

        return new LiveChannelAdminDto { Enabled = IsEnabled, Channels = channels };
    }

    /// <inheritdoc/>
    public IReadOnlyList<LiveChannelDefinition> GetDefinitions()
    {
        lock (_definitionsLock)
        {
            return _definitions.Concat(_dynamicDefinitions).OrderBy(d => d.Number).ToList();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<LiveChannelDto> GetGuide(User user, DateTime startUtc, DateTime endUtc)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = DateTime.UtcNow;
        var result = new List<LiveChannelDto>();

        foreach (var (definition, content) in GetWatchableChannels(user))
        {
            var state = EnsureSchedule(definition, content, now, endUtc);
            List<LiveChannelScheduledProgram> programs;
            lock (GetChannelLock(definition.Id))
            {
                programs = state.Programs.Where(p => p.EndUtc > startUtc && p.StartUtc < endUtc).ToList();
            }

            result.Add(ToChannelDto(definition, programs.Select(ToProgramDto).OfType<LiveChannelProgramDto>().ToList()));
        }

        return result;
    }

    /// <inheritdoc/>
    public LiveChannelTuneResult? Tune(User user, string? deviceId, Guid channelId)
    {
        ArgumentNullException.ThrowIfNull(user);
        var channel = GetWatchableChannels(user).FirstOrDefault(c => c.Definition.Id.Equals(channelId));
        if (channel.Definition is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var state = EnsureSchedule(channel.Definition, channel.Content, now, now + _minimumAhead);

        List<LiveChannelScheduledProgram> upcoming;
        lock (GetChannelLock(channelId))
        {
            var index = LiveChannelScheduler.FindProgramIndex(state, now);
            upcoming = index < 0 ? new List<LiveChannelScheduledProgram>() : state.Programs.Skip(index).Take(UpNextCount + 10).ToList();
        }

        // Skip anything removed from the library since it was scheduled
        var programs = new List<(LiveChannelScheduledProgram Program, LiveChannelProgramDto Dto)>();
        foreach (var program in upcoming)
        {
            var dto = ToProgramDto(program);
            if (dto is not null)
            {
                programs.Add((program, dto));
            }

            if (programs.Count > UpNextCount)
            {
                break;
            }
        }

        if (programs.Count == 0)
        {
            return null;
        }

        var current = programs[0];
        var positionTicks = current.Program.StartUtc <= now ? (now - current.Program.StartUtc).Ticks : 0;

        if (!string.IsNullOrEmpty(deviceId))
        {
            foreach (var (program, _) in programs)
            {
                _viewingRegistry.Register(user.Id, deviceId, program.ItemId, program.EndUtc + _viewingGrace);
            }
        }

        var dtos = programs.Select(p => p.Dto).ToList();
        return new LiveChannelTuneResult
        {
            Channel = ToChannelDto(channel.Definition, dtos),
            Current = current.Dto,
            StartPositionTicks = positionTicks,
            UpNext = dtos.Skip(1).ToList(),
            ServerTimeUtc = now
        };
    }

    /// <inheritdoc/>
    public LiveChannelDetailsDto? GetChannelDetails(User user, Guid channelId, DateTime endUtc)
    {
        ArgumentNullException.ThrowIfNull(user);
        var channel = GetWatchableChannels(user).FirstOrDefault(c => c.Definition.Id.Equals(channelId));
        if (channel.Definition is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var state = EnsureSchedule(channel.Definition, channel.Content, now, endUtc);
        List<LiveChannelScheduledProgram> programs;
        lock (GetChannelLock(channelId))
        {
            programs = state.Programs.Where(p => p.EndUtc > now && p.StartUtc < endUtc).ToList();
        }

        var titles = channel.Content.Units.Values
            .Select(u => _libraryManager.GetItemById(u.TitleId))
            .OfType<BaseItem>()
            .OrderBy(i => i.SortName ?? i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new LiveChannelDetailsDto
        {
            Channel = ToChannelDto(channel.Definition, programs.Select(ToProgramDto).OfType<LiveChannelProgramDto>().ToList()),
            TitleCount = titles.Count,
            Titles = titles.Take(MaxDetailTitles).Select(i => new LiveChannelTitleDto
            {
                ItemId = i.Id,
                Type = i.GetBaseItemKind(),
                Name = i.Name,
                ProductionYear = i.ProductionYear
            }).ToList()
        };
    }

    /// <inheritdoc/>
    public Task<string?> GetChannelLogoPathAsync(Guid channelId, CancellationToken cancellationToken)
    {
        // One lookup per channel at a time; results (or "no logo") are cached on disk
        var task = _logoTasks.GetOrAdd(channelId, id => new Lazy<Task<string?>>(() => FindChannelLogoAsync(id, CancellationToken.None))).Value;
        if (task.IsCompleted)
        {
            _logoTasks.TryRemove(channelId, out _);
        }

        return task.WaitAsync(cancellationToken);
    }

    private async Task<string?> FindChannelLogoAsync(Guid channelId, CancellationToken cancellationToken)
    {
        var definition = GetDefinitions().FirstOrDefault(d => d.Id.Equals(channelId));
        if (definition?.AutoKey is null || !definition.AutoKey.StartsWith(LiveChannelCatalog.NetworkKeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var baseName = Path.Combine(_logosPath, channelId.ToString("N"));
        foreach (var extension in _logoExtensions.Values)
        {
            if (File.Exists(baseName + extension))
            {
                return baseName + extension;
            }
        }

        // Remember misses for a while instead of asking TMDB on every guide load
        var missMarker = baseName + ".none";
        if (File.Exists(missMarker) && DateTime.UtcNow - File.GetLastWriteTimeUtc(missMarker) < _logoRetryAfter)
        {
            return null;
        }

        var networkNames = definition.Criteria.Studios;
        var seriesIds = (_snapshot ?? Array.Empty<LiveChannelTitle>())
            .Where(t => t.IsSeries && t.TmdbId is not null && t.Studios.Any(s => networkNames.Contains(s, StringComparer.OrdinalIgnoreCase)))
            .Select(t => t.TmdbId!)
            .ToList();

        try
        {
            Directory.CreateDirectory(_logosPath);
            var url = await _logoProvider.GetNetworkLogoUrlAsync(networkNames, seriesIds, cancellationToken).ConfigureAwait(false);
            if (url is not null)
            {
                using var response = await _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (response.IsSuccessStatusCode && _logoExtensions.TryGetValue(contentType, out var extension))
                {
                    var path = baseName + extension;
                    var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
                    return path;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Could not fetch the logo for live channel {Channel}", definition.Name);
        }

        try
        {
            await File.WriteAllTextAsync(missMarker, string.Empty, CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Could not write {Path}", missMarker);
        }

        return null;
    }

    /// <inheritdoc/>
    public void Leave(User user, string? deviceId)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!string.IsNullOrEmpty(deviceId))
        {
            _viewingRegistry.Release(user.Id, deviceId);
        }
    }

    /// <inheritdoc/>
    public void ResetSchedules()
    {
        foreach (var id in _states.Keys)
        {
            lock (GetChannelLock(id))
            {
                _states.TryRemove(id, out _);
                TryDelete(GetStatePath(id));
            }
        }

        if (Directory.Exists(_schedulesPath))
        {
            foreach (var file in Directory.EnumerateFiles(_schedulesPath, "*.json"))
            {
                TryDelete(file);
            }
        }

        lock (_snapshotLock)
        {
            _snapshot = null;
        }

        _logger.LogInformation("Khylo live channel schedules reset");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _libraryManager.ItemAdded -= OnLibraryChanged;
        _libraryManager.ItemRemoved -= OnLibraryChanged;
        _libraryManager.ItemUpdated -= OnCollectionUpdated;
    }

    private void OnLibraryChanged(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is Movie or Episode or Series or BoxSet)
        {
            _libraryChanged = true;
        }
    }

    // Movies added to or removed from a collection change its channel
    private void OnCollectionUpdated(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is BoxSet)
        {
            _libraryChanged = true;
        }
    }

    private IEnumerable<(LiveChannelDefinition Definition, LiveChannelContent Content)> GetWatchableChannels(User user)
    {
        // Turned off: nothing is shown, tuned or scheduled
        if (!IsEnabled)
        {
            yield break;
        }

        var contentByChannel = GetContentByChannel();
        var userLibraries = _libraryManager.GetUserRootFolder()
            .GetChildren(user, true)
            .Select(f => f.Id)
            .ToHashSet();
        var maxScore = user.MaxParentalRatingScore;

        var month = DateTime.Now.Month;
        foreach (var definition in GetDefinitions().Where(IsChannelEnabled))
        {
            // Channels from someone's personal collection are theirs alone
            if (!IsVisibleTo(definition, user))
            {
                continue;
            }

            // Seasonal channels are only on the air in their months
            if (definition.Criteria.ActiveMonths.Length > 0 && !definition.Criteria.ActiveMonths.Contains(month))
            {
                continue;
            }

            if (!contentByChannel.TryGetValue(definition.Id, out var content) || !content.IsAvailable)
            {
                continue;
            }

            if (!content.LibraryIds.All(userLibraries.Contains))
            {
                continue;
            }

            if (maxScore.HasValue && content.MaxRatingScore > maxScore.Value)
            {
                continue;
            }

            yield return (definition, content);
        }
    }

    private LiveChannelScheduleState EnsureSchedule(LiveChannelDefinition definition, LiveChannelContent content, DateTime nowUtc, DateTime neededUntilUtc)
    {
        lock (GetChannelLock(definition.Id))
        {
            var state = _states.GetOrAdd(definition.Id, LoadState);
            var fingerprint = GetFingerprint(definition);
            var changed = false;

            // The channel's rules changed: start a new schedule
            if (!string.Equals(state.DefinitionFingerprint, fingerprint, StringComparison.Ordinal))
            {
                state = new LiveChannelScheduleState { DefinitionFingerprint = fingerprint };
                _states[definition.Id] = state;
                changed = true;
            }

            var scheduledUntil = state.Programs.Count > 0 ? state.Programs[^1].EndUtc : DateTime.MinValue;
            var target = neededUntilUtc > nowUtc + _minimumAhead ? neededUntilUtc : nowUtc + _minimumAhead;
            if (scheduledUntil < target)
            {
                var extendTo = target > nowUtc + _horizon ? target : nowUtc + _horizon;
                changed |= LiveChannelScheduler.Extend(state, definition.Id, content.Units, nowUtc, extendTo);
            }

            if (changed)
            {
                SaveState(definition.Id, state);
            }

            return state;
        }
    }

    private Dictionary<Guid, LiveChannelContent> GetContentByChannel()
    {
        lock (_snapshotLock)
        {
            var age = DateTime.UtcNow - _snapshotBuiltUtc;
            var isStale = _snapshot is null || age > _snapshotLifetime || (_libraryChanged && age > _snapshotMinimumAge);
            if (isStale)
            {
                _libraryChanged = false;
                _snapshot = BuildSnapshot(out var dynamicDefinitions);
                _snapshotBuiltUtc = DateTime.UtcNow;
                lock (_definitionsLock)
                {
                    _dynamicDefinitions = dynamicDefinitions;
                }

                _contentDirty = true;
            }

            if (_contentDirty)
            {
                _contentDirty = false;
                var all = GetDefinitions();
                _contentByChannel = BuildAllContent(all, _snapshot!);
                RemoveOrphanSchedules(all);
            }

            return _contentByChannel;
        }
    }

    private List<LiveChannelTitle> BuildSnapshot(out IReadOnlyList<LiveChannelDefinition> dynamicDefinitions)
    {
        var started = DateTime.UtcNow;
        var titles = new List<LiveChannelTitle>();
        var seenMovies = new HashSet<(string? Name, int? Year)>();
        var libraries = _libraryManager.GetUserRootFolder().Children
            .OfType<CollectionFolder>()
            .Where(f => f.CollectionType is null or CollectionType.movies or CollectionType.tvshows);

        foreach (var library in libraries)
        {
            var movies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie],
                AncestorIds = [library.Id],
                IsVirtualItem = false,
                Recursive = true
            });
            foreach (var movie in movies)
            {
                var runTime = movie.RunTimeTicks ?? 0;
                if (movie.ExtraType is not null || runTime < _minimumMovieRuntime.Ticks)
                {
                    continue;
                }

                // The same movie can exist twice (e.g. a 4K and a 1080p copy); air it once
                if (!seenMovies.Add((movie.Name?.ToUpperInvariant(), movie.ProductionYear)))
                {
                    continue;
                }

                titles.Add(CreateTitle(movie, library.Id, false, [new LiveChannelUnitItem(movie.Id, runTime)]));
            }

            var episodesBySeries = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.Episode],
                    AncestorIds = [library.Id],
                    IsVirtualItem = false,
                    Recursive = true
                })
                .OfType<Episode>()
                .Where(e => e.ParentIndexNumber is not 0 && e.ExtraType is null && (e.RunTimeTicks ?? 0) >= _minimumEpisodeRuntime.Ticks)
                .GroupBy(e => e.SeriesId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var seriesList = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Series],
                AncestorIds = [library.Id],
                Recursive = true
            });
            foreach (var series in seriesList)
            {
                if (!episodesBySeries.TryGetValue(series.Id, out var episodes) || episodes.Count == 0)
                {
                    continue;
                }

                var ordered = episodes
                    .OrderBy(e => e.ParentIndexNumber ?? int.MaxValue)
                    .ThenBy(e => e.IndexNumber ?? int.MaxValue)
                    .ThenBy(e => e.SortName, StringComparer.Ordinal)
                    .Select(e => new LiveChannelUnitItem(e.Id, e.RunTimeTicks!.Value, e.ParentIndexNumber ?? 1))
                    .ToList();
                titles.Add(CreateTitle(series, library.Id, true, ordered));
            }
        }

        dynamicDefinitions = BuildDynamicDefinitions(titles);

        _logger.LogInformation(
            "Khylo live channels: indexed {Count} titles in {Seconds:F1}s",
            titles.Count,
            (DateTime.UtcNow - started).TotalSeconds);
        return titles;
    }

    /// <summary>
    /// Network channels (from the networks series list) and collection channels (from box sets).
    /// </summary>
    private List<LiveChannelDefinition> BuildDynamicDefinitions(List<LiveChannelTitle> titles)
    {
        var seriesStudios = titles
            .Where(t => t.IsSeries)
            .SelectMany(t => t.Studios)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var movieIds = titles.Where(t => !t.IsSeries).Select(t => t.Id).ToHashSet();
        var collections = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.BoxSet],
                Recursive = true
            })
            .OfType<BoxSet>()

            // A personal collection's channel belongs to the person who made the collection
            .Select(b => (b.Id, b.Name, OwnerUserId: b.IsPersonal ? b.OwnerUserId : (Guid?)null, MovieIds: b.GetLinkedChildren().Select(c => c.Id).Where(movieIds.Contains).ToArray()))
            .Where(c => c.MovieIds.Length > 0);

        return [.. LiveChannelCatalog.CreateNetworkChannels(seriesStudios.Overlaps), .. LiveChannelCatalog.CreateCollectionChannels(collections)];
    }

    private static LiveChannelTitle CreateTitle(BaseItem item, Guid libraryId, bool isSeries, IReadOnlyList<LiveChannelUnitItem> items)
    {
        // Series: the year it first aired, and when it ended (if it has)
        int? endedYear = null;
        if (item is Series series && series.Status == SeriesStatus.Ended)
        {
            endedYear = series.EndDate?.Year ?? series.ProductionYear;
        }

        return new LiveChannelTitle
        {
            Id = item.Id,
            LibraryId = libraryId,
            IsSeries = isSeries,
            Genres = item.Genres ?? Array.Empty<string>(),
            Tags = item.Tags ?? Array.Empty<string>(),
            Studios = item.Studios ?? Array.Empty<string>(),
            TmdbId = item.GetProviderId(MetadataProvider.Tmdb),
            CommunityRating = item.CommunityRating,
            DateCreated = item.DateCreated,
            Year = isSeries ? item.PremiereDate?.Year ?? item.ProductionYear : item.ProductionYear ?? item.PremiereDate?.Year,
            EndedYear = endedYear,
            RatingScore = item.InheritedParentalRatingValue,
            Items = items
        };
    }

    /// <summary>
    /// Works out what every channel airs. Category channels (<see cref="LiveChannelCriteria.Exclusive"/>)
    /// share the library: each title goes to the first one, by channel number, that it matches. A
    /// category channel without enough content stays off the air and its titles move on to their next
    /// match, so nothing is lost. Network, collection and seasonal channels draw from the whole library.
    /// </summary>
    private Dictionary<Guid, LiveChannelContent> BuildAllContent(IReadOnlyList<LiveChannelDefinition> definitions, IReadOnlyList<LiveChannelTitle> snapshot)
    {
        var result = new Dictionary<Guid, LiveChannelContent>();

        // Turned-off category channels don't claim titles; what they would air is still worked
        // out (from the whole library) so administrators can see it
        foreach (var definition in definitions.Where(d => !d.Criteria.Exclusive || !IsChannelEnabled(d)))
        {
            var matches = CreateMatcher(definition);
            result[definition.Id] = BuildContent(definition, snapshot.Where(matches).ToList());
        }

        var active = definitions
            .Where(d => d.Criteria.Exclusive && IsChannelEnabled(d))
            .OrderBy(d => d.Number)
            .Select(d => (Definition: d, Matches: CreateMatcher(d)))
            .ToList();
        while (active.Count > 0)
        {
            var assigned = active.ToDictionary(c => c.Definition.Id, _ => new List<LiveChannelTitle>());
            foreach (var title in snapshot)
            {
                foreach (var (definition, matches) in active)
                {
                    if (matches(title))
                    {
                        assigned[definition.Id].Add(title);
                        break;
                    }
                }
            }

            var contents = active.ToDictionary(c => c.Definition.Id, c => BuildContent(c.Definition, assigned[c.Definition.Id]));
            foreach (var (id, content) in contents)
            {
                result[id] = content;
            }

            var removed = active.RemoveAll(c => !contents[c.Definition.Id].IsAvailable);
            if (removed == 0)
            {
                break;
            }
        }

        return result;
    }

    private Func<LiveChannelTitle, bool> CreateMatcher(LiveChannelDefinition definition)
    {
        var criteria = definition.Criteria;
        var minScore = GetRatingScore(criteria.MinOfficialRating);
        var maxScore = GetRatingScore(criteria.MaxOfficialRating);
        var maxEpisodeTicks = criteria.MaxEpisodeMinutes is > 0 ? TimeSpan.FromMinutes(criteria.MaxEpisodeMinutes.Value).Ticks : long.MaxValue;
        var addedAfter = criteria.AddedWithinDays is > 0 ? DateTime.UtcNow.AddDays(-criteria.AddedWithinDays.Value) : (DateTime?)null;

        // Episode length limits apply to the show as a whole (its typical episode), so a few long
        // episodes don't leave holes in the episode order
        return title => title.Items.Count > 0
            && Matches(criteria, title, minScore, maxScore)
            && !(title.DateCreated < addedAfter)
            && !(title.IsSeries && GetMedianRunTime(title.Items) > maxEpisodeTicks);
    }

    private static LiveChannelContent BuildContent(LiveChannelDefinition definition, IReadOnlyList<LiveChannelTitle> titles)
    {
        var criteria = definition.Criteria;
        var units = new Dictionary<string, LiveChannelUnit>(StringComparer.Ordinal);
        var libraryIds = new HashSet<Guid>();
        int? contentMaxScore = null;
        long totalTicks = 0;

        foreach (var title in titles)
        {
            var key = (title.IsSeries ? "s:" : "m:") + title.Id.ToString("N");
            units[key] = new LiveChannelUnit(key, title.IsSeries, title.Items) { TitleId = title.Id };
            libraryIds.Add(title.LibraryId);
            totalTicks += title.Items.Sum(i => i.RunTimeTicks);
            if (title.RatingScore.HasValue && (!contentMaxScore.HasValue || title.RatingScore > contentMaxScore))
            {
                contentMaxScore = title.RatingScore;
            }
        }

        // Enough variety that the channel doesn't keep looping the same few titles
        var isCollection = criteria.ItemIds.Length > 0;
        var isSeries = criteria.ContentType == LiveChannelContentType.Series;
        return new LiveChannelContent
        {
            MinimumTitles = isCollection ? MinimumCollectionMovies : isSeries ? MinimumSeries : MinimumMovies,
            MinimumRuntime = isCollection ? _minimumCollectionRuntime : _minimumChannelRuntime,
            Units = units,
            LibraryIds = libraryIds,
            MaxRatingScore = contentMaxScore,
            TotalTicks = totalTicks
        };
    }

    private static long GetMedianRunTime(IReadOnlyList<LiveChannelUnitItem> items)
    {
        var sorted = items.Select(i => i.RunTimeTicks).Order().ToList();
        return sorted[sorted.Count / 2];
    }

    private int? GetRatingScore(string? rating)
        => string.IsNullOrWhiteSpace(rating) ? null : _localizationManager.GetRatingScore(rating)?.Score;

    private static bool Matches(LiveChannelCriteria criteria, LiveChannelTitle title, int? minScore, int? maxScore)
    {
        // A channel airs TV or movies, never both
        if (title.IsSeries != (criteria.ContentType == LiveChannelContentType.Series))
        {
            return false;
        }

        // Titles without metadata can't be classified, so they stay off channels
        if (title.Genres.Length == 0)
        {
            return false;
        }

        if (criteria.LibraryIds.Length > 0 && !criteria.LibraryIds.Contains(title.LibraryId))
        {
            return false;
        }

        if (criteria.Genres.Length > 0 && !title.Genres.Any(g => criteria.Genres.Contains(g, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (criteria.RequiredGenres.Length > 0 && !criteria.RequiredGenres.All(r => title.Genres.Contains(r, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (criteria.AnyOf.Length > 0
            && !title.Genres.Any(g => criteria.AnyOf.Contains(g, StringComparer.OrdinalIgnoreCase))
            && !title.Tags.Any(t => criteria.AnyOf.Contains(t, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (criteria.ExcludeTags.Length > 0 && title.Tags.Any(t => criteria.ExcludeTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (criteria.ItemIds.Length > 0 && !criteria.ItemIds.Contains(title.Id))
        {
            return false;
        }

        if (criteria.Studios.Length > 0 && !title.Studios.Any(s => criteria.Studios.Contains(s, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (criteria.MinCommunityRating.HasValue && !(title.CommunityRating >= criteria.MinCommunityRating))
        {
            return false;
        }

        if (criteria.ExcludeGenres.Length > 0 && title.Genres.Any(g => criteria.ExcludeGenres.Contains(g, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (criteria.Tags.Length > 0 && !title.Tags.Any(t => criteria.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if ((criteria.MinYear.HasValue || criteria.MaxYear.HasValue) && !title.Year.HasValue)
        {
            return false;
        }

        if (title.Year < criteria.MinYear || title.Year > criteria.MaxYear)
        {
            return false;
        }

        if (criteria.EndedByYear.HasValue && !(title.EndedYear <= criteria.EndedByYear))
        {
            return false;
        }

        // A maximum rating only admits rated content at or below it; a minimum lets unrated through
        if (maxScore.HasValue && (!title.RatingScore.HasValue || title.RatingScore > maxScore))
        {
            return false;
        }

        if (minScore.HasValue && title.RatingScore < minScore)
        {
            return false;
        }

        return true;
    }

    private LiveChannelProgramDto? ToProgramDto(LiveChannelScheduledProgram program)
    {
        var item = _libraryManager.GetItemById(program.ItemId);
        if (item is null)
        {
            return null;
        }

        var episode = item as Episode;
        return new LiveChannelProgramDto
        {
            ItemId = item.Id,
            Type = item.GetBaseItemKind(),
            Name = item.Name,
            SeriesName = episode?.SeriesName,
            SeriesId = episode?.SeriesId,
            SeasonNumber = episode?.ParentIndexNumber,
            EpisodeNumber = episode?.IndexNumber,
            ProductionYear = item.ProductionYear,
            OfficialRating = item.OfficialRating,
            Overview = item.Overview,
            StartUtc = program.StartUtc,
            EndUtc = program.EndUtc
        };
    }

    private static LiveChannelDto ToChannelDto(LiveChannelDefinition definition, IReadOnlyList<LiveChannelProgramDto> programs)
        => new()
        {
            Id = definition.Id,
            Key = definition.AutoKey,
            Number = definition.Number,
            Name = definition.Name,
            Category = definition.Category,
            Description = definition.Description,
            Programs = programs
        };

    private object GetChannelLock(Guid channelId) => _channelLocks.GetOrAdd(channelId, _ => new object());

    private static string GetFingerprint(LiveChannelDefinition definition)
    {
        var json = JsonSerializer.Serialize(definition.Criteria);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)), 0, 8);
    }

    private List<LiveChannelDefinition> LoadDefinitions(out IReadOnlyList<string> legacyDisabled)
    {
        var definitions = new List<LiveChannelDefinition>();
        try
        {
            if (File.Exists(_definitionsPath))
            {
                definitions = JsonSerializer.Deserialize<List<LiveChannelDefinition>>(File.ReadAllText(_definitionsPath)) ?? definitions;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read Khylo live channel definitions from {Path}", _definitionsPath);
        }

        // Built-in channels always follow the current catalog; whether each is on now lives in the
        // settings file (older files kept it here). Channels created by users are kept as they are.
        legacyDisabled = definitions
            .Where(d => d.AutoKey is not null && !d.Enabled)
            .Select(d => d.AutoKey!)
            .ToList();
        var builtIns = LiveChannelCatalog.Create();

        var merged = builtIns.Concat(definitions.Where(d => d.AutoKey is null)).ToList();
        var serialized = JsonSerializer.Serialize(merged, _jsonOptions);
        var existing = File.Exists(_definitionsPath) ? File.ReadAllText(_definitionsPath) : null;
        if (!string.Equals(serialized, existing, StringComparison.Ordinal))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_definitionsPath)!);
                File.WriteAllText(_definitionsPath, serialized);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save Khylo live channel definitions to {Path}", _definitionsPath);
            }
        }

        definitions = merged;
        return definitions;
    }

    private LiveChannelSettings LoadSettings(IReadOnlyList<string> legacyDisabled)
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                return JsonSerializer.Deserialize<LiveChannelSettings>(File.ReadAllText(_settingsPath)) ?? new LiveChannelSettings();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read Khylo live channel settings from {Path}", _settingsPath);
            return new LiveChannelSettings();
        }

        _settings = new LiveChannelSettings { DisabledChannels = [.. legacyDisabled] };
        SaveSettings();
        return _settings;
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, _jsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Khylo live channel settings to {Path}", _settingsPath);
        }
    }

    private static bool IsVisibleTo(LiveChannelDefinition definition, User user)
        => definition.OwnerUserId is not { } owner || owner.Equals(user.Id);

    private static string GetKey(LiveChannelDefinition definition) => definition.AutoKey ?? definition.Id.ToString("N");

    private bool IsChannelEnabled(LiveChannelDefinition definition)
    {
        lock (_definitionsLock)
        {
            return definition.Enabled && !_disabledKeys.Contains(GetKey(definition));
        }
    }

    private void RemoveOrphanSchedules(IReadOnlyList<LiveChannelDefinition> definitions)
    {
        if (!Directory.Exists(_schedulesPath))
        {
            return;
        }

        var known = definitions.Select(d => d.Id.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(_schedulesPath, "*.json"))
        {
            if (!known.Contains(Path.GetFileNameWithoutExtension(file)))
            {
                TryDelete(file);
            }
        }
    }

    private string GetStatePath(Guid channelId) => Path.Combine(_schedulesPath, channelId.ToString("N") + ".json");

    private LiveChannelScheduleState LoadState(Guid channelId)
    {
        var path = GetStatePath(channelId);
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<LiveChannelScheduleState>(File.ReadAllText(path)) ?? new LiveChannelScheduleState();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discarding unreadable Khylo live channel schedule {Path}", path);
        }

        return new LiveChannelScheduleState();
    }

    private void SaveState(Guid channelId, LiveChannelScheduleState state)
    {
        var path = GetStatePath(channelId);
        try
        {
            Directory.CreateDirectory(_schedulesPath);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state));
            File.Move(temp, path, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Khylo live channel schedule {Path}", path);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to delete {Path}", path);
        }
    }
}
