using System;
using System.Collections.Generic;

namespace Khylo.Controller.Listening;

/// <summary>
/// The listening and watching history the server records and keeps for good. It is private to each person: nothing here
/// reads another person's history.
/// </summary>
public interface IListeningHistory
{
    /// <summary>
    /// Gets whether a person's plays are being recorded (on by default).
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <returns>True when recording is on.</returns>
    bool IsRecordingEnabled(Guid userId);

    /// <summary>
    /// Turns recording on or off for a person. Turning it off keeps what is already stored.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="enabled">Whether to record.</param>
    void SetRecordingEnabled(Guid userId, bool enabled);

    /// <summary>
    /// Deletes all of a person's history.
    /// </summary>
    /// <param name="userId">The person.</param>
    void DeleteForUser(Guid userId);

    /// <summary>
    /// Stores plays made with no connection (downloaded songs). Plays already stored for the same item and start time are ignored.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="plays">The plays.</param>
    /// <returns>How many were new.</returns>
    int AddOfflinePlays(Guid userId, IReadOnlyList<OfflinePlay> plays);
}
