using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Khylo.Api.Models;
using Khylo.Common.Api;
using Khylo.Common.Configuration;
using Khylo.Controller.SystemBackupService;
using Khylo.Server.Implementations.SystemBackupService;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Khylo.Api.Controllers;

/// <summary>
/// The backup controller.
/// </summary>
[Authorize(Policy = Policies.RequiresElevation)]
public class BackupController : BaseKhyloApiController
{
    private static readonly object _runLock = new();
    private static bool _running;
    private static DateTime _startedAt;
    private static string? _lastError;

    private readonly IBackupService _backupService;
    private readonly IApplicationPaths _applicationPaths;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackupController"/> class.
    /// </summary>
    /// <param name="backupService">Instance of the <see cref="IBackupService"/> interface.</param>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    public BackupController(IBackupService backupService, IApplicationPaths applicationPaths)
    {
        _backupService = backupService;
        _applicationPaths = applicationPaths;
    }

    /// <summary>
    /// Starts a backup and answers straight away, for backups that take minutes (a big library's artwork can be many
    /// gigabytes). Ask <c>Backup/Status</c> how it's going.
    /// </summary>
    /// <param name="backupOptions">The backup options.</param>
    /// <response code="202">The backup has started.</response>
    /// <response code="409">A backup is already running.</response>
    /// <returns>Accepted.</returns>
    [HttpPost("CreateInBackground")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult CreateBackupInBackground([FromBody] BackupOptionsDto backupOptions)
    {
        lock (_runLock)
        {
            if (_running)
            {
                return Conflict();
            }

            _running = true;
            _startedAt = DateTime.UtcNow;
            _lastError = null;
        }

        var options = backupOptions ?? new();
        _ = Task.Run(async () =>
        {
            try
            {
                await _backupService.CreateBackupAsync(options).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (_runLock)
                {
                    _lastError = ex.Message;
                }
            }
            finally
            {
                lock (_runLock)
                {
                    _running = false;
                }
            }
        });
        return Accepted();
    }

    /// <summary>
    /// Says whether a backup is running, how big it is so far, and how the last one ended.
    /// </summary>
    /// <response code="200">The state.</response>
    /// <returns>The state of the backup.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<BackupRunStatus> GetBackupStatus()
    {
        bool running;
        DateTime started;
        string? error;
        lock (_runLock)
        {
            running = _running;
            started = _startedAt;
            error = _lastError;
        }

        long bytes = 0;
        if (running && Directory.Exists(_applicationPaths.BackupPath))
        {
            var newest = new DirectoryInfo(_applicationPaths.BackupPath).EnumerateFiles("*.zip")
                .Where(f => f.LastWriteTimeUtc >= started.AddSeconds(-5))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            bytes = newest?.Length ?? 0;
        }

        return new BackupRunStatus { InProgress = running, StartedAt = running ? started : null, BytesSoFar = bytes, Error = error };
    }

    /// <summary>
    /// Creates a new Backup.
    /// </summary>
    /// <param name="backupOptions">The backup options.</param>
    /// <response code="200">Backup created.</response>
    /// <response code="403">User does not have permission to retrieve information.</response>
    /// <returns>The created backup manifest.</returns>
    [HttpPost("Create")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BackupManifestDto>> CreateBackup([FromBody] BackupOptionsDto backupOptions)
    {
        return Ok(await _backupService.CreateBackupAsync(backupOptions ?? new()).ConfigureAwait(false));
    }

    /// <summary>
    /// Restores to a backup by restarting the server and applying the backup.
    /// </summary>
    /// <param name="archiveRestoreDto">The data to start a restore process.</param>
    /// <response code="204">Backup restore started.</response>
    /// <response code="403">User does not have permission to retrieve information.</response>
    /// <returns>No-Content.</returns>
    [HttpPost("Restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult StartRestoreBackup([FromBody, BindRequired] BackupRestoreRequestDto archiveRestoreDto)
    {
        var archivePath = SanitizePath(archiveRestoreDto.ArchiveFileName);
        if (!System.IO.File.Exists(archivePath))
        {
            return NotFound();
        }

        _backupService.ScheduleRestoreAndRestartServer(archivePath);
        return NoContent();
    }

    /// <summary>
    /// Gets a list of all currently present backups in the backup directory.
    /// </summary>
    /// <response code="200">Backups available.</response>
    /// <response code="403">User does not have permission to retrieve information.</response>
    /// <returns>The list of backups.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BackupManifestDto[]>> ListBackups()
    {
        return Ok(await _backupService.EnumerateBackups().ConfigureAwait(false));
    }

    /// <summary>
    /// Gets the descriptor from an existing archive is present.
    /// </summary>
    /// <param name="path">The data to start a restore process.</param>
    /// <response code="200">Backup archive manifest.</response>
    /// <response code="204">Not a valid khylo Archive.</response>
    /// <response code="404">Not a valid path.</response>
    /// <response code="403">User does not have permission to retrieve information.</response>
    /// <returns>The backup manifest.</returns>
    [HttpGet("Manifest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BackupManifestDto>> GetBackup([BindRequired] string path)
    {
        var backupPath = SanitizePath(path);

        if (!System.IO.File.Exists(backupPath))
        {
            return NotFound();
        }

        var manifest = await _backupService.GetBackupManifest(backupPath).ConfigureAwait(false);
        if (manifest is null)
        {
            return NoContent();
        }

        return Ok(manifest);
    }

    [NonAction]
    private string SanitizePath(string path)
    {
        // sanitize path
        var archiveRestorePath = Path.GetFileName(Path.GetFullPath(path));
        var archivePath = Path.Combine(_applicationPaths.BackupPath, archiveRestorePath);
        return archivePath;
    }
}
