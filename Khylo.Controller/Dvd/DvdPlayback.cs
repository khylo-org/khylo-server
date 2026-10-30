using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Khylo.Model.Dto;
using Khylo.Model.Entities;
using Khylo.Model.MediaInfo;

namespace Khylo.Controller.Dvd;

/// <summary>Turns a DVD media source into what ffmpeg needs to play it.</summary>
public static class DvdPlayback
{
    private const string MenuSuffix = "-menu";

    /// <summary>Gets the media source id for one of a disc's titles.</summary>
    /// <param name="itemId">The item's id.</param>
    /// <param name="titleNumber">The title number.</param>
    /// <returns>The id.</returns>
    public static string TitleSourceId(Guid itemId, int titleNumber)
        => string.Create(CultureInfo.InvariantCulture, $"{itemId:N}-t{titleNumber}");

    /// <summary>Gets the media source id for a disc's menu picture.</summary>
    /// <param name="itemId">The item's id.</param>
    /// <returns>The id.</returns>
    public static string MenuSourceId(Guid itemId) => itemId.ToString("N", CultureInfo.InvariantCulture) + MenuSuffix;

    /// <summary>Whether a video is a DVD (an ISO image or a VIDEO_TS folder).</summary>
    /// <param name="videoType">The video type.</param>
    /// <param name="isoType">The disc type, for ISO images.</param>
    /// <returns>True for DVDs.</returns>
    public static bool IsDvd(VideoType? videoType, IsoType? isoType)
        => videoType == VideoType.Dvd || (videoType == VideoType.Iso && isoType == IsoType.Dvd);

    /// <summary>Whether a media source is a DVD we can read.</summary>
    /// <param name="source">The media source.</param>
    /// <returns>True when it is.</returns>
    public static bool IsDisc(MediaSourceInfo? source)
        => source is not null && source.Protocol == MediaProtocol.File && IsDvd(source.VideoType, source.IsoType);

    /// <summary>The title a media source id asks for (a "-t2" ending), or null for the main feature.</summary>
    /// <param name="mediaSourceId">The media source id.</param>
    /// <returns>The title number, or null.</returns>
    public static int? ParseTitle(string? mediaSourceId)
    {
        if (string.IsNullOrEmpty(mediaSourceId))
        {
            return null;
        }

        var dash = mediaSourceId.LastIndexOf("-t", StringComparison.Ordinal);
        return dash > 0 && int.TryParse(mediaSourceId.AsSpan(dash + 2), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>Whether a media source id is the disc's menu picture.</summary>
    /// <param name="mediaSourceId">The media source id.</param>
    /// <returns>True for the menu.</returns>
    public static bool IsMenu(string? mediaSourceId)
        => mediaSourceId is not null && mediaSourceId.EndsWith(MenuSuffix, StringComparison.Ordinal);

    /// <summary>Gets the title a media source plays.</summary>
    /// <param name="disc">The disc.</param>
    /// <param name="mediaSourceId">The media source id.</param>
    /// <returns>The title, or null.</returns>
    public static DvdTitle? GetTitle(DvdDisc disc, string? mediaSourceId)
    {
        var number = ParseTitle(mediaSourceId);
        return number is null ? disc.MainTitle : disc.Titles.FirstOrDefault(t => t.Number == number);
    }

    /// <summary>Gets the pieces ffmpeg should read for a media source.</summary>
    /// <param name="source">The media source.</param>
    /// <param name="path">The disc's path, for callers whose media source doesn't carry it.</param>
    /// <returns>The pieces in playing order, or null when this isn't a readable DVD.</returns>
    public static IReadOnlyList<DvdSegment>? GetSegments(MediaSourceInfo? source, string? path = null)
    {
        var discPath = string.IsNullOrEmpty(source?.Path) ? path : source!.Path;
        if (!IsDisc(source) || string.IsNullOrEmpty(discPath))
        {
            return null;
        }

        var disc = DvdDisc.TryOpen(discPath);
        if (disc is null || disc.IsScrambled)
        {
            return null;
        }

        if (IsMenu(source!.Id))
        {
            var menu = disc.GetMenuSegment();
            return menu is null ? null : [menu];
        }

        var title = GetTitle(disc, source!.Id);
        return title is null || title.Segments.Count == 0 ? null : title.Segments;
    }

    /// <summary>Fills in what a DVD's own index knows better than ffmpeg: the true length, chapters, languages.</summary>
    /// <param name="info">What ffmpeg found.</param>
    /// <param name="title">The disc title being played.</param>
    public static void ApplyTitle(MediaInfo info, DvdTitle title)
    {
        info.RunTimeTicks = TimeSpan.FromSeconds(title.Seconds).Ticks;

        var bytes = title.Segments.Sum(x => x.Length);
        if (title.Seconds > 0 && bytes > 0)
        {
            info.Bitrate = (int)Math.Min(int.MaxValue, bytes * 8 / title.Seconds);
            info.Size = bytes;
        }

        info.Chapters = title.Chapters
            .Select(c => new ChapterInfo { StartPositionTicks = TimeSpan.FromSeconds(c.StartSeconds).Ticks })
            .ToArray();

        // ffmpeg lists a title's audio and subtitle tracks in the order the disc numbers them
        var audio = info.MediaStreams.Where(x => x.Type == MediaStreamType.Audio).OrderBy(x => x.Index).ToList();
        if (audio.Count == title.Audio.Count || audio.Count == 1)
        {
            for (var i = 0; i < audio.Count && i < title.Audio.Count; i++)
            {
                audio[i].Language ??= title.Audio[i].Language;
                if (title.Audio[i].Note is not null)
                {
                    audio[i].Title ??= title.Audio[i].Note;
                }
            }
        }

        var subs = info.MediaStreams.Where(x => x.Type == MediaStreamType.Subtitle).OrderBy(x => x.Index).ToList();
        if (subs.Count == title.Subtitles.Count)
        {
            for (var i = 0; i < subs.Count; i++)
            {
                subs[i].Language ??= title.Subtitles[i].Language;
                if (title.Subtitles[i].Note is not null)
                {
                    subs[i].Title ??= title.Subtitles[i].Note;
                }

                subs[i].IsForced = string.Equals(title.Subtitles[i].Note, "Forced", StringComparison.Ordinal);
            }
        }
    }

    /// <summary>A name that changes whenever the disc or the title changes, for caching generated files.</summary>
    /// <param name="source">The media source.</param>
    /// <returns>The key.</returns>
    public static string CacheKey(MediaSourceInfo source)
    {
        long stamp = 0;
        try
        {
            stamp = Directory.Exists(source.Path)
                ? Directory.GetLastWriteTimeUtc(source.Path).Ticks
                : new FileInfo(source.Path).Length ^ File.GetLastWriteTimeUtc(source.Path).Ticks;
        }
        catch (Exception)
        {
            // no stamp
        }

        return string.Create(CultureInfo.InvariantCulture, $"{source.Id}_{stamp & 0xFFFFFFFFFFL:x}");
    }
}
