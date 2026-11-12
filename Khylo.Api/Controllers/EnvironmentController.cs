using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Khylo.Api.Models.EnvironmentDtos;
using Khylo.Common.Api;
using Khylo.Common.Configuration;
using Khylo.Common.Extensions;
using Khylo.Controller.IO;
using Khylo.Model.IO;
using Khylo.Model.System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Khylo.Api.Controllers;

/// <summary>
/// Environment Controller.
/// </summary>
[Authorize(Policy = Policies.FirstTimeSetupOrElevated)]
public class EnvironmentController : BaseKhyloApiController
{
    private const char UncSeparator = '\\';
    private const string UncStartPrefix = @"\\";

    private readonly IFileSystem _fileSystem;
    private readonly ILogger<EnvironmentController> _logger;
    private readonly IApplicationPaths _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnvironmentController"/> class.
    /// </summary>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{EnvironmentController}"/> interface.</param>
    /// <param name="paths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    public EnvironmentController(IFileSystem fileSystem, ILogger<EnvironmentController> logger, IApplicationPaths paths)
    {
        _fileSystem = fileSystem;
        _logger = logger;
        _paths = paths;
    }

    /// <summary>
    /// Gets the contents of a given directory in the file system.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="includeFiles">An optional filter to include or exclude files from the results. true/false.</param>
    /// <param name="includeDirectories">An optional filter to include or exclude folders from the results. true/false.</param>
    /// <response code="200">Directory contents returned.</response>
    /// <returns>Directory contents.</returns>
    [HttpGet("DirectoryContents")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IEnumerable<FileSystemEntryInfo> GetDirectoryContents(
        [FromQuery, Required] string path,
        [FromQuery] bool includeFiles = false,
        [FromQuery] bool includeDirectories = false)
    {
        if (path.StartsWith(UncStartPrefix, StringComparison.OrdinalIgnoreCase)
            && path.LastIndexOf(UncSeparator) == 1)
        {
            return Array.Empty<FileSystemEntryInfo>();
        }

        var entries =
            _fileSystem.GetFileSystemEntries(path)
                .Where(i => (i.IsDirectory && includeDirectories) || (!i.IsDirectory && includeFiles))
                .OrderBy(i => i.FullName);

        return entries.Select(f => new FileSystemEntryInfo(f.Name, f.FullName, f.IsDirectory ? FileSystemEntryType.Directory : FileSystemEntryType.File));
    }

    /// <summary>
    /// Says whether Khylo can open a folder, and when it can't, whether Khylo can be given access from here.
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <param name="checkChanges">Whether to also find out if Khylo can add files there (this briefly adds one, so only on request).</param>
    /// <response code="200">The answer.</response>
    /// <returns>Whether the folder can be opened, as which Windows account Khylo runs, and whether access can be given.</returns>
    [HttpGet("FolderAccess")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<FolderAccessDto> GetFolderAccess([FromQuery, Required] string path, [FromQuery] bool checkChanges = false)
    {
        var result = new FolderAccessDto { CanOpen = KhyloFolderAccess.CanOpen(path), Account = KhyloFolderAccess.RunsAs() };
        if (!result.CanOpen)
        {
            result.HowToFix = KhyloFolderAccess.HowToFix(KhyloFolderAccess.HowKhyloRuns(), path, result.Account);
            _logger.LogWarning("Khylo, running as {Account}, isn't allowed to open {Path}", result.Account, path);
        }

        if (OperatingSystem.IsWindows())
        {
            result.CanChange = checkChanges && result.CanOpen && Directory.Exists(path) && KhyloFolderAccess.CanChange(path);
            if (KhyloFolderAccess.HelperAvailable())
            {
                result.WhyNotGive = KhyloFolderAccess.WhyNot(path, KhyloFolderAccess.PlacesOnThisPc());
                result.CanGive = result.WhyNotGive is null;
            }
        }

        return result;
    }

    /// <summary>
    /// Asks for Khylo to be given access to a folder on this PC. The part of Khylo that runs as the system does it, after
    /// checking the folder itself, usually within a minute.
    /// </summary>
    /// <param name="request">The folder, and whether Khylo may change what is in it.</param>
    /// <response code="204">Asked.</response>
    /// <response code="400">Khylo can't be given access to that folder this way.</response>
    /// <returns>Nothing.</returns>
    [HttpPost("FolderAccess")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult GiveFolderAccess([FromBody, Required] GiveFolderAccessDto request)
    {
        if (!KhyloFolderAccess.HelperAvailable())
        {
            return BadRequest("Khylo can't change who may open folders on this server. Give the account Khylo runs as access to the folder in Windows.");
        }

        if (KhyloFolderAccess.WhyNot(request.Path, KhyloFolderAccess.PlacesOnThisPc()) is { } problem)
        {
            return BadRequest(problem);
        }

        _logger.LogInformation("Asked for access to {Path} (changes: {AllowChanges})", request.Path, request.AllowChanges);
        KhyloFolderAccess.Ask(_paths.CachePath, request.Path!, request.AllowChanges);
        return NoContent();
    }

    /// <summary>
    /// Validates path.
    /// </summary>
    /// <param name="validatePathDto">Validate request object.</param>
    /// <response code="204">Path validated.</response>
    /// <response code="404">Path not found.</response>
    /// <returns>Validation status.</returns>
    [HttpPost("ValidatePath")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult ValidatePath([FromBody, Required] ValidatePathDto validatePathDto)
    {
        if (validatePathDto.IsFile.HasValue)
        {
            if (validatePathDto.IsFile.Value)
            {
                if (!System.IO.File.Exists(validatePathDto.Path))
                {
                    return NotFound();
                }
            }
            else
            {
                if (!Directory.Exists(validatePathDto.Path))
                {
                    return NotFound();
                }
            }
        }
        else
        {
            if (!System.IO.File.Exists(validatePathDto.Path) && !Directory.Exists(validatePathDto.Path))
            {
                return NotFound();
            }

            if (validatePathDto.ValidateWritable)
            {
                if (validatePathDto.Path is null)
                {
                    throw new ResourceNotFoundException(nameof(validatePathDto.Path));
                }

                var file = Path.Combine(validatePathDto.Path, Guid.NewGuid().ToString());
                try
                {
                    System.IO.File.WriteAllText(file, string.Empty);
                }
                finally
                {
                    if (System.IO.File.Exists(file))
                    {
                        System.IO.File.Delete(file);
                    }
                }
            }
        }

        return NoContent();
    }

    /// <summary>
    /// Gets available drives from the server's file system.
    /// </summary>
    /// <response code="200">List of entries returned.</response>
    /// <returns>List of entries.</returns>
    [HttpGet("Drives")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IEnumerable<FileSystemEntryInfo> GetDrives()
    {
        return _fileSystem.GetDrives().Select(d => new FileSystemEntryInfo(d.Name, d.FullName, FileSystemEntryType.Directory));
    }

    /// <summary>
    /// Gets detailed storage device information including capacity, utilization, and low-space warnings.
    /// </summary>
    /// <response code="200">List of storage devices returned.</response>
    /// <returns>List of storage device information.</returns>
    [HttpGet("StorageDevices")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IEnumerable<StorageDeviceInfoDto> GetStorageDevices()
    {
        var result = new List<StorageDeviceInfoDto>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                var total = drive.TotalSize;
                var free = drive.AvailableFreeSpace;
                var used = Math.Max(0, total - free);
                var percentUsed = total > 0 ? Math.Round((double)used / total * 100.0, 2) : 0.0;
                var freePercent = total > 0 ? ((double)free / total) * 100.0 : 100.0;

                result.Add(new StorageDeviceInfoDto
                {
                    Name = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : drive.VolumeLabel,
                    Path = drive.RootDirectory.FullName,
                    FileSystem = drive.DriveFormat,
                    TotalSpace = total,
                    FreeSpace = free,
                    UsedSpace = used,
                    PercentUsed = percentUsed,
                    IsLowSpaceWarning = freePercent < 15.0,
                    IsCriticalSpaceWarning = freePercent < 5.0
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error reading storage metrics for drive {DriveName}", drive.Name);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the parent path of a given path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>Parent path.</returns>
    [HttpGet("ParentPath")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<string?> GetParentPath([FromQuery, Required] string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent))
        {
            // Check if unc share
            var index = path.LastIndexOf(UncSeparator);

            if (index != -1 && path[0] == UncSeparator)
            {
                parent = path.Substring(0, index);

                if (string.IsNullOrWhiteSpace(parent.TrimStart(UncSeparator)))
                {
                    parent = null;
                }
            }
        }

        return parent;
    }

    /// <summary>
    /// Get Default directory browser.
    /// </summary>
    /// <response code="200">Default directory browser returned.</response>
    /// <returns>Default directory browser.</returns>
    [HttpGet("DefaultDirectoryBrowser")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<DefaultDirectoryBrowserInfoDto> GetDefaultDirectoryBrowser()
    {
        return new DefaultDirectoryBrowserInfoDto();
    }
}
