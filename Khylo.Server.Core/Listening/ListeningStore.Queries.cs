using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Khylo.Server.Core.Listening;

/// <summary>
/// The reading side of the listening history.
/// </summary>
public partial class ListeningStore
{
    /// <summary>The Client value that marks made-up test history, so it can be told apart and removed.</summary>
    public const string TestClient = "khylo-test-data";

    /// <summary>
    /// Loads sessions in a time range, oldest first.
    /// </summary>
    /// <param name="userId">One person, or null for everyone who has recording on.</param>
    /// <param name="fromUtc">Start of the range (inclusive).</param>
    /// <param name="toUtc">End of the range (exclusive).</param>
    /// <param name="kinds">The kinds to include.</param>
    /// <returns>The sessions.</returns>
    public IReadOnlyList<ListeningPlay> Load(Guid? userId, DateTime fromUtc, DateTime toUtc, IReadOnlyCollection<string> kinds)
    {
        var where = "StartedAt >= $from AND StartedAt < $to AND Kind IN (" + KindList(kinds) + ")";
        if (userId is { } person)
        {
            where += " AND UserId = $u";
        }
        else
        {
            where += " AND UserId NOT IN (SELECT UserId FROM Settings WHERE Enabled = 0)";
        }

        return Read(where + " ORDER BY StartedAt", command =>
        {
            command.Parameters.AddWithValue("$from", Stamp(fromUtc));
            command.Parameters.AddWithValue("$to", Stamp(toUtc));
            if (userId is { } id)
            {
                command.Parameters.AddWithValue("$u", id.ToString("N"));
            }
        });
    }

    /// <summary>
    /// Loads a page of one person's (or everyone's) sessions, newest first.
    /// </summary>
    /// <param name="userId">One person, or null for everyone who has recording on.</param>
    /// <param name="beforeUtc">Only sessions that started before this.</param>
    /// <param name="limit">The page size.</param>
    /// <param name="kinds">The kinds to include.</param>
    /// <returns>The sessions.</returns>
    public IReadOnlyList<ListeningPlay> LoadBefore(Guid? userId, DateTime beforeUtc, int limit, IReadOnlyCollection<string> kinds)
    {
        var where = "StartedAt < $before AND Kind IN (" + KindList(kinds) + ")";
        where += userId is null ? " AND UserId NOT IN (SELECT UserId FROM Settings WHERE Enabled = 0)" : " AND UserId = $u";
        return Read(where + " ORDER BY StartedAt DESC LIMIT " + Math.Clamp(limit, 1, 200).ToString(CultureInfo.InvariantCulture), command =>
        {
            command.Parameters.AddWithValue("$before", Stamp(beforeUtc));
            if (userId is { } id)
            {
                command.Parameters.AddWithValue("$u", id.ToString("N"));
            }
        });
    }

    /// <summary>
    /// Gets the artist ids, series keys and movie keys a person had played before a moment, to tell what is new.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="beforeUtc">The moment.</param>
    /// <param name="kinds">The kinds to include.</param>
    /// <returns>The sessions that started earlier.</returns>
    public IReadOnlyList<ListeningPlay> LoadEarlier(Guid userId, DateTime beforeUtc, IReadOnlyCollection<string> kinds)
        => Load(userId, DateTime.MinValue.AddYears(1), beforeUtc, kinds);

