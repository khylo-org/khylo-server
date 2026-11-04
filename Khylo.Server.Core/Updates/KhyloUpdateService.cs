using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
    private const string TaskName = "Khylo Update";

    // Runs as the system account in its own one-time task: back up, install, check Khylo answers, or put the old one back
    private const string UpdateScript = """
        $ErrorActionPreference = 'Continue'
        $log = '{{UPDATE_DIR}}\update.log'
        function Log($m) { Add-Content -Path $log -Value ("{0} {1}" -f (Get-Date -Format s), $m) }
        $install = '{{INSTALL_DIR}}'
        $backup = '{{UPDATE_DIR}}\previous'
        $setup = '{{SETUP}}'
        Log 'Update to {{BUILD}} started'
        schtasks /End /TN "Khylo Server" | Out-Null
        Start-Sleep -Seconds 5
        Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue
        robocopy $install $backup /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
        Log 'Backed up the current version'
        $p = Start-Process -FilePath $setup -ArgumentList '/S' -Wait -PassThru
        Log ("Installer finished with {0}" -f $p.ExitCode)
        $ok = $false
        for ($i = 0; $i -lt 36; $i++) {
            Start-Sleep -Seconds 5
            try {
                $r = Invoke-WebRequest -Uri 'http://127.0.0.1:{{PORT}}/health' -UseBasicParsing -TimeoutSec 5
                if ($r.Content -match 'Healthy') { $ok = $true; break }
            } catch { }
        }
        if ($ok) {
            Log 'The new version is answering'
            Set-Content -Path '{{RESULT}}' -Value 'ok|{{BUILD}}'
        } else {
            Log 'The new version did not start; putting the old one back'
            schtasks /End /TN "Khylo Server" | Out-Null
            Start-Sleep -Seconds 5
            robocopy $backup $install /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
            schtasks /Run /TN "Khylo Server" | Out-Null
            Set-Content -Path '{{RESULT}}' -Value 'rolledback|{{BUILD}}'
        }
        Remove-Item $setup -Force -ErrorAction SilentlyContinue
        schtasks /Delete /TN "Khylo Update" /F | Out-Null
        """;

    private static readonly TimeSpan _checkEvery = TimeSpan.FromHours(6);
    private static readonly TimeSpan _wake = TimeSpan.FromMinutes(30);
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
    private State _state;
    private DateTime? _installingSince;
    private string? _checkError;
    private string? _stage;
    private int? _percent;

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

    private bool IsRealBuild => _current.StartsWith("DEV-V", StringComparison.OrdinalIgnoreCase) || _current.StartsWith('V');

    /// <inheritdoc />
    public KhyloUpdateStatus GetStatus()
    {
        var (can, why) = WhyCantInstall();
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
            Installing = Installing,
            Stage = Installing ? _stage : null,
            Percent = Installing ? _percent : null,
            LastResult = _state.LastResult,
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
            try
            {
                await PrepareAndHandOverAsync(status.Latest.Build, startedBy, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _installingSince = null;
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
            ReadLastResult();
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
            return (false, "On this kind of install Khylo doesn't update itself yet. Download the new version from the release page and install it the way you did before.");
        }

        if (!IsRealBuild)
        {
            return (false, "This is a development copy of Khylo, so it doesn't update itself.");
        }

        if (InstalledFolder() is null)
        {
            return (false, "Khylo can only update itself when it was set up with the Windows installer.");
        }

        if (!Environment.IsPrivilegedProcess)
        {
            return (false, "Khylo is running as a regular Windows user, and Windows wants your permission to update. Download the new installer from the release page.");
        }

        return (true, null);
    }

    // The folder the installer put Khylo in, when this is that copy
    private static string? InstalledFolder()
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } exe)
        {
            return null;
        }

        var folder = Path.GetDirectoryName(exe);
        if (folder is null || !string.Equals(Path.GetFileName(exe), "khylo.exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var key = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64).OpenSubKey(@"Software\Khylo");
        var registered = key?.GetValue("InstallDir") as string;
        return registered is not null && string.Equals(Path.TrimEndingDirectorySeparator(registered), Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase)
            && !folder.Contains('\'', StringComparison.Ordinal)
            ? folder
            : null;
    }

    private async Task PrepareAndHandOverAsync(string build, string startedBy, CancellationToken cancellationToken)
    {
        var release = await FindReleaseAsync(build, cancellationToken).ConfigureAwait(false);
        var version = build.StartsWith("DEV-V", StringComparison.OrdinalIgnoreCase) ? build[5..] : build.TrimStart('V', 'v');
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var fileName = $"khylo-server-{version}-windows-{arch}-setup.exe";
        if (!release.Assets.TryGetValue(fileName, out var address) || !address.StartsWith(KhyloReleaseFeed.DownloadPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The release doesn't have the Windows installer for this PC ({fileName}).");
        }

        if (!release.Assets.TryGetValue("SHA256SUMS.txt", out var sumsAddress) || !sumsAddress.StartsWith(KhyloReleaseFeed.DownloadPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The release has no checksums to check the download against, so Khylo won't install it.");
        }

        Directory.CreateDirectory(_updateDir);
        var setup = Path.Combine(_updateDir, fileName);
        using var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Khylo-Server");

        _logger.LogInformation("Downloading Khylo {Build} ({File})", build, fileName);
        var sums = await client.GetStringAsync(sumsAddress, cancellationToken).ConfigureAwait(false);
        var expected = KhyloReleaseFeed.ChecksumFor(sums, fileName)
            ?? throw new InvalidOperationException("The installer isn't in the release's checksum list, so Khylo won't install it.");

        _stage = "Downloading";
        _percent = 0;
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
        var port = _config.GetNetworkConfiguration().InternalHttpPort;
        await _activity.CreateAsync(new ActivityLog($"Khylo is updating to {build} ({startedBy})", "KhyloUpdate", Guid.Empty)).ConfigureAwait(false);
        StartInstaller(setup, build, port);
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

    // A separate one-time system task runs the update, so it isn't stopped when the installer stops Khylo's own task
    private void StartInstaller(string setup, string build, int port)
    {
        var installDir = InstalledFolder() ?? throw new InvalidOperationException("Khylo's folder wasn't found.");
        var script = Path.Combine(_updateDir, "update.ps1");
        var result = Path.Combine(_updateDir, "result.txt");
        File.WriteAllText(script, UpdateScript
            .Replace("{{UPDATE_DIR}}", _updateDir, StringComparison.Ordinal)
            .Replace("{{INSTALL_DIR}}", installDir, StringComparison.Ordinal)
            .Replace("{{SETUP}}", setup, StringComparison.Ordinal)
            .Replace("{{RESULT}}", result, StringComparison.Ordinal)
            .Replace("{{BUILD}}", build, StringComparison.Ordinal)
            .Replace("{{PORT}}", port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));

        Run("schtasks.exe", "/Create", "/TN", TaskName, "/TR", $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{script}\"", "/SC", "ONCE", "/ST", "00:00", "/RU", "SYSTEM", "/RL", "HIGHEST", "/F");
        Run("schtasks.exe", "/Run", "/TN", TaskName);
        _logger.LogWarning("Handed Khylo {Build} to the installer; Khylo will restart in a moment", build);
    }

    private static void Run(string file, params string[] args)
    {
        var info = new ProcessStartInfo(file) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Windows wouldn't start the update.");
        process.WaitForExit(30_000);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Windows wouldn't schedule the update: " + process.StandardError.ReadToEnd().Trim());
        }
    }

    // What the last update did, left by the update task
    private void ReadLastResult()
    {
        try
        {
            var path = Path.Combine(_updateDir, "result.txt");
            if (!File.Exists(path))
            {
                return;
            }

            var parts = File.ReadAllText(path).Trim().Split('|');
            File.Delete(path);
            var build = parts.Length > 1 ? parts[1] : string.Empty;
            _state.LastResult = parts[0] switch
            {
                "ok" => $"Khylo updated to {build}.",
                "rolledback" => $"The update to {build} didn't start properly, so Khylo went back to the version it had.",
                _ => "The last update didn't finish."
            };
            Save();
            _logger.LogInformation("Last update: {Result}", _state.LastResult);
        }
        catch (IOException ex)
        {
            _logger.LogWarning("Couldn't read the last update's result: {Reason}", ex.Message);
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
    }
}
