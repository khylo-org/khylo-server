using System;
using System.Threading.Tasks;

namespace Khylo.Controller.Playlists
{
    /// <summary>
    /// Manages independent playback state for playlist sessions,
    /// preventing playlist progression from polluting or corrupting episodic Continue Watching queues.
    /// </summary>
    public interface IPlaylistPlaybackStateManager
    {
        /// <summary>
        /// Gets the playback state for a playlist and user.
        /// </summary>
        /// <param name="playlistId">The playlist id.</param>
        /// <param name="userId">The user id.</param>
        /// <returns>A task returning the playlist playback state if present, or null.</returns>
        Task<PlaylistPlaybackStateDto?> GetPlaybackStateAsync(Guid playlistId, Guid userId);

        /// <summary>
        /// Updates or creates the playback state for a playlist session.
        /// </summary>
        /// <param name="playlistId">The playlist id.</param>
        /// <param name="userId">The user id.</param>
        /// <param name="currentItemId">The item id currently playing.</param>
        /// <param name="positionTicks">The position in ticks.</param>
        /// <param name="queueStateJson">Optional serialized queue state.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task UpdatePlaybackStateAsync(Guid playlistId, Guid userId, Guid? currentItemId, long positionTicks, string? queueStateJson = null);

        /// <summary>
        /// Clears the playback state when a playlist finishes or is reset.
        /// </summary>
        /// <param name="playlistId">The playlist id.</param>
        /// <param name="userId">The user id.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task ClearPlaybackStateAsync(Guid playlistId, Guid userId);
    }

    /// <summary>
    /// Data transfer object for playlist playback state.
    /// </summary>
    public class PlaylistPlaybackStateDto
    {
        /// <summary>
        /// Gets or sets the primary key.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the playlist id.
        /// </summary>
        public Guid PlaylistId { get; set; }

        /// <summary>
        /// Gets or sets the user id.
        /// </summary>
        public Guid UserId { get; set; }

        /// <summary>
        /// Gets or sets the current item id.
        /// </summary>
        public Guid? CurrentItemId { get; set; }

        /// <summary>
        /// Gets or sets the position ticks.
        /// </summary>
        public long PlaybackPositionTicks { get; set; }

        /// <summary>
        /// Gets or sets the queue state json.
        /// </summary>
        public string? QueueStateJson { get; set; }

        /// <summary>
        /// Gets or sets the last active timestamp.
        /// </summary>
        public DateTime LastActive { get; set; }
    }
}
