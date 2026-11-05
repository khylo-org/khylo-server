using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Khylo.Common.Configuration;
using Microsoft.Data.Sqlite;

namespace Khylo.Server.Core.Listening;

/// <summary>
/// The server's own measurements over time, in their own small file (<c>insights.db</c>): one reading a minute kept for 90 days,
/// then one a hour for two years.
/// </summary>
public class InsightsStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="InsightsStore"/> class.
    /// </summary>
    /// <param name="paths">The application paths.</param>
    public InsightsStore(IApplicationPaths paths)
    {
        var folder = Path.Combine(paths.DataPath, "insights");
        Directory.CreateDirectory(folder);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, "insights.db"), Pooling = true }.ToString();
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS Minute (Ts INTEGER PRIMARY KEY, Cpu REAL, MemoryUsed INTEGER, Streams INTEGER, Transcodes INTEGER, BandwidthLocalKbps INTEGER, BandwidthRemoteKbps INTEGER);
                CREATE TABLE IF NOT EXISTS Hour (Ts INTEGER PRIMARY KEY, Cpu REAL, MemoryUsed INTEGER, Streams INTEGER, Transcodes INTEGER, BandwidthLocalKbps INTEGER, BandwidthRemoteKbps INTEGER);
                """;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Stores one reading.
    /// </summary>
    /// <param name="at">When it was taken.</param>
    /// <param name="sample">The reading.</param>
    public void Add(DateTime at, InsightsSample sample)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO Minute VALUES ($ts, $cpu, $mem, $streams, $transcodes, $local, $remote)";
            command.Parameters.AddWithValue("$ts", Epoch(at) / 60);
            command.Parameters.AddWithValue("$cpu", sample.Cpu);
            command.Parameters.AddWithValue("$mem", sample.MemoryUsed);
            command.Parameters.AddWithValue("$streams", sample.Streams);
            command.Parameters.AddWithValue("$transcodes", sample.Transcodes);
            command.Parameters.AddWithValue("$local", sample.BandwidthLocalKbps);
            command.Parameters.AddWithValue("$remote", sample.BandwidthRemoteKbps);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Folds minute readings older than 90 days into hourly ones, and forgets hourly ones older than two years.
    /// </summary>
    /// <param name="now">The current time.</param>
    public void Trim(DateTime now)
    {
        var cutoffMinute = Epoch(now.AddDays(-90)) / 60;
        var cutoffHour = Epoch(now.AddYears(-2)) / 3600;
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR REPLACE INTO Hour
                SELECT Ts / 60, AVG(Cpu), CAST(AVG(MemoryUsed) AS INTEGER), MAX(Streams), MAX(Transcodes), CAST(AVG(BandwidthLocalKbps) AS INTEGER), CAST(AVG(BandwidthRemoteKbps) AS INTEGER)
                FROM Minute WHERE Ts < $minute GROUP BY Ts / 60;
                DELETE FROM Minute WHERE Ts < $minute;
                DELETE FROM Hour WHERE Ts < $hour;
                """;
            command.Parameters.AddWithValue("$minute", cutoffMinute);
            command.Parameters.AddWithValue("$hour", cutoffHour);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Reads the points in a range, grouped into steps.
    /// </summary>
    /// <param name="from">Start of the range.</param>
    /// <param name="to">End of the range.</param>
    /// <param name="stepMinutes">The size of each point in minutes.</param>
    /// <returns>The points, oldest first.</returns>
    public IReadOnlyList<Dictionary<string, object?>> Points(DateTime from, DateTime to, int stepMinutes)
    {
        var step = Math.Max(1, stepMinutes);
        var points = new SortedDictionary<long, List<InsightsSample>>();
        lock (_gate)
        {
            using var connection = Open();
            foreach (var (table, scale) in new[] { ("Hour", 60L), ("Minute", 1L) })
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT Ts, Cpu, MemoryUsed, Streams, Transcodes, BandwidthLocalKbps, BandwidthRemoteKbps FROM {table} WHERE Ts >= $a AND Ts <= $b ORDER BY Ts";
                command.Parameters.AddWithValue("$a", Epoch(from) / (60 * scale));
                command.Parameters.AddWithValue("$b", Epoch(to) / (60 * scale));
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var minute = reader.GetInt64(0) * scale;
                    var bucket = minute / step * step;
                    if (!points.TryGetValue(bucket, out var list))
                    {
                        points[bucket] = list = [];
                    }

                    list.Add(new InsightsSample(reader.GetDouble(1), reader.GetInt64(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt64(5), reader.GetInt64(6)));
                }
            }
        }

        var result = new List<Dictionary<string, object?>>();
        foreach (var (bucket, list) in points)
        {
            var count = list.Count;
            var cpu = 0.0;
            long memory = 0, local = 0, remote = 0;
            int streams = 0, transcodes = 0;
            foreach (var s in list)
            {
                cpu += s.Cpu;
                memory += s.MemoryUsed;
                local += s.BandwidthLocalKbps;
                remote += s.BandwidthRemoteKbps;
                streams = Math.Max(streams, s.Streams);
                transcodes = Math.Max(transcodes, s.Transcodes);
            }

            result.Add(new Dictionary<string, object?>
            {
                ["At"] = DateTimeOffset.FromUnixTimeSeconds(bucket * 60).UtcDateTime,
                ["Cpu"] = Math.Round(cpu / count, 1),
                ["MemoryUsed"] = memory / count,
                ["Streams"] = streams,
                ["Transcodes"] = transcodes,
                ["BandwidthLocalKbps"] = local / count,
                ["BandwidthRemoteKbps"] = remote / count
            });
        }

        return result;
    }

    private static long Epoch(DateTime time) => new DateTimeOffset(DateTime.SpecifyKind(time.ToUniversalTime(), DateTimeKind.Utc)).ToUnixTimeSeconds();

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
