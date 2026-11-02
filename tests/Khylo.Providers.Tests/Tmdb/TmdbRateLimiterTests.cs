using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Khylo.Providers.Plugins.Tmdb;
using Xunit;

namespace Khylo.Providers.Tests.Tmdb
{
    public class TmdbRateLimiterTests
    {
        [Fact]
        public async Task WaitForTurnAsync_NeverAllowsMoreThanTheLimitInOneSecond()
        {
            var total = TmdbRateLimiter.MaxRequestsPerSecond * 2;
            var timer = Stopwatch.StartNew();

            await Task.WhenAll(Enumerable.Range(0, total).Select(_ => TmdbRateLimiter.WaitForTurnAsync(TestContext.Current.CancellationToken)));
            timer.Stop();

            // The first batch goes straight away; the rest must wait for the next second.
            Assert.True(timer.Elapsed >= TimeSpan.FromMilliseconds(900), $"Finished in {timer.Elapsed}");
        }

        [Fact]
        public async Task WaitForTurnAsync_DoesNotDelayRequestsUnderTheLimit()
        {
            // Let any earlier test's burst age out of the one-second window.
            await Task.Delay(TimeSpan.FromMilliseconds(1100), TestContext.Current.CancellationToken);
            var timer = Stopwatch.StartNew();

            for (var i = 0; i < 5; i++)
            {
                await TmdbRateLimiter.WaitForTurnAsync(TestContext.Current.CancellationToken);
            }

            Assert.True(timer.Elapsed < TimeSpan.FromMilliseconds(500), $"Took {timer.Elapsed}");
        }
    }
}
