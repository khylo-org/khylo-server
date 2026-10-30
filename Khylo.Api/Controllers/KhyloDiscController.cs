using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Api.Extensions;
using Khylo.Common.Configuration;
using Khylo.Controller.Dvd;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Controller.MediaEncoding;
using Khylo.Model.Dto;
using Khylo.Model.Entities;
using Khylo.Model.MediaInfo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// A DVD's own menu, read from the disc: its titles, chapters, languages and the picture from its menu. Every app
/// builds the same Play / Scenes / Audio and Subtitles / Extras screen from this.
/// </summary>
[Route("Khylo/Disc")]
[Authorize]
public class KhyloDiscController : BaseKhyloApiController
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IApplicationPaths _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloDiscController"/> class.
    /// </summary>
    /// <param name="libraryManager">The library.</param>
    /// <param name="userManager">The users.</param>
    /// <param name="mediaEncoder">The encoder.</param>
    /// <param name="paths">The application paths.</param>
    public KhyloDiscController(ILibraryManager libraryManager, IUserManager userManager, IMediaEncoder mediaEncoder, IApplicationPaths paths)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _mediaEncoder = mediaEncoder;
        _paths = paths;
    }

    /// <summary>
    /// Gets what a disc holds.
    /// </summary>
    /// <param name="itemId">The disc's item.</param>
    /// <response code="200">The disc's menu.</response>
    /// <response code="404">Not a disc, or not found.</response>
    /// <returns>The disc's titles and tracks.</returns>
    [HttpGet("{itemId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<DiscInfo> Get([FromRoute, Required] Guid itemId)
    {
        var video = FindDisc(itemId);
        if (video is null)
        {
            return NotFound();
        }

        var disc = DvdDisc.TryOpen(video.Path);
        if (disc?.MainTitle is null)
        {
            return NotFound();
        }

        var main = disc.MainTitle;
        var info = new DiscInfo
        {
            IsScrambled = disc.IsScrambled,
            HasMenuImage = disc.MenuVobs.Count > 0,
            MainTitle = main.Number,
            Titles = disc.Titles
                .Where(t => t.Number == main.Number || t.Seconds >= 60)
                .Select(t => new DiscTitle
                {
                    Number = t.Number,
                    IsMain = t.Number == main.Number,
                    MediaSourceId = t.Number == main.Number ? video.Id.ToString("N", CultureInfo.InvariantCulture) : DvdPlayback.TitleSourceId(video.Id, t.Number),
                    Seconds = t.Seconds,
                    RunTimeTicks = TimeSpan.FromSeconds(t.Seconds).Ticks,
                    Width = t.Width,
                    Height = t.Height,
                    IsWidescreen = t.IsWidescreen,
                    Chapters = t.Chapters.Select(c => new DiscChapter
                    {
                        Number = c.Number,
                        StartSeconds = c.StartSeconds,
                        StartPositionTicks = TimeSpan.FromSeconds(c.StartSeconds).Ticks
                    }).ToArray(),
                    Audio = t.Audio.Select(a => new DiscAudio { Index = a.Index, Codec = a.Codec, Channels = a.Channels, Language = a.Language, LanguageName = a.LanguageName, Note = a.Note }).ToArray(),
                    Subtitles = t.Subtitles.Select(s => new DiscSubtitle { Index = s.Index, Language = s.Language, LanguageName = s.LanguageName, Note = s.Note }).ToArray()
                })
                .ToArray()
        };

        return info;
    }

    /// <summary>
    /// Gets a picture from the disc: its menu, or a frame of one of its titles.
    /// </summary>
    /// <param name="itemId">The disc's item.</param>
    /// <param name="menu">Take the picture from the disc's menu.</param>
    /// <param name="title">The title (default: the main feature).</param>
    /// <param name="seconds">How far into the title.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <response code="200">A JPEG picture.</response>
    /// <response code="404">Not a disc, or no picture could be made.</response>
    /// <returns>The picture.</returns>
    [HttpGet("{itemId:guid}/Image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetImage(
        [FromRoute, Required] Guid itemId,
        [FromQuery] bool menu = false,
        [FromQuery] int? title = null,
        [FromQuery] double seconds = 10,
        CancellationToken cancellationToken = default)
    {
        var video = FindDisc(itemId);
        if (video is null)
        {
            return NotFound();
        }

        var disc = DvdDisc.TryOpen(video.Path);
        if (disc?.MainTitle is null || disc.IsScrambled)
        {
            return NotFound();
        }

        var source = new MediaSourceInfo
        {
            Id = menu ? DvdPlayback.MenuSourceId(video.Id) : (title is null || title == disc.MainTitle.Number ? video.Id.ToString("N", CultureInfo.InvariantCulture) : DvdPlayback.TitleSourceId(video.Id, title.Value)),
            Path = video.Path,
            Protocol = MediaProtocol.File,
            VideoType = video.VideoType,
            IsoType = video.IsoType
        };

        var chosen = menu ? null : DvdPlayback.GetTitle(disc, source.Id);
        if (!menu && chosen is null)
        {
            return NotFound();
        }

        // Whole seconds are plenty for a thumbnail, and keep the cache from filling with near-duplicates
        var at = menu ? 1 : Math.Clamp(Math.Floor(seconds), 0, Math.Max(0, (chosen?.Seconds ?? 1) - 2));
        var cache = Path.Combine(_paths.CachePath, "khylo-disc", string.Create(CultureInfo.InvariantCulture, $"{DvdPlayback.CacheKey(source)}_{at}.jpg"));
        if (!System.IO.File.Exists(cache))
        {
            try
            {
                var temp = await _mediaEncoder.ExtractVideoImage(video.Path, "mpeg", source, new MediaStream { Type = MediaStreamType.Video }, null, TimeSpan.FromSeconds(at), cancellationToken).ConfigureAwait(false);
                Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                System.IO.File.Copy(temp, cache, true);
                System.IO.File.Delete(temp);
            }
            catch (Exception)
            {
                return NotFound();
            }
        }

        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(cache, "image/jpeg");
    }

    private Video? FindDisc(Guid itemId)
    {
        var user = _userManager.GetUserById(User.GetUserId());
        var item = _libraryManager.GetItemById<BaseItem>(itemId, user);
        return item is Video video && video.IsFileProtocol && DvdPlayback.IsDvd(video.VideoType, video.IsoType) ? video : null;
    }
}
