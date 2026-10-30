using System;

namespace Khylo.Database.Implementations.Entities;

/// <summary>
/// Tracks playback progress within a playlist independently from item-level UserData,
/// ensuring playlist playback does not corrupt episodic Continue Watching queues.
/// </summary>
public class PlaylistPlaybackState
{
    /// <summary>
    /// Gets or sets the primary key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the playlist ID.
    /// </summary>
    public Guid PlaylistId { get; set; }

    /// <summary>
    /// Gets or sets the user ID.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the ID of the item currently being played in the playlist.
    /// </summary>
    public Guid? CurrentItemId { get; set; }

    /// <summary>
    /// Gets or sets the current item playback position in ticks.
    /// </summary>
    public long PlaybackPositionTicks { get; set; }

    /// <summary>
    /// Gets or sets serialized queue state (shuffle order, repeat mode, etc.).
    /// </summary>
    public string? QueueStateJson { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this playlist session was last active.
    /// </summary>
    public DateTime LastActive { get; set; }
}
