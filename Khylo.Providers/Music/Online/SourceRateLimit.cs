using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Khylo.Providers.Music.Online;

/// <summary>
/// Keeps each online source within its published request limits, so Khylo never has to be told to slow
/// down: requests to one source are spaced out, and when a source does ask for a pause (HTTP 429 or 503),
/// it's left alone for as long as it asked.
/// </summary>
public static class SourceRateLimit
{
    private static readonly ConcurrentDictionary<string, Gate> _gates = new(StringComparer.Ordinal);

    /// <summary>TheAudioDB: about 30 requests a minute on the free key.</summary>
    public const string TheAudioDb = "theaudiodb";

    /// <summary>Wikidata and Wikipedia: one request at a time, gently.</summary>
    public const string Wikimedia = "wikimedia";

    /// <summary>Deezer: 50 requests per 5 seconds.</summary>
    public const string Deezer = "deezer";

    /// <summary>Apple's catalog search: about 20 requests a minute.</summary>
    public const string Apple = "apple";

    /// <summary>
    /// Waits for this source's turn.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>False when the source asked for a pause that's still running (skip it for now).</returns>
    public static async Task<bool> WaitTurnAsync(string source, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(source, s => new Gate(SpacingFor(s)));
        if (DateTime.UtcNow < gate.PausedUntil)
        {
            return false;
        }

        await gate.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = gate.Next - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            gate.Next = DateTime.UtcNow + gate.Spacing;
            return DateTime.UtcNow >= gate.PausedUntil;
        }
        finally
        {
            gate.Lock.Release();
        }
    }

    /// <summary>
    /// Looks at a source's answer and pauses the source when it asked to slow down.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="response">Its answer.</param>
    /// <returns>True when the source asked for a pause.</returns>
    public static bool CheckForPause(string source, HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable))
        {
            return false;
        }

        var retryAfter = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null)
            ?? TimeSpan.FromMinutes(5);
        if (retryAfter < TimeSpan.FromMinutes(1))
        {
            retryAfter = TimeSpan.FromMinutes(1);
        }

        _gates.GetOrAdd(source, s => new Gate(SpacingFor(s))).PausedUntil = DateTime.UtcNow + retryAfter;
        return true;
    }

    private static TimeSpan SpacingFor(string source) => source switch
    {
        TheAudioDb => TimeSpan.FromSeconds(2.1),
        Wikimedia => TimeSpan.FromSeconds(1),
        Deezer => TimeSpan.FromMilliseconds(150),
        Apple => TimeSpan.FromSeconds(3.1),
        _ => TimeSpan.FromSeconds(1)
    };

    private sealed class Gate
    {
        public Gate(TimeSpan spacing) => Spacing = spacing;

        public SemaphoreSlim Lock { get; } = new(1, 1);

        public TimeSpan Spacing { get; }

        public DateTime Next { get; set; } = DateTime.MinValue;

        public DateTime PausedUntil { get; set; } = DateTime.MinValue;
    }
}
