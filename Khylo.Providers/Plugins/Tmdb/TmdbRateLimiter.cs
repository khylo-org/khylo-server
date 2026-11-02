using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Khylo.Providers.Plugins.Tmdb
{
    /// <summary>
    /// Keeps Khylo's calls to the TMDb API under TMDb's limit (about 40 requests a second).
    /// </summary>
    /// <remarks>
    /// The state is shared by every caller, because the limit applies to this server's connection as a whole.
    /// </remarks>
    public static class TmdbRateLimiter
    {
        /// <summary>
        /// The most requests started in any one second. TMDb's limit is about 40; this leaves room to spare.
        /// </summary>
        public const int MaxRequestsPerSecond = 30;

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static readonly Queue<long> RecentStarts = new Queue<long>();

        /// <summary>
        /// Waits until another request to TMDb may be started.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the request may go ahead.</returns>
        public static async Task WaitForTurnAsync(CancellationToken cancellationToken = default)
        {
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                while (true)
                {
                    var now = Stopwatch.GetTimestamp();

                    while (RecentStarts.Count > 0 && now - RecentStarts.Peek() >= Stopwatch.Frequency)
                    {
                        RecentStarts.Dequeue();
                    }

                    if (RecentStarts.Count < MaxRequestsPerSecond)
                    {
                        RecentStarts.Enqueue(now);
                        return;
                    }

                    var wait = TimeSpan.FromSeconds((double)(RecentStarts.Peek() + Stopwatch.Frequency - now) / Stopwatch.Frequency);
                    await Task.Delay(wait < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : wait, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}