    /// <summary>
    /// Gets the earliest session of a person, or null when there is none.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="kinds">The kinds to include.</param>
    /// <returns>When it started.</returns>
    public DateTime? FirstPlay(Guid userId, IReadOnlyCollection<string> kinds)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT MIN(StartedAt) FROM Plays WHERE UserId = $u AND Kind IN (" + KindList(kinds) + ")";
            command.Parameters.AddWithValue("$u", userId.ToString("N"));
            return command.ExecuteScalar() is string text
                ? DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
                : null;
        }
    }

    /// <summary>
    /// Gets a stored review (a Wrapped or a quarterly review), which never changes once built.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="key">What it is, for example "wrapped:music:2026".</param>
    /// <returns>The stored JSON, or null.</returns>
    public string? GetSnapshot(Guid userId, string key)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Json FROM Snapshots WHERE UserId = $u AND Key = $k";
            command.Parameters.AddWithValue("$u", userId.ToString("N"));
            command.Parameters.AddWithValue("$k", key);
            return command.ExecuteScalar() as string;
        }
    }

    /// <summary>
    /// Stores a review. The first one stored for a person and key is kept.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="key">What it is.</param>
    /// <param name="json">The review.</param>
    public void PutSnapshot(Guid userId, string key, string json)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO Snapshots (UserId, Key, Json) VALUES ($u, $k, $j)";
            command.Parameters.AddWithValue("$u", userId.ToString("N"));
            command.Parameters.AddWithValue("$k", key);
            command.Parameters.AddWithValue("$j", json);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Deletes the made-up test history of one person and the reviews built from it.
    /// </summary>
    /// <param name="userId">The person.</param>
    public void DeleteTestData(Guid userId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Plays WHERE UserId = $u AND Client = $c; DELETE FROM Snapshots WHERE UserId = $u";
            command.Parameters.AddWithValue("$u", userId.ToString("N"));
            command.Parameters.AddWithValue("$c", TestClient);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Deletes all made-up test history of everyone, including the pretend household members.
    /// </summary>
    public void DeleteAllTestData()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Plays WHERE Client = $c";
            command.Parameters.AddWithValue("$c", TestClient);
            command.ExecuteNonQuery();
        }
    }

    private static string KindList(IReadOnlyCollection<string> kinds)
        => string.Join(",", kinds.Select(k => "'" + k.Replace("'", string.Empty, StringComparison.Ordinal) + "'"));

    private static Guid? GuidOrNull(SqliteDataReader reader, int i)
        => reader.IsDBNull(i) ? null : Guid.ParseExact(reader.GetString(i), "N");

    private static string? TextOrNull(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetString(i);

    private static int? IntOrNull(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetInt32(i);

    private static long? LongOrNull(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetInt64(i);

    private IReadOnlyList<ListeningPlay> Read(string whereAndOrder, Action<SqliteCommand> bind)
    {
        var plays = new List<ListeningPlay>();
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT Id, {Columns} FROM Plays WHERE {whereAndOrder}";
            bind(command);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                plays.Add(new ListeningPlay
                {
                    Id = reader.GetInt64(0),
                    UserId = Guid.ParseExact(reader.GetString(1), "N"),
                    ItemId = Guid.ParseExact(reader.GetString(2), "N"),
                    StartedAt = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                    SecondsListened = reader.GetDouble(4),
                    DurationSeconds = reader.GetDouble(5),
                    Counted = reader.GetInt32(6) != 0,
                    Kind = reader.GetString(7),
                    Client = TextOrNull(reader, 8),
                    DeviceName = TextOrNull(reader, 9),
                    Title = TextOrNull(reader, 10),
                    Album = TextOrNull(reader, 11),
                    AlbumId = GuidOrNull(reader, 12),
                    AlbumArtist = TextOrNull(reader, 13),
                    ArtistsJson = TextOrNull(reader, 14),
                    ArtistIdsJson = TextOrNull(reader, 15),
                    GenresJson = TextOrNull(reader, 16),
                    Year = IntOrNull(reader, 17),
                    SongKey = TextOrNull(reader, 18),
                    AlbumKey = TextOrNull(reader, 19),
                    SeriesId = GuidOrNull(reader, 20),
                    SeriesName = TextOrNull(reader, 21),
                    SeasonNumber = IntOrNull(reader, 22),
                    EpisodeNumber = IntOrNull(reader, 23),
                    LibraryId = GuidOrNull(reader, 24),
                    PlayMethod = TextOrNull(reader, 25),
                    TranscodeReasons = TextOrNull(reader, 26),
                    Resolution = TextOrNull(reader, 27),
                    Bitrate = LongOrNull(reader, 28),
                    VideoCodec = TextOrNull(reader, 29),
                    IsLocal = reader.IsDBNull(30) ? null : reader.GetInt32(30) != 0,
                    BytesSent = LongOrNull(reader, 31),
                    SeriesKey = TextOrNull(reader, 32),
                    MovieKey = TextOrNull(reader, 33)
                });
            }
        }

        return plays;
    }
}
