using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Common.Net;
using Khylo.Controller.Configuration;
using Khylo.Controller.Session;
using Khylo.Controller.Updates;
using Khylo.Database.Implementations.Entities;
using Khylo.Model.Activity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Updates;

/// <summary>
/// Keeps Khylo up to date. It asks the public release page once in a while, remembers what it found, and on a
/// Windows install (the installer's) can fetch the new installer, check it against the published checksum and run it
/// in a separate system task that backs the old version up first and puts it back if the new one doesn't start.
/// Other installs are told, plainly, that they update by hand for now.
/// </summary>
public sealed class KhyloUpdateService : BackgroundService, IKhyloUpdateService
{
    private const string ReleasesAddress = "https://api.github.com/repos/khylo-org/khylo/releases?per_page=30";

    // A checksum list is a few kilobytes and its signature about seventy bytes; anything much bigger isn't one
    private const int MaxChecksumListBytes = 1024 * 1024;
    private const int MaxSignatureBytes = 1024;
    private const int MaxLogLines = 80;

    private static readonly TimeSpan _checkEvery = TimeSpan.FromHours(6);
    private static readonly TimeSpan _wake = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan _pendingGiveUp = TimeSpan.FromMinutes(20);
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    private readonly ILogger<KhyloUpdateService> _logger;
    private readonly IApplicationPaths _paths;
    private readonly IServerConfigurationManager _config;
    private readonly ISessionManager _sessions;
    private readonly IActivityManager _activity;
    private readonly IHttpClientFactory _http;
    private readonly string _statePath;
    private readonly string _updateDir;
    private readonly string _current;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly object _outcomeLock = new();
    private State _state;
    private DateTime? _installingSince;
    private string? _checkError;
    private string? _stage;
    private int? _percent;
    private DateTime _lastOutcomeCheck = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloUpdateService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="paths">The application paths.</param>
    /// <param name="config">The server configuration.</param>
    /// <param name="sessions">The session manager.</param>
    /// <param name="activity">The activity log.</param>
    /// <param name="http">The HTTP client factory.</param>
    public KhyloUpdateService(
        ILogger<KhyloUpdateService> logger,
        IApplicationPaths paths,
        IServerConfigurationManager config,
        ISessionManager sessions,
        IActivityManager activity,
        IHttpClientFactory http)
    {
        _logger = logger;
        _paths = paths;
        _config = config;
        _sessions = sessions;
        _activity = activity;
        _http = http;
        _statePath = Path.Combine(paths.ConfigurationDirectoryPath, "khylo_updates.json");
        _updateDir = Path.Combine(paths.CachePath, "update");
        _current = typeof(KhyloUpdateService).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "KhyloBuild")?.Value ?? string.Empty;
        _state = Load();
    }

    private bool Installing => _installingSince is { } since && DateTime.UtcNow - since < TimeSpan.FromMinutes(15);

    // An update was started (it is saved, so this is true after Khylo restarts too) and is not finished or given up on
    private bool PendingActive => _state.PendingBuild is { } wanted
        && !string.Equals(wanted, _current, StringComparison.OrdinalIgnoreCase)
        && _state.PendingAt is { } at
        && DateTime.UtcNow - at < _pendingGiveUp;

    private bool IsRealBuild => _current.StartsWith("DEV-V", StringComparison.OrdinalIgnoreCase) || _current.StartsWith('V');

    /// <inheritdoc />
    public KhyloUpdateStatus GetStatus()
    {
        // The update's script writes its result after the new Khylo has started, so it is looked for again as pages ask
        RefreshOutcome(false);
        var (can, why) = WhyCantInstall();
        var pendingActive = PendingActive;
        var latest = _state.Latest;
        return new KhyloUpdateStatus
        {
            CurrentBuild = IsRealBuild ? _current : string.Empty,
            Channel = _state.Channel,
            AutoInstall = _state.AutoInstall,
            InstallHour = _state.InstallHour,
            Latest = latest,
            UpdateAvailable = IsRealBuild && latest is not null
                && KhyloReleaseFeed.IsNewer(latest.Build, _current)
                && !string.Equals(latest.Build, _state.SkippedBuild, StringComparison.OrdinalIgnoreCase),
            LastChecked = _state.LastChecked,
            CanInstall = can,
            WhyNot = why,
            Installing = Installing || pendingActive,
            Stage = Installing ? _stage : (pendingActive ? "Installing" : null),
            Percent = Installing ? _percent : null,
            UpdatingTo = Installing || pendingActive ? _state.PendingBuild : null,
            UpdateStartedAt = Installing || pendingActive ? _state.PendingAt : null,
            ServerTime = DateTime.UtcNow,
            UpdateLog = _state.Log.ToList(),
            LastResult = ResultStillWorthShowing() ? _state.LastResult : null,
            CheckError = _checkError
        };
    }

    /// <inheritdoc />
    public async Task<KhyloUpdateStatus> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesAddress);
            request.Headers.UserAgent.ParseAdd("Khylo-Server");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var releases = KhyloReleaseFeed.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), _state.Channel);
            var newest = releases.FirstOrDefault();
            _state.Latest = newest is null ? null : new KhyloUpdateInfo { Build = newest.Tag, PublishedAt = newest.PublishedAt, Notes = KhyloReleaseFeed.WhatsNew(newest.Notes), Url = newest.Url };
            _state.LastChecked = DateTime.UtcNow;
            _checkError = null;
            Save();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning("Couldn't check for a Khylo update: {Reason}", ex.Message);
            _checkError = "Couldn't reach the release page to look for updates. Khylo will try again later.";
        }

        return GetStatus();
    }

    /// <inheritdoc />
    public KhyloUpdateStatus SetSettings(string channel, bool autoInstall)
    {
        var chosen = KhyloReleaseFeed.IsStable(channel) ? "stable" : "dev";
        var channelChanged = !string.Equals(chosen, _state.Channel, StringComparison.Ordinal);
        _state.Channel = chosen;
        _state.AutoInstall = autoInstall;
        if (channelChanged)
        {
            // What was found was for the other channel
            _state.Latest = null;
            _state.SkippedBuild = null;
        }

        Save();
        return GetStatus();
    }

    /// <inheritdoc />
    public KhyloUpdateStatus SkipLatest()
    {
        _state.SkippedBuild = _state.Latest?.Build;
        Save();
        return GetStatus();
    }

    /// <inheritdoc />
    public KhyloUpdateStatus DismissResult()
    {
        _state.LastResult = null;
        _state.LastResultAt = null;
        _state.LastResultBuild = null;
        Save();
        return GetStatus();
    }

    /// <inheritdoc />
    public async Task InstallAsync(string startedBy, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Installing)
            {
                throw new InvalidOperationException("An update is already being installed.");
            }

            var status = GetStatus();
            if (!status.CanInstall)
            {
                throw new InvalidOperationException(status.WhyNot ?? "This install can't update itself.");
            }

            if (!status.UpdateAvailable || status.Latest is null)
            {
                throw new InvalidOperationException("Khylo is already up to date.");
            }

            _installingSince = DateTime.UtcNow;
            _state.Log.Clear();
            _state.LastResult = null;
            _state.LastResultAt = null;
            _state.LastResultBuild = null;
            _state.PendingBuild = status.Latest.Build;
            _state.PendingAt = DateTime.UtcNow;
            AddLog($"Update to {status.Latest.Build} requested by {startedBy}.");
            try
            {
                await PrepareAndHandOverAsync(status.Latest.Build, startedBy, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _installingSince = null;
                _state.PendingBuild = null;
                _state.PendingAt = null;
                AddLog($"The update stopped: {ex.Message}");
                throw;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            RefreshOutcome(true);
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_state.LastChecked is null || DateTime.UtcNow - _state.LastChecked.Value > _checkEvery)
                {
                    await CheckAsync(stoppingToken).ConfigureAwait(false);
                }

                await AutoInstallIfDueAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(_wake, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _lock.Dispose();
        base.Dispose();
    }

    // Only at the quiet hour, and only when nobody is watching
    private async Task AutoInstallIfDueAsync(CancellationToken cancellationToken)
    {
        if (!_state.AutoInstall || Installing)
        {
            return;
        }

        var status = GetStatus();
        if (!status.UpdateAvailable || !status.CanInstall)
        {
            return;
        }

        var hour = DateTime.Now.Hour;
        if (hour != _state.InstallHour)
        {
            return;
        }

        if (_sessions.Sessions.Any(s => s.NowPlayingItem is not null))
        {
            _logger.LogInformation("An update is waiting, but someone is watching, so it will wait");
            return;
        }

        try
        {
            await InstallAsync("automatically", cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("The automatic update didn't start: {Reason}", ex.Message);
        }
    }

    // ---- Windows install ----------------------------------------------------------------------

    private (bool Can, string? Why) WhyCantInstall()
    {
        if (!OperatingSystem.IsWindows())
        {
            if (File.Exists("/.dockerenv") || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            {
                return (false, "Khylo is running in Docker, and a container can't replace its own image. Where you run Docker, run \"docker compose pull\" and then \"docker compose up -d\" (on a NAS, use its pull and recreate buttons). If you installed from a downloaded Docker file, load the new file with \"docker load -i\" and run \"docker compose up -d\". Your library and settings stay as they are.");
            }

            return (false, "On this kind of install Khylo doesn't update itself yet. Download the new version from the release page and install it the way you did before.");
        }

        if (!IsRealBuild)
        {
            return (false, "This is a development copy of Khylo, so it doesn't update itself.");
        }

        if (WindowsUpdate.InstalledFolder() is null)
        {
            return (false, "Khylo can only update itself when it was set up with the Windows installer.");
        }

        if (!Environment.IsPrivilegedProcess)
        {
            // Khylo itself can't install anything, but the installer's update helper (a task that runs as the system) can
            if (WindowsUpdate.HelperInstalled() && Directory.Exists(UpdateHelper.StageFolder(_paths.CachePath)))
            {
                return (true, null);
            }

            return (false, "Khylo is running as a limited Windows account, and Windows wants your permission to update. Download the new installer from the release page.");
        }

        return (true, null);
    }

    private async Task PrepareAndHandOverAsync(string build, string startedBy, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        // The release name goes into file names and into the script the system task runs, so only a real build number is used
        if (!KhyloReleaseFeed.IsValidTag(build))
        {
            throw new InvalidOperationException("The release has an unexpected name, so Khylo won't install it.");
        }

        var release = await FindReleaseAsync(build, cancellationToken).ConfigureAwait(false);
        var version = build.StartsWith("DEV-V", StringComparison.OrdinalIgnoreCase) ? build[5..] : build.TrimStart('V', 'v');
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var fileName = $"khylo-server-{version}-windows-{arch}-setup.exe";
        if (!release.Assets.TryGetValue(fileName, out var address) || !address.StartsWith(KhyloReleaseFeed.DownloadPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The release doesn't have the Windows installer for this PC ({fileName}).");
        }

        if (!release.Assets.TryGetValue(KhyloReleaseFeed.ChecksumsAsset, out var sumsAddress) || !sumsAddress.StartsWith(KhyloReleaseFeed.DownloadPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The release has no checksums to check the download against, so Khylo won't install it.");
        }

        if (!release.Assets.TryGetValue(KhyloReleaseFeed.SignatureAsset, out var signatureAddress) || !signatureAddress.StartsWith(KhyloReleaseFeed.DownloadPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The release isn't signed by Khylo, so Khylo won't install it.");
        }

        using var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Khylo-Server");

        _logger.LogInformation("Downloading Khylo {Build} ({File})", build, fileName);
        AddLog($"Found {build} on the release page. Checking Khylo's signature on its checksum list.");

        // The checksum list says which installer is the real one, so it has to carry Khylo's signature first
        var sumsBytes = await DownloadSmallAsync(client, sumsAddress, MaxChecksumListBytes, cancellationToken).ConfigureAwait(false);
        var signature = await DownloadSmallAsync(client, signatureAddress, MaxSignatureBytes, cancellationToken).ConfigureAwait(false);
        if (!KhyloUpdateSignature.Verify(sumsBytes, signature))
        {
            _logger.LogWarning("The checksum list of {Build} doesn't carry Khylo's signature; not installing it", build);
            throw new InvalidOperationException("The release's checksum list doesn't carry Khylo's signature, so Khylo won't install it.");
        }

        AddLog("The checksum list carries Khylo's signature.");
        var expected = KhyloReleaseFeed.ChecksumFor(Encoding.UTF8.GetString(sumsBytes), fileName)
            ?? throw new InvalidOperationException("The installer isn't in the release's checksum list, so Khylo won't install it.");

        // With enough rights, everything for this update stays in a folder only the system and administrators can open, made
        // that way from the start. Without them (a limited account), it waits in the stage folder for the update helper.
        var direct = Environment.IsPrivilegedProcess;
        var work = direct ? WindowsUpdate.CreatePrivateWorkFolder(_logger) : UpdateHelper.PrepareStageFolder(_paths.CachePath);
        var setup = Path.Combine(work, fileName);

        _stage = "Downloading";
        _percent = 0;
        AddLog($"Downloading {fileName}.");
        using (var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var file = File.Create(setup);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                done += read;
                if (total > 0)
                {
                    _percent = (int)Math.Min(100, done * 100 / total);
                }
            }
        }

        _stage = "Checking";
        _percent = null;
        AddLog("Download finished. Checking it against the checksum.");

        string actual;
        await using (var check = File.OpenRead(setup))
        {
            actual = Convert.ToHexString(await SHA256.HashDataAsync(check, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
        }

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            File.Delete(setup);
            throw new InvalidOperationException("The download didn't match its checksum, so Khylo threw it away and kept the version it has.");
        }

        _stage = "Installing";
        AddLog("The download matches its checksum.");
        var port = _config.GetNetworkConfiguration().InternalHttpPort;
        await _activity.CreateAsync(new ActivityLog($"Khylo is updating to {build} ({startedBy})", "KhyloUpdate", Guid.Empty)).ConfigureAwait(false);
        if (direct)
        {
            var installDir = WindowsUpdate.InstalledFolder() ?? throw new InvalidOperationException("Khylo's folder wasn't found.");
            Save();
            AddLog("Starting the installer. Khylo stops and starts again; this page reconnects by itself.");
            WindowsUpdate.StartInstaller(work, setup, actual, build, port, Path.Combine(work, "result.txt"), installDir, _logger);
        }
        else
        {
            UpdateHelper.HandOver(work, sumsBytes, signature, build, port);

            // The helper works where this account can't see, so how it went is told by the build Khylo comes back as
            AddLog("Handed over to the update helper, which installs it within a few minutes.");
            Save();
            _logger.LogWarning("Staged Khylo {Build} for the update helper, which installs it within a few minutes", build);
        }
    }

    // A file that should be small, read with a limit so a wrong address can't fill memory
    private static async Task<byte[]> DownloadSmallAsync(HttpClient client, string address, int maxBytes, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw new InvalidOperationException("A file of the release is much bigger than it should be, so Khylo won't install it.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + read > maxBytes)
            {
                throw new InvalidOperationException("A file of the release is much bigger than it should be, so Khylo won't install it.");
            }

            await bytes.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return bytes.ToArray();
    }

    private async Task<KhyloReleaseFeed.Release> FindReleaseAsync(string build, CancellationToken cancellationToken)
    {
        using var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesAddress);
        request.Headers.UserAgent.ParseAdd("Khylo-Server");
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var releases = KhyloReleaseFeed.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), _state.Channel);
        return releases.FirstOrDefault(r => string.Equals(r.Tag, build, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("That build isn't on the release page any more. Check again.");
    }

    // The note about the last update is true only while the build it was written for is the one running, so it is shown for that
    // build alone, and for a day at most. A note saved without a build (an older save) is dropped.
    private bool ResultStillWorthShowing() => _state.LastResultAt is { } at
        && DateTime.UtcNow - at < TimeSpan.FromDays(1)
        && string.Equals(_state.LastResultBuild, _current, StringComparison.OrdinalIgnoreCase);

    // Once a different build is running the old note is wrong, so it is removed from the saved settings too
    private void ForgetNoteFromAnotherBuild()
    {
        if (_state.LastResult is null || string.Equals(_state.LastResultBuild, _current, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _state.LastResult = null;
        _state.LastResultAt = null;
        _state.LastResultBuild = null;
        Save();
    }

    // Adds a line to the update log (kept with the other update settings, so it survives Khylo restarting)
    private void AddLog(string text)
    {
        _state.Log.Add(new KhyloUpdateLogLine { At = DateTime.UtcNow, Text = text });
        if (_state.Log.Count > MaxLogLines)
        {
            _state.Log.RemoveRange(0, _state.Log.Count - MaxLogLines);
        }

        Save();
    }

    // Looks at how an update went: at start-up, and again whenever the dashboard asks (at most every ten seconds), because the
    // update's script writes its result only after the new Khylo is up and answering
    private void RefreshOutcome(bool force)
    {
        lock (_outcomeLock)
        {
            if (!force && DateTime.UtcNow - _lastOutcomeCheck < TimeSpan.FromSeconds(10))
            {
                return;
            }

            _lastOutcomeCheck = DateTime.UtcNow;
            ForgetNoteFromAnotherBuild();
            ReadLastResult();
            ReadPendingResult();
        }
    }

    // Says how an update went, once, and closes the update
    private void FinishWith(string note)
    {
        var alreadySaid = string.Equals(_state.LastResult, note, StringComparison.Ordinal)
            && string.Equals(_state.LastResultBuild, _current, StringComparison.OrdinalIgnoreCase);
        _state.LastResult = note;
        _state.LastResultAt = DateTime.UtcNow;
        _state.LastResultBuild = _current;
        _state.PendingBuild = null;
        _state.PendingAt = null;
        if (!alreadySaid)
        {
            AddLog(note);
            _logger.LogInformation("Last update: {Result}", note);
        }
        else
        {
            Save();
        }
    }

    // The note the update task left: in its own folder in the system's update folder, or (from an update an older Khylo started)
    // in Khylo's cache. A limited account can't open the system's folder; it tells how an update went by its build instead.
    private string? LatestResultFile()
    {
        var folders = new List<string> { _updateDir };
        if (OperatingSystem.IsWindows())
        {
            folders.Add(WindowsUpdate.PrivateRoot());
        }

        var candidates = new List<string>();
        foreach (var folder in folders)
        {
            candidates.Add(Path.Combine(folder, "result.txt"));
            try
            {
                if (Directory.Exists(folder))
                {
                    candidates.AddRange(Directory.EnumerateDirectories(folder, "run-*").Select(d => Path.Combine(d, "result.txt")));
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Not an account that may look in there
            }
        }

        return candidates.Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    // An update the update helper was asked to install: it did it if this is that build now, and didn't if the build is still
    // another one a good while later (the helper checks every two minutes, and an install takes a few)
    private void ReadPendingResult()
    {
        if (_state.PendingBuild is not { } wanted)
        {
            return;
        }

        if (string.Equals(wanted, _current, StringComparison.OrdinalIgnoreCase))
        {
            FinishWith($"Khylo updated to {_current}.");
        }
        else if (_state.PendingAt is { } at && DateTime.UtcNow - at > _pendingGiveUp)
        {
            FinishWith($"The update to {wanted} didn't finish, so Khylo is still on {_current}. You can run the installer from the release page yourself.");
        }
    }

    // What the last update did, left by the update task. A result is only believed if it fits what is running now: "ok" must be for
    // the running build, and a roll-back or failure must be recent. Anything else is a leftover from an earlier update and is
    // thrown away, so it can never be shown as if it were about this one.
    private void ReadLastResult()
    {
        try
        {
            var path = LatestResultFile();
            if (path is null)
            {
                return;
            }

            var parts = File.ReadAllText(path).Trim().Split('|');
            var written = File.GetLastWriteTimeUtc(path);
            var folder = Path.GetDirectoryName(path);
            File.Delete(path);

            var kind = parts[0];
            var build = parts.Length > 1 ? parts[1] : string.Empty;
            var recent = DateTime.UtcNow - written < TimeSpan.FromMinutes(45);
            string? note = null;
            if (kind == "ok" && string.Equals(build, _current, StringComparison.OrdinalIgnoreCase))
            {
                note = $"Khylo updated to {_current}.";
            }
            else if (kind == "rolledback" && recent && !string.Equals(build, _current, StringComparison.OrdinalIgnoreCase))
            {
                note = $"The update to {build} didn't start properly, so Khylo went back to the version it had.";
            }
            else if (kind == "failed" && recent)
            {
                note = "The last update didn't finish.";
            }

            if (note is null)
            {
                _logger.LogInformation("Ignored an old update result ({Kind} for {Build}); Khylo is running {Current}", kind, build, _current);
                return;
            }

            ImportInstallerLog(folder);
            FinishWith(note);
        }
        catch (IOException ex)
        {
            _logger.LogWarning("Couldn't read the last update's result: {Reason}", ex.Message);
        }
    }

    // What the installer's own script did (backup, install, waiting for the new Khylo), added to the update log
    private void ImportInstallerLog(string? folder)
    {
        try
        {
            var file = folder is null ? null : Path.Combine(folder, "update.log");
            if (file is null || !File.Exists(file))
            {
                return;
            }

            foreach (var line in File.ReadAllLines(file).Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(30))
            {
                _state.Log.Add(new KhyloUpdateLogLine { At = File.GetLastWriteTimeUtc(file), Text = "Installer: " + line.Trim() });
            }
        }
        catch (IOException)
        {
            // The installer's own log is a bonus; the update's result doesn't depend on it
        }
        catch (UnauthorizedAccessException)
        {
            // An account that may not look in the system's update folder
        }
    }

    // ---- Storage ------------------------------------------------------------------------------

    private State Load()
    {
        try
        {
            if (File.Exists(_statePath) && JsonSerializer.Deserialize<State>(File.ReadAllText(_statePath)) is { } saved)
            {
                return saved;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning("Couldn't read the update settings: {Reason}", ex.Message);
        }

        // A development build follows every build; a finished release follows finished releases
        return new State { Channel = _current.StartsWith("DEV-", StringComparison.OrdinalIgnoreCase) ? "dev" : "stable" };
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_statePath, JsonSerializer.Serialize(_state, _json));
        }
        catch (IOException ex)
        {
            _logger.LogWarning("Couldn't save the update settings: {Reason}", ex.Message);
        }
    }

    private sealed class State
    {
        public string Channel { get; set; } = "stable";

        public bool AutoInstall { get; set; }

        public int InstallHour { get; set; } = 3;

        public string? SkippedBuild { get; set; }

        public DateTime? LastChecked { get; set; }

        public KhyloUpdateInfo? Latest { get; set; }

        public string? LastResult { get; set; }

        public DateTime? LastResultAt { get; set; }

        // The build that was running when the note was written
        public string? LastResultBuild { get; set; }

        // An update left for the update helper (a limited account can't see how it goes), and when
        public string? PendingBuild { get; set; }

        public DateTime? PendingAt { get; set; }

        // What the last or current update has done, oldest first
        public List<KhyloUpdateLogLine> Log { get; set; } = new();
    }
}
