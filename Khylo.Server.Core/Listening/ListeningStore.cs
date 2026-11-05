using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Khylo.Common.Configuration;
using Khylo.Controller.Library;
using Khylo.Controller.Listening;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Listening;

/// <summary>
/// Keeps the listening and watching history in its own SQLite file (<c>listening.db</c> in the data folder), which the server's
/// backups include. Nothing here is written to the activity log or the server log except counts.
/// </summary>
public partial class ListeningStore : IListeningHistory
{
    private const string Columns =
        "UserId, ItemId, StartedAt, SecondsListened, DurationSeconds, Counted, Kind, Client, DeviceName, Title, Album, AlbumId, AlbumArtist, " +
        "ArtistsJson, ArtistIdsJson, GenresJson, Year, SongKey, AlbumKey, SeriesId, SeriesName, SeasonNumber, EpisodeNumber, LibraryId, " +
        "PlayMethod, TranscodeReasons, Resolution, Bitrate, VideoCodec, IsLocal, BytesSent, SeriesKey, MovieKey";

    private readonly object _gate = new();
    private readonly string _connectionString;
    private readonly ILibraryManager _library;
    private readonly ILogger<ListeningStore> _logger;
    private readonly ConcurrentDictionary<Guid, bool> _enabled = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ListeningStore"/> class.
    /// </summary>
    /// <param name="paths">The application paths.</param>
    /// <param name="library">The library manager.</param>
    /// <param name="logger">The logger.</param>
    public ListeningStore(IApplicationPaths paths, ILibraryManager library, ILogger<ListeningStore> logger)
    {
        _library = library;
        _logger = logger;
        DirectoryPath = Path.Combine(paths.DataPath, "listening");
        Directory.CreateDirectory(DirectoryPath);
        FilePath = Path.Combine(DirectoryPath, "listening.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = FilePath, Pooling = true }.ToString();
        CreateSchema();
    }

    /// <summary>
    /// Gets the folder the history lives in.
    /// </summary>
    public string DirectoryPath { get; }

    /// <summary>
    /// Gets the history file.
    /// </summary>
    public string FilePath { get; }

    /// <inheritdoc />
    public bool IsRecordingEnabled(Guid userId)
    {
        if (_enabled.TryGetValue(userId, out var known))
        {
            return known;
        }

        var enabled = true;
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Enabled FROM Settings WHERE UserId = $u";
            command.Parameters.AddWithValue("$u", userId.ToString("N"));
            if (command.ExecuteScalar() is long value)
            {
                enabled = value != 0;
            }
        }

        _enabled[userId] = enabled;
        return enabled;
    }

