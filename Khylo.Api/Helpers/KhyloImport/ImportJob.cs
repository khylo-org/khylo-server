using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Khylo.Api.Helpers.KhyloImport;

/// <summary>
/// Runs one import (or preview) at a time in the background and keeps its progress for the dashboard.
/// What's already in Khylo is never made older: when Khylo has newer progress for a title, it's kept,
/// and favorites and play counts only ever go up.
/// </summary>
public static class ImportJob
{
    private const int MaxExamples = 60;
    private const string PlexClientId = "khylo-server-import";

    private static readonly object _lock = new();
    private static ImportStatus _status = new();

    /// <summary>
    /// Gets a copy of where things are.
    /// </summary>
    /// <returns>The status.</returns>
    public static ImportStatus GetStatus()
    {
        lock (_lock)
        {
            return new ImportStatus
            {
                State = _status.State,
                DryRun = _status.DryRun,
                Message = _status.Message,
                Percent = _status.Percent,
                StartedAt = _status.StartedAt,
                FinishedAt = _status.FinishedAt,
                Users = _status.Users.Select(u => new ImportUserResult
                {
                    SourceUserName = u.SourceUserName,
                    KhyloUserName = u.KhyloUserName,
                    Found = u.Found,
                    Matched = u.Matched,
                    Watched = u.Watched,
                    InProgress = u.InProgress,
                    Plays = u.Plays,
                    Favorites = u.Favorites,
                    KeptNewer = u.KeptNewer,
                    Unmatched = u.Unmatched,
                    UnmatchedExamples = u.UnmatchedExamples.ToList(),
                    Error = u.Error
                }).ToList()
            };
        }
    }

    /// <summary>
    /// Lists the people on the other server, checking the address and key on the way.
    /// </summary>
    /// <param name="request">How to reach it.</param>
    /// <param name="http">The HTTP client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The users.</returns>
    public static Task<List<ImportSourceUser>> GetSourceUsersAsync(ImportConnectRequest request, HttpClient http, CancellationToken cancellationToken)
        => request.Source == ImportSourceKind.Plex
            ? new PlexImportSource(http, request.Url, request.Token, PlexClientId).GetUsersAsync(cancellationToken)
            : new KhyloImportSource(http, request.Url, request.Token).GetUsersAsync(cancellationToken);

    /// <summary>
    /// Starts an import or preview in the background.
    /// </summary>
    /// <param name="request">What to import and where.</param>
    /// <param name="http">The HTTP client.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>False when one is already running.</returns>
    public static bool TryStart(ImportRunRequest request, HttpClient http, IUserManager userManager, ILibraryManager libraryManager, IUserDataManager userDataManager, ILogger logger)
    {
        lock (_lock)
        {
            if (_status.State == "Running")
            {
                return false;
            }

            _status = new ImportStatus { State = "Running", DryRun = request.DryRun, Message = "Starting…", StartedAt = DateTime.UtcNow };
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RunAsync(request, http, userManager, libraryManager, userDataManager, logger).ConfigureAwait(false);
                Update(s =>
                {
                    s.State = "Done";
                    s.Percent = 100;
                    s.Message = request.DryRun ? "Preview ready." : "Import finished.";
                    s.FinishedAt = DateTime.UtcNow;
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Watch history import failed");
                Update(s =>
                {
                    s.State = "Failed";
                    s.Message = ex is ImportException ? ex.Message : "Something went wrong: " + ex.Message;
                    s.FinishedAt = DateTime.UtcNow;
                });
            }
        });

        return true;
    }

