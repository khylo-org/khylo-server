using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Khylo.Api.Extensions;
using Khylo.Api.Helpers;
using Khylo.Common.Api;
using Khylo.Common.Configuration;
using Khylo.Common.Extensions;
using Khylo.Controller;
using Khylo.Controller.Library;
using Khylo.Controller.MediaEncoding;
using Khylo.Controller.Session;
using Khylo.Database.Implementations.Entities;
using Khylo.Model.Activity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Khylo.Api.Controllers;

/// <summary>
/// What the Khylo Manager app (admins only) uses to look after the server from a phone: the machine's health,
/// Khylo's own processes, a live log and a guarded reboot. Only fixed actions: no commands, no file paths, and
/// nothing can touch a process Khylo didn't start. Every action is written to the activity log with the admin's name.
/// </summary>
[Route("Khylo/Manage")]
[Authorize(Policy = Policies.RequiresElevation)]
public class KhyloManageController : BaseKhyloApiController
{
    private const int TailDefaultBytes = 128 * 1024;
    private const int TailMaxBytes = 1024 * 1024;
    private const int TailFirstBytes = 64 * 1024;
    private const int MaxRebootAttemptsPerMinute = 5;
    private static readonly TimeSpan _codeLifetime = TimeSpan.FromSeconds(60);

    private static readonly object _rebootLock = new();
    private static readonly Dictionary<Guid, (string Code, DateTime Expires)> _rebootCodes = [];
    private static readonly Dictionary<string, List<DateTime>> _rebootAttempts = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<int, (DateTime At, TimeSpan Cpu)> _cpuSamples = new();

    private readonly ILogger<KhyloManageController> _logger;
    private readonly IServerApplicationHost _appHost;
    private readonly IApplicationPaths _appPaths;
    private readonly ISystemManager _systemManager;
    private readonly ISessionManager _sessionManager;
    private readonly ITranscodeManager _transcodeManager;
    private readonly IActivityManager _activityManager;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloManageController"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="appHost">The application host.</param>
    /// <param name="appPaths">The application paths.</param>
    /// <param name="systemManager">The system manager.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="transcodeManager">The transcode manager.</param>
    /// <param name="activityManager">The activity manager.</param>
    /// <param name="userManager">The user manager.</param>
    public KhyloManageController(
        ILogger<KhyloManageController> logger,
        IServerApplicationHost appHost,
        IApplicationPaths appPaths,
        ISystemManager systemManager,
        ISessionManager sessionManager,
        ITranscodeManager transcodeManager,
        IActivityManager activityManager,
        IUserManager userManager)
    {
        _logger = logger;
        _appHost = appHost;
        _appPaths = appPaths;
        _systemManager = systemManager;
        _sessionManager = sessionManager;
        _transcodeManager = transcodeManager;
        _activityManager = activityManager;
        _userManager = userManager;
    }

