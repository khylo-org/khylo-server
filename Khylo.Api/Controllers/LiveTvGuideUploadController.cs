using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Api.Models.LiveTvDtos;
using Khylo.Common.Api;
using Khylo.Common.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// Lets someone upload a program guide (XMLTV) file from their own computer, for providers that give a file instead of a
/// link. The file is kept on the server, and its path is what the guide source then points at. Used by Live TV in the
/// dashboard and in first-time setup.
/// </summary>
[Route("LiveTv/ListingProviders/Xmltv")]
[Authorize(Policy = Policies.FirstTimeSetupOrElevated)]
public class LiveTvGuideUploadController : BaseKhyloApiController
{
    // Guides for a big IPTV lineup run to a few hundred megabytes
    private const long MaxGuideBytes = 512L * 1024 * 1024;

    private static readonly string[] _extensions = [".xml", ".xml.gz", ".gz", ".xmltv"];

    private readonly IApplicationPaths _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveTvGuideUploadController"/> class.
    /// </summary>
    /// <param name="paths">The application paths.</param>
    public LiveTvGuideUploadController(IApplicationPaths paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// Saves an uploaded guide file on the server.
    /// </summary>
    /// <param name="file">The guide file (.xml or .xml.gz).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The file is saved; its path is returned.</response>
    /// <response code="400">No file, or not a guide file.</response>
    /// <returns>Where the file was saved.</returns>
    [HttpPost("Upload")]
    [RequestSizeLimit(MaxGuideBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxGuideBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GuideUploadResult>> UploadGuide([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("Choose a guide file.");
        }

        var original = System.IO.Path.GetFileName(file.FileName);
        var extension = _extensions.FirstOrDefault(e => original.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        if (extension is null)
        {
            return BadRequest("That doesn't look like a guide file. Use the .xml or .xml.gz file from your provider.");
        }

        var folder = System.IO.Path.Combine(_paths.DataPath, "livetv", "guides");
        Directory.CreateDirectory(folder);

        var path = System.IO.Path.Combine(folder, $"{SafeName(original[..^extension.Length])}-{Guid.NewGuid().ToString("N")[..8]}{extension.ToLowerInvariant()}");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            await file.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        return new GuideUploadResult { Path = path };
    }

    // Letters, digits, dots and dashes only: the name becomes part of a path
    private static string SafeName(string name)
    {
        var safe = new StringBuilder();
        foreach (var c in name)
        {
            safe.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-');
        }

        var result = safe.ToString().Trim('-');
        return result.Length == 0 ? "guide" : result[..Math.Min(result.Length, 40)];
    }
}