    /// <inheritdoc />
    public void SetRecordingEnabled(Guid userId, bool enabled)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Settings (UserId, Enabled) VALUES ($u, $e) ON CONFLICT(UserId) DO UPDATE SET Enabled = $e";
            command.Parameters.AddWithValue("$u", userId.ToString("N"));
            command.Parameters.AddWithValue("$e", enabled ? 1 : 0);
            command.ExecuteNonQuery();
        }

        _enabled[userId] = enabled;
    }

    /// <inheritdoc />
    public void DeleteForUser(Guid userId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Plays WHERE UserId = $u; DELETE FROM Settings WHERE UserId = $u";
            command.Parameters.AddWithValue("$u", userId.ToString("N"));
            command.ExecuteNonQuery();
        }

        _enabled.TryRemove(userId, out _);
        _logger.LogInformation("Deleted a person's listening history");
    }

    /// <inheritdoc />
    public int AddOfflinePlays(Guid userId, IReadOnlyList<OfflinePlay> plays)
    {
        if (!IsRecordingEnabled(userId))
        {
            return 0;
        }

        var added = 0;
        foreach (var offline in plays)
        {
            if (_library.GetItemById(offline.ItemId) is not { } item || ListeningSnapshot.Describe(item, _library) is not { } play)
            {
                continue;
            }

            play.UserId = userId;
            play.StartedAt = offline.StartedAt.ToUniversalTime();
            play.SecondsListened = Math.Max(0, Math.Min(offline.SecondsListened, play.DurationSeconds > 0 ? play.DurationSeconds * 2 : offline.SecondsListened));
            play.Counted = ListeningKeys.IsCounted(false, play.SecondsListened, play.DurationSeconds);
            play.Client = "offline";
            if (Insert(play, out var isNew) > 0 && isNew)
            {
                added++;
            }
        }

        _logger.LogInformation("Stored {Count} offline plays", added);
        return added;
    }

    /// <summary>
    /// Stores a new session, unless the same person already has one for the same item starting within a few seconds (a re-sent report).
    /// </summary>
    /// <param name="play">The session.</param>
    /// <param name="isNew">Set to false when an existing row was reused.</param>
    /// <returns>The row id.</returns>
    public long Insert(ListeningPlay play, out bool isNew)
    {
        lock (_gate)
        {
            using var connection = Open();
            using (var find = connection.CreateCommand())
            {
                find.CommandText = "SELECT Id FROM Plays WHERE UserId = $u AND ItemId = $i AND StartedAt BETWEEN $a AND $b LIMIT 1";
                find.Parameters.AddWithValue("$u", play.UserId.ToString("N"));
                find.Parameters.AddWithValue("$i", play.ItemId.ToString("N"));
                find.Parameters.AddWithValue("$a", Stamp(play.StartedAt.AddSeconds(-5)));
                find.Parameters.AddWithValue("$b", Stamp(play.StartedAt.AddSeconds(5)));
                if (find.ExecuteScalar() is long existing)
                {
                    isNew = false;
                    return existing;
                }
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO Plays ({Columns}) VALUES ($UserId, $ItemId, $StartedAt, $SecondsListened, $DurationSeconds, $Counted, $Kind, $Client, $DeviceName, $Title, $Album, $AlbumId, $AlbumArtist, $ArtistsJson, $ArtistIdsJson, $GenresJson, $Year, $SongKey, $AlbumKey, $SeriesId, $SeriesName, $SeasonNumber, $EpisodeNumber, $LibraryId, $PlayMethod, $TranscodeReasons, $Resolution, $Bitrate, $VideoCodec, $IsLocal, $BytesSent, $SeriesKey, $MovieKey); SELECT last_insert_rowid()";
            Bind(command, "$UserId", play.UserId.ToString("N"));
            Bind(command, "$ItemId", play.ItemId.ToString("N"));
            Bind(command, "$StartedAt", Stamp(play.StartedAt));
            Bind(command, "$SecondsListened", play.SecondsListened);
            Bind(command, "$DurationSeconds", play.DurationSeconds);
            Bind(command, "$Counted", play.Counted ? 1 : 0);
            Bind(command, "$Kind", play.Kind);
            Bind(command, "$Client", play.Client);
            Bind(command, "$DeviceName", play.DeviceName);
            Bind(command, "$Title", play.Title);
            Bind(command, "$Album", play.Album);
            Bind(command, "$AlbumId", play.AlbumId?.ToString("N"));
            Bind(command, "$AlbumArtist", play.AlbumArtist);
            Bind(command, "$ArtistsJson", play.ArtistsJson);
            Bind(command, "$ArtistIdsJson", play.ArtistIdsJson);
            Bind(command, "$GenresJson", play.GenresJson);
            Bind(command, "$Year", play.Year);
            Bind(command, "$SongKey", play.SongKey);
            Bind(command, "$AlbumKey", play.AlbumKey);
            Bind(command, "$SeriesId", play.SeriesId?.ToString("N"));
            Bind(command, "$SeriesName", play.SeriesName);
            Bind(command, "$SeasonNumber", play.SeasonNumber);
            Bind(command, "$EpisodeNumber", play.EpisodeNumber);
            Bind(command, "$LibraryId", play.LibraryId?.ToString("N"));
            Bind(command, "$PlayMethod", play.PlayMethod);
            Bind(command, "$TranscodeReasons", play.TranscodeReasons);
            Bind(command, "$Resolution", play.Resolution);
            Bind(command, "$Bitrate", play.Bitrate);
            Bind(command, "$VideoCodec", play.VideoCodec);
            Bind(command, "$IsLocal", play.IsLocal is { } local ? (local ? 1 : 0) : null);
            Bind(command, "$BytesSent", play.BytesSent);
            Bind(command, "$SeriesKey", play.SeriesKey);
            Bind(command, "$MovieKey", play.MovieKey);
            isNew = true;
            return (long)command.ExecuteScalar()!;
        }
    }

    /// <summary>
    /// Writes how far a session has got.
    /// </summary>
    /// <param name="id">The row id.</param>
    /// <param name="seconds">Time heard so far.</param>
    /// <param name="counted">Whether it is a real play yet.</param>
    /// <param name="bytesSent">The estimated bytes sent so far, if known.</param>
    /// <param name="playMethod">How it is being played, once known (an earlier value is kept when null).</param>
    /// <param name="transcodeReasons">Why it is being transcoded, once known (an earlier value is kept when null).</param>
    public void Update(long id, double seconds, bool counted, long? bytesSent, string? playMethod = null, string? transcodeReasons = null)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Plays SET SecondsListened = $s, Counted = $c, BytesSent = $b, PlayMethod = COALESCE($pm, PlayMethod), TranscodeReasons = COALESCE($tr, TranscodeReasons) WHERE Id = $id";
            command.Parameters.AddWithValue("$s", seconds);
            command.Parameters.AddWithValue("$c", counted ? 1 : 0);
            Bind(command, "$b", bytesSent);
            Bind(command, "$pm", playMethod);
            Bind(command, "$tr", transcodeReasons);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    private static string Stamp(DateTime time) => time.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static void Bind(SqliteCommand command, string name, object? value)
        => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void CreateSchema()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS Plays (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId TEXT NOT NULL, ItemId TEXT NOT NULL, StartedAt TEXT NOT NULL,
                    SecondsListened REAL NOT NULL, DurationSeconds REAL NOT NULL, Counted INTEGER NOT NULL, Kind TEXT NOT NULL,
                    Client TEXT, DeviceName TEXT, Title TEXT, Album TEXT, AlbumId TEXT, AlbumArtist TEXT,
                    ArtistsJson TEXT, ArtistIdsJson TEXT, GenresJson TEXT, Year INTEGER, SongKey TEXT, AlbumKey TEXT,
                    SeriesId TEXT, SeriesName TEXT, SeasonNumber INTEGER, EpisodeNumber INTEGER, LibraryId TEXT,
                    PlayMethod TEXT, TranscodeReasons TEXT, Resolution TEXT, Bitrate INTEGER, VideoCodec TEXT,
                    IsLocal INTEGER, BytesSent INTEGER, SeriesKey TEXT, MovieKey TEXT
                );
                CREATE INDEX IF NOT EXISTS IX_Plays_UserId_StartedAt ON Plays (UserId, StartedAt);
                CREATE TABLE IF NOT EXISTS Settings (UserId TEXT PRIMARY KEY, Enabled INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS Snapshots (UserId TEXT NOT NULL, Key TEXT NOT NULL, Json TEXT NOT NULL, PRIMARY KEY (UserId, Key));
                """;
            command.ExecuteNonQuery();
        }
    }
}