    /// <summary>
    /// Gets the computer Khylo runs on, not just the Khylo process.
    /// </summary>
    /// <response code="200">The machine.</response>
    /// <returns>Its name, load, memory, uptime and what Manager may do to it.</returns>
    [HttpGet("Machine")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ManageMachine> GetMachine()
    {
        var process = Process.GetCurrentProcess();
        var (used, total) = MachineStats.Memory();
        var cpu = MachineStats.CpuPercent();
        if (cpu is null)
        {
            // This system can't say how busy it is overall; Khylo's own use is the best answer
            var (percent, sample) = MachineStats.ProcessCpu(process, _cpuSamples.TryGetValue(process.Id, out var last) ? last : null);
            _cpuSamples[process.Id] = sample;
            cpu = percent;
        }

        return new ManageMachine
        {
            MachineName = Environment.MachineName,
            OperatingSystem = MachineStats.OperatingSystemName(),
            Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            CpuCores = Environment.ProcessorCount,
            CpuPercent = cpu.Value,
            MemoryUsedBytes = used,
            MemoryTotalBytes = total,
            MachineUptimeSeconds = Math.Round(MachineStats.Uptime().TotalSeconds),
            ServerUptimeSeconds = Math.Round(Math.Max(0, (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds)),
            ServerVersion = _appHost.ApplicationVersionString,
            CanRestartServer = true,
            CanRebootMachine = CanRebootMachine()
        };
    }

    /// <summary>
    /// Gets Khylo's own processes: the server and the helpers it started (transcodes). Never the computer's other processes.
    /// </summary>
    /// <response code="200">The processes.</response>
    /// <returns>Each process with what it's doing.</returns>
    [HttpGet("Processes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ManageProcess>> GetProcesses()
    {
        var list = new List<ManageProcess>();
        var self = Process.GetCurrentProcess();
        var (selfCpu, selfSample) = MachineStats.ProcessCpu(self, _cpuSamples.TryGetValue(self.Id, out var lastSelf) ? lastSelf : null);
        _cpuSamples[self.Id] = selfSample;
        list.Add(new ManageProcess
        {
            Id = "server",
            Kind = "server",
            Name = "Khylo Server",
            Detail = "Serving " + _sessionManager.Sessions.Count(s => s.NowPlayingItem is not null) + " stream(s)",
            Pid = self.Id,
            CpuPercent = selfCpu,
            MemoryBytes = self.WorkingSet64,
            StartedAt = self.StartTime.ToUniversalTime(),
            CanStop = false
        });

        var sessions = _sessionManager.Sessions.ToList();
        var live = new HashSet<int>();
        foreach (var job in _transcodeManager.GetActiveJobs())
        {
            var process = job.Process;
            if (process is null || job.HasExited || string.IsNullOrEmpty(job.Id))
            {
                continue;
            }

            try
            {
                if (process.HasExited)
                {
                    continue;
                }

                var session = sessions.FirstOrDefault(s => string.Equals(s.DeviceId, job.DeviceId, StringComparison.OrdinalIgnoreCase));
                var info = session?.TranscodingInfo;
                var size = info?.Width is { } w && info.Height is { } h ? $", {w}x{h}" : string.Empty;
                var who = session is null ? string.Empty : $", for {session.UserName} on {session.DeviceName}";
                var (percent, sample) = MachineStats.ProcessCpu(process, _cpuSamples.TryGetValue(process.Id, out var last) ? last : null);
                _cpuSamples[process.Id] = sample;
                live.Add(process.Id);
                list.Add(new ManageProcess
                {
                    Id = "transcode-" + job.Id,
                    Kind = "transcode",
                    Name = "Transcode",
                    Detail = (job.MediaSource?.Name ?? "A video") + size + who,
                    Pid = process.Id,
                    CpuPercent = percent,
                    MemoryBytes = process.WorkingSet64,
                    StartedAt = process.StartTime.ToUniversalTime(),
                    CanStop = true
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // It ended while we looked
            }
        }

        foreach (var gone in _cpuSamples.Keys.Where(pid => pid != self.Id && !live.Contains(pid)).ToList())
        {
            _cpuSamples.TryRemove(gone, out _);
        }

        return list;
    }

    /// <summary>
    /// Stops one listed helper process (a transcode). Only ids from the list work.
    /// </summary>
    /// <param name="id">The process's id from the list.</param>
    /// <response code="204">Stopped.</response>
    /// <response code="404">Not there any more.</response>
    /// <response code="409">Can't be stopped (the server itself: use Restart).</response>
    /// <returns>No content.</returns>
    [HttpPost("Processes/{id}/Stop")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> StopProcess([FromRoute, Required] string id)
    {
        if (string.Equals(id, "server", StringComparison.Ordinal))
        {
            return Conflict();
        }

        const string Prefix = "transcode-";
        if (!id.StartsWith(Prefix, StringComparison.Ordinal) || !await _transcodeManager.StopJobAsync(id[Prefix.Length..]).ConfigureAwait(false))
        {
            return NotFound();
        }

        await LogAsync("stopped a transcode").ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Restarts the Khylo server, recording who did it.
    /// </summary>
    /// <response code="204">Restarting.</response>
    /// <returns>No content.</returns>
    [HttpPost("Restart")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Restart()
    {
        await LogAsync("restarted the server").ConfigureAwait(false);
        _systemManager.Restart();
        return NoContent();
    }

    /// <summary>
    /// Step one of a reboot: a one-use code, tied to this admin, that lasts a minute.
    /// </summary>
    /// <response code="200">The code.</response>
    /// <response code="429">Asked too often.</response>
    /// <response code="501">This machine can't be rebooted from here.</response>
    /// <returns>The code and how long it lasts.</returns>
    [HttpPost("Reboot/Prepare")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<ActionResult<RebootPrepared>> PrepareReboot()
    {
        if (!CanRebootMachine())
        {
            return StatusCode(StatusCodes.Status501NotImplemented);
        }

        var userId = User.GetUserId();
        string code;
        lock (_rebootLock)
        {
            if (TooManyAttempts("prepare:" + userId, out var retryAfter))
            {
                Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return StatusCode(StatusCodes.Status429TooManyRequests);
            }

            RecordAttempt("prepare:" + userId);
            code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
            _rebootCodes[userId] = (code, DateTime.UtcNow + _codeLifetime);
        }

        await LogAsync("asked to reboot the machine (waiting for confirmation)").ConfigureAwait(false);
        return new RebootPrepared { Code = code, ExpiresInSeconds = (int)_codeLifetime.TotalSeconds };
    }

    /// <summary>
    /// Step two of a reboot: with the code from step one, restarts the machine about ten seconds later.
    /// </summary>
    /// <param name="request">The code.</param>
    /// <response code="204">The reboot is scheduled.</response>
    /// <response code="403">The code is wrong, used or expired.</response>
    /// <response code="429">Too many tries.</response>
    /// <response code="501">This machine can't be rebooted from here.</response>
    /// <returns>No content.</returns>
    [HttpPost("Reboot")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<ActionResult> Reboot([FromBody, Required] RebootRequest request)
    {
        if (!CanRebootMachine())
        {
            return StatusCode(StatusCodes.Status501NotImplemented);
        }

        var userId = User.GetUserId();
        var key = "reboot:" + userId;
        lock (_rebootLock)
        {
            if (TooManyAttempts(key, out var retryAfter))
            {
                Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return StatusCode(StatusCodes.Status429TooManyRequests);
            }

            // The code is spent by any try, right or wrong
            var valid = _rebootCodes.Remove(userId, out var stored)
                && stored.Expires > DateTime.UtcNow
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stored.Code), Encoding.UTF8.GetBytes(request.Code ?? string.Empty));
            if (!valid)
            {
                RecordAttempt(key);
                return Forbid();
            }
        }

        await LogAsync("rebooted the machine").ConfigureAwait(false);
        ScheduleReboot();
        return NoContent();
    }

    /// <summary>
    /// Follows a log live: whole lines from a point on. Ask again with the last NextByte.
    /// </summary>
    /// <param name="name">The log's name, from the list of logs.</param>
    /// <param name="fromByte">Where to start; left out, it's the last 64 KB.</param>
    /// <param name="maxBytes">The most to send (default 128 KB, capped at 1 MB).</param>
    /// <response code="200">The new lines.</response>
    /// <response code="404">No such log.</response>
    /// <returns>The text, where to ask next, and whether the log started over.</returns>
    [HttpGet("Logs/Tail")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<LogTail> TailLog([FromQuery, Required] string name, [FromQuery] long? fromByte, [FromQuery] int? maxBytes)
    {
        // Only a plain file name inside the log folder, found by listing it: nothing the caller types is used as a path
        var file = new DirectoryInfo(_appPaths.LogDirectoryPath).EnumerateFiles()
            .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            return NotFound("Log file not found.");
        }

        var limit = Math.Clamp(maxBytes ?? TailDefaultBytes, 1024, TailMaxBytes);
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var size = stream.Length;
        var reset = fromByte is { } asked && (asked > size || asked < 0);
        var fromEnd = fromByte is null || reset;
        var start = fromEnd ? Math.Max(0, size - Math.Min(limit, TailFirstBytes)) : fromByte!.Value;

        var length = (int)Math.Min(limit, size - start);
        var buffer = new byte[Math.Max(0, length)];
        stream.Seek(start, SeekOrigin.Begin);
        var read = stream.Read(buffer, 0, buffer.Length);

        var from = 0;
        if (fromEnd && start > 0)
        {
            // Began in the middle of a line: skip to the next one
            var firstBreak = Array.IndexOf(buffer, (byte)'\n', 0, read);
            from = firstBreak < 0 ? read : firstBreak + 1;
        }

        var lastBreak = Array.LastIndexOf(buffer, (byte)'\n', Math.Max(0, read - 1));
        var end = lastBreak >= from && read > 0 ? lastBreak + 1 : (read == limit ? read : from);
        if (end < from)
        {
            end = from;
        }

        return new LogTail
        {
            Text = Encoding.UTF8.GetString(buffer, from, end - from),
            NextByte = start + end,
            Size = size,
            Reset = reset
        };
    }

    private bool CanRebootMachine()
    {
        if (MachineStats.InContainer())
        {
            return false;
        }

        if (OperatingSystem.IsWindows() && System.Security.Principal.WindowsIdentity.GetCurrent().IsSystem)
        {
            return true;
        }

        return Environment.IsPrivilegedProcess;
    }

    private void ScheduleReboot()
    {
        _logger.LogWarning("Rebooting this machine in about 10 seconds, as an administrator asked");
        if (OperatingSystem.IsWindows())
        {
            // Windows counts the ten seconds itself
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 10") { CreateNoWindow = true, UseShellExecute = false });
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Process.Start(new ProcessStartInfo("shutdown", "-r now") { CreateNoWindow = true, UseShellExecute = false });
        });
    }

    private bool TooManyAttempts(string key, out int retryAfterSeconds)
    {
        retryAfterSeconds = 60;
        if (!_rebootAttempts.TryGetValue(key, out var times))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        times.RemoveAll(t => t < now.AddMinutes(-1));
        if (times.Count < MaxRebootAttemptsPerMinute)
        {
            return false;
        }

        retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((times.Min().AddMinutes(1) - now).TotalSeconds));
        return true;
    }

    private void RecordAttempt(string key)
    {
        if (!_rebootAttempts.TryGetValue(key, out var times))
        {
            _rebootAttempts[key] = times = [];
        }

        times.Add(DateTime.UtcNow);
    }

    private async Task LogAsync(string what)
    {
        var userId = User.GetUserId();
        var name = _userManager.GetUserById(userId)?.Username ?? "An administrator";
        await _activityManager.CreateAsync(new ActivityLog($"Khylo Manager: {name} {what}", "KhyloManager", userId)
        {
            ShortOverview = "IP address: " + HttpContext.GetNormalizedRemoteIP()
        }).ConfigureAwait(false);
    }

    /// <summary>The computer Khylo runs on.</summary>
    public class ManageMachine
    {
        /// <summary>Gets or sets the computer's name.</summary>
        public string MachineName { get; set; } = string.Empty;

        /// <summary>Gets or sets the operating system.</summary>
        public string OperatingSystem { get; set; } = string.Empty;

        /// <summary>Gets or sets the processor family.</summary>
        public string Architecture { get; set; } = string.Empty;

        /// <summary>Gets or sets the number of processor cores.</summary>
        public int CpuCores { get; set; }

        /// <summary>Gets or sets how busy the whole computer is, 0 to 100.</summary>
        public double CpuPercent { get; set; }

        /// <summary>Gets or sets the memory in use, when the system can say.</summary>
        public long? MemoryUsedBytes { get; set; }

        /// <summary>Gets or sets the memory the computer has.</summary>
        public long? MemoryTotalBytes { get; set; }

        /// <summary>Gets or sets how long the computer has been on.</summary>
        public double MachineUptimeSeconds { get; set; }

        /// <summary>Gets or sets how long Khylo has been running.</summary>
        public double ServerUptimeSeconds { get; set; }

        /// <summary>Gets or sets Khylo's version.</summary>
        public string ServerVersion { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether Khylo can be restarted.</summary>
        public bool CanRestartServer { get; set; }

        /// <summary>Gets or sets a value indicating whether a reboot would really work.</summary>
        public bool CanRebootMachine { get; set; }
    }

    /// <summary>One of Khylo's own processes.</summary>
    public class ManageProcess
    {
        /// <summary>Gets or sets an id the server made up and can map back.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Gets or sets the kind: server or transcode.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Gets or sets the name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets what it's doing.</summary>
        public string? Detail { get; set; }

        /// <summary>Gets or sets the operating system's process number.</summary>
        public int Pid { get; set; }

        /// <summary>Gets or sets its processor use as a share of the whole computer.</summary>
        public double CpuPercent { get; set; }

        /// <summary>Gets or sets its memory in bytes.</summary>
        public long MemoryBytes { get; set; }

        /// <summary>Gets or sets when it started.</summary>
        public DateTime StartedAt { get; set; }

        /// <summary>Gets or sets a value indicating whether it can be stopped.</summary>
        public bool CanStop { get; set; }
    }

    /// <summary>The one-use reboot code.</summary>
    public class RebootPrepared
    {
        /// <summary>Gets or sets the code.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Gets or sets how long it works.</summary>
        public int ExpiresInSeconds { get; set; }
    }

    /// <summary>The code that confirms a reboot.</summary>
    public class RebootRequest
    {
        /// <summary>Gets or sets the code from the first step.</summary>
        public string? Code { get; set; }
    }

    /// <summary>A piece of a log.</summary>
    public class LogTail
    {
        /// <summary>Gets or sets the new whole lines.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Gets or sets where to ask next.</summary>
        public long NextByte { get; set; }

        /// <summary>Gets or sets the log's size now.</summary>
        public long Size { get; set; }

        /// <summary>Gets or sets a value indicating whether the log was replaced or shrank, so this is its newest part.</summary>
        public bool Reset { get; set; }
    }
}
