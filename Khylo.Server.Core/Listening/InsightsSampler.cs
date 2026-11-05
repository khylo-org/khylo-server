using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Api.Helpers;
using Khylo.Controller.MediaEncoding;
using Khylo.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Listening;

/// <summary>
/// Once a minute, writes down how busy the server is: processor, memory, streams, transcodes and bandwidth.
/// </summary>
public sealed class InsightsSampler : BackgroundService
{
    private readonly InsightsStore _store;
    private readonly ISessionManager _sessions;
    private readonly ITranscodeManager _transcodes;
    private readonly ILogger<InsightsSampler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InsightsSampler"/> class.
    /// </summary>
    /// <param name="store">Where the readings are kept.</param>
    /// <param name="sessions">The session manager.</param>
    /// <param name="transcodes">The transcode manager.</param>
    /// <param name="logger">The logger.</param>
    public InsightsSampler(InsightsStore store, ISessionManager sessions, ITranscodeManager transcodes, ILogger<InsightsSampler> logger)
    {
        _store = store;
        _sessions = sessions;
        _transcodes = transcodes;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastTrim = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var now = DateTime.UtcNow;
                _store.Add(now, Take());
                if (now - lastTrim > TimeSpan.FromHours(6))
                {
                    _store.Trim(now);
                    lastTrim = now;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not record a server reading");
            }
        }
    }

    private InsightsSample Take()
    {
        long local = 0, remote = 0;
        var playing = _sessions.Sessions.Where(s => s.NowPlayingItem is not null).ToList();
        foreach (var session in playing)
        {
            var bitrate = (long?)session.TranscodingInfo?.Bitrate ?? session.NowPlayingItem.MediaSources?.FirstOrDefault()?.Bitrate ?? 0;
            if (ListeningNetwork.IsLocal(session.RemoteEndPoint) == false)
            {
                remote += bitrate / 1000;
            }
            else
            {
                local += bitrate / 1000;
            }
        }

        var (used, _) = MachineStats.Memory();
        return new InsightsSample(MachineStats.CpuPercent() ?? 0, used ?? 0, playing.Count, _transcodes.GetActiveJobs().Count, local, remote);
    }
}
