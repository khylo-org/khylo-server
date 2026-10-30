#pragma warning disable RS0030 // Do not use banned APIs

using System;
using System.Threading.Tasks;
using Khylo.Controller.Playlists;
using Khylo.Database.Implementations;
using Khylo.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Playlists;

/// <summary>
/// Implements independent playlist playback tracking to decouple playlist viewing from the main Continue Watching queue.
/// </summary>
public class PlaylistPlaybackStateManager : IPlaylistPlaybackStateManager
{
    private readonly IDbContextFactory<KhyloDbContext> _dbProvider;
    private readonly ILogger<PlaylistPlaybackStateManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistPlaybackStateManager"/> class.
    /// </summary>
    /// <param name="dbProvider">The database context factory.</param>
    /// <param name="logger">The logger.</param>
    public PlaylistPlaybackStateManager(
        IDbContextFactory<KhyloDbContext> dbProvider,
        ILogger<PlaylistPlaybackStateManager> logger)
    {
        _dbProvider = dbProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PlaylistPlaybackStateDto?> GetPlaybackStateAsync(Guid playlistId, Guid userId)
    {
        await using var context = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        var entity = await context.PlaylistPlaybackStates
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PlaylistId == playlistId && p.UserId == userId)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        return new PlaylistPlaybackStateDto
        {
            Id = entity.Id,
            PlaylistId = entity.PlaylistId,
            UserId = entity.UserId,
            CurrentItemId = entity.CurrentItemId,
            PlaybackPositionTicks = entity.PlaybackPositionTicks,
            QueueStateJson = entity.QueueStateJson,
            LastActive = entity.LastActive
        };
    }

    /// <inheritdoc />
    public async Task UpdatePlaybackStateAsync(Guid playlistId, Guid userId, Guid? currentItemId, long positionTicks, string? queueStateJson = null)
    {
        await using var context = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        var entity = await context.PlaylistPlaybackStates
            .FirstOrDefaultAsync(p => p.PlaylistId == playlistId && p.UserId == userId)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new PlaylistPlaybackState
            {
                Id = Guid.NewGuid(),
                PlaylistId = playlistId,
                UserId = userId,
                CurrentItemId = currentItemId,
                PlaybackPositionTicks = positionTicks,
                QueueStateJson = queueStateJson,
                LastActive = DateTime.UtcNow
            };
            context.PlaylistPlaybackStates.Add(entity);
        }
        else
        {
            if (currentItemId.HasValue)
            {
                entity.CurrentItemId = currentItemId;
            }

            entity.PlaybackPositionTicks = positionTicks;
            if (!string.IsNullOrEmpty(queueStateJson))
            {
                entity.QueueStateJson = queueStateJson;
            }

            entity.LastActive = DateTime.UtcNow;
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
        _logger.LogDebug("Updated PlaylistPlaybackState for playlist {PlaylistId}, user {UserId}, item {ItemId}", playlistId, userId, currentItemId);
    }

    /// <inheritdoc />
    public async Task ClearPlaybackStateAsync(Guid playlistId, Guid userId)
    {
        await using var context = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        var entity = await context.PlaylistPlaybackStates
            .FirstOrDefaultAsync(p => p.PlaylistId == playlistId && p.UserId == userId)
            .ConfigureAwait(false);

        if (entity is not null)
        {
            context.PlaylistPlaybackStates.Remove(entity);
            await context.SaveChangesAsync().ConfigureAwait(false);
            _logger.LogDebug("Cleared PlaylistPlaybackState for playlist {PlaylistId}, user {UserId}", playlistId, userId);
        }
    }
}