    private static async Task RunAsync(ImportRunRequest request, HttpClient http, IUserManager userManager, ILibraryManager libraryManager, IUserDataManager userDataManager, ILogger logger)
    {
        var cancellationToken = CancellationToken.None;
        Update(s => s.Message = "Reading your Khylo library…");
        var matcher = new ImportMatcher(libraryManager);

        var mappings = request.Users.Where(m => !m.KhyloUserId.Equals(Guid.Empty)).ToList();
        for (var i = 0; i < mappings.Count; i++)
        {
            var mapping = mappings[i];
            var result = new ImportUserResult { SourceUserName = mapping.SourceUserName };
            Update(s => s.Users.Add(result));

            var user = userManager.GetUserById(mapping.KhyloUserId);
            if (user is null)
            {
                Update(_ => result.Error = "That Khylo user no longer exists.");
                continue;
            }

            Update(_ => result.KhyloUserName = user.Username);
            var share = 100.0 / mappings.Count;
            Update(s =>
            {
                s.Message = $"Reading {mapping.SourceUserName}'s history…";
                s.Percent = (i * share) + (share * 0.05);
            });

            List<ImportEntry> entries;
            try
            {
                entries = request.Source == ImportSourceKind.Plex
                    ? await new PlexImportSource(http, request.Url, request.Token, PlexClientId).GetEntriesAsync(mapping.SourceUserId, mapping.Pin, cancellationToken).ConfigureAwait(false)
                    : await new KhyloImportSource(http, request.Url, request.Token).GetEntriesAsync(mapping.SourceUserId, cancellationToken).ConfigureAwait(false);
            }
            catch (ImportException ex)
            {
                Update(_ => result.Error = ex.Message);
                continue;
            }

            Update(s =>
            {
                result.Found = entries.Count;
                s.Message = request.DryRun ? $"Matching {mapping.SourceUserName}'s history…" : $"Importing {mapping.SourceUserName}'s history…";
            });

            for (var n = 0; n < entries.Count; n++)
            {
                var entry = entries[n];
                var item = matcher.Match(entry);
                if (item is null)
                {
                    Update(_ =>
                    {
                        result.Unmatched++;
                        if (result.UnmatchedExamples.Count < MaxExamples)
                        {
                            result.UnmatchedExamples.Add(entry.Describe());
                        }
                    });
                    continue;
                }

                try
                {
                    Apply(entry, item, user, userDataManager, result, request.DryRun, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Couldn't import history for {Item}", item.Name);
                }

                if (n % 200 == 0)
                {
                    var done = n;
                    Update(s => s.Percent = (i * share) + (share * (0.1 + (0.9 * done / Math.Max(1, entries.Count)))));
                }
            }

            logger.LogInformation(
                "Watch history {Mode} for {SourceUser} -> {User}: {Found} found, {Matched} matched, {Unmatched} not found",
                request.DryRun ? "preview" : "import",
                mapping.SourceUserName,
                user.Username,
                result.Found,
                result.Matched,
                result.Unmatched);
        }
    }

    private static void Apply(ImportEntry entry, BaseItem item, Khylo.Database.Implementations.Entities.User user, IUserDataManager userDataManager, ImportUserResult result, bool dryRun, CancellationToken cancellationToken)
    {
        var data = userDataManager.GetUserData(user, item);
        if (data is null)
        {
            return;
        }

        var changed = false;
        var watched = false;
        var inProgress = false;
        var plays = 0;
        var favorite = false;
        var keptNewer = false;

        // Shows, albums and artists only carry favorites and ratings across
        var playable = entry.Kind is not (ImportEntryKind.Series or ImportEntryKind.Album or ImportEntryKind.Artist);
        var hasHistory = entry.Played || entry.PositionTicks > 0;

        if (playable && hasHistory)
        {
            var khyloIsNewer = data.LastPlayedDate is { } ours
                && (entry.LastPlayed is not { } theirs ? data.Played || data.PlaybackPositionTicks > 0 : ours >= theirs);

            if (khyloIsNewer)
            {
                keptNewer = true;
            }
            else
            {
                if (entry.Played && !data.Played)
                {
                    data.Played = true;
                    changed = true;
                }

                if (entry.PlayCount > data.PlayCount)
                {
                    plays = entry.PlayCount - data.PlayCount;
                    data.PlayCount = entry.PlayCount;
                    changed = true;
                }

                // Where they stopped; a finished title goes back to the start
                var position = entry.PositionTicks > 0 ? entry.PositionTicks : 0;
                if (data.PlaybackPositionTicks != position && (entry.PositionTicks > 0 || entry.Played))
                {
                    data.PlaybackPositionTicks = position;
                    changed = true;
                }

                if (entry.LastPlayed is { } last && (data.LastPlayedDate is null || last > data.LastPlayedDate))
                {
                    data.LastPlayedDate = last;
                    changed = true;
                }

                watched = entry.Played;
                inProgress = entry.PositionTicks > 0;
            }
        }

        if (entry.Favorite && !data.IsFavorite)
        {
            data.IsFavorite = true;
            favorite = true;
            changed = true;
        }

        if (entry.Rating is > 0 and <= 10 && data.Rating is null)
        {
            data.Rating = entry.Rating;
            changed = true;
        }

        if (changed && !dryRun)
        {
            userDataManager.SaveUserData(user, item, data, UserDataSaveReason.Import, cancellationToken);
        }

        Update(_ =>
        {
            result.Matched++;
            result.Watched += watched ? 1 : 0;
            result.InProgress += inProgress ? 1 : 0;
            result.Plays += plays;
            result.Favorites += favorite ? 1 : 0;
            result.KeptNewer += keptNewer ? 1 : 0;
        });
    }

    private static void Update(Action<ImportStatus> change)
    {
        lock (_lock)
        {
            change(_status);
        }
    }
}
