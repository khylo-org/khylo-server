using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Khylo.Server.Core.Listening;

/// <summary>
/// The rules that turn titles into grouping keys, and that decide what counts as a real play. Statistics group by these keys,
/// so the same song on a single, the album and a deluxe edition counts as one song.
/// </summary>
public static partial class ListeningKeys
{
    /// <summary>Seconds of listening that make a play of a song.</summary>
    public const double PlaySeconds = 30;

    /// <summary>Seconds of watching that make a view of a movie or episode.</summary>
    public const double ViewSeconds = 300;

    /// <summary>
    /// A song key: the normalized title (without "(feat. ...)" and "(Remastered ...)" style suffixes) plus the first artist.
    /// </summary>
    /// <param name="title">The song title.</param>
    /// <param name="artist">The first artist.</param>
    /// <returns>The lowercase key.</returns>
    public static string SongKey(string? title, string? artist)
        => Plain(StripSongNoise(title)) + "|" + Plain(artist);

    /// <summary>
    /// An album key: the normalized album name (without edition words such as Deluxe, Expanded or Remastered) plus the album artist.
    /// </summary>
    /// <param name="album">The album name.</param>
    /// <param name="albumArtist">The album artist.</param>
    /// <returns>The lowercase key.</returns>
    public static string AlbumKey(string? album, string? albumArtist)
        => Plain(StripAlbumNoise(album)) + "|" + Plain(albumArtist);

    /// <summary>
    /// A show key: the normalized series name, without a trailing year, so re-added copies group together.
    /// </summary>
    /// <param name="series">The series name.</param>
    /// <returns>The lowercase key.</returns>
    public static string SeriesKey(string? series)
        => Plain(TrailingYear().Replace(series ?? string.Empty, string.Empty));

    /// <summary>
    /// A movie key: the normalized title plus the year.
    /// </summary>
    /// <param name="title">The movie title.</param>
    /// <param name="year">The release year.</param>
    /// <returns>The lowercase key.</returns>
    public static string MovieKey(string? title, int? year)
        => Plain(TrailingYear().Replace(title ?? string.Empty, string.Empty)) + "|" + (year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

    /// <summary>
    /// Whether a listen or view is a real play. A song counts at 30 seconds or half of a track shorter than a minute; a movie or
    /// episode counts at 5 minutes, or 20 percent of anything under 25 minutes. Shorter ones still add to time listened.
    /// </summary>
    /// <param name="video">True for a movie or episode.</param>
    /// <param name="seconds">Time actually heard or watched.</param>
    /// <param name="durationSeconds">The item's length, or 0 if unknown.</param>
    /// <returns>True when it counts.</returns>
    public static bool IsCounted(bool video, double seconds, double durationSeconds)
    {
        if (video)
        {
            return seconds >= ViewSeconds || (durationSeconds is > 0 and < 1500 && seconds >= durationSeconds * 0.2);
        }

        return seconds >= PlaySeconds || (durationSeconds is > 0 and < 60 && seconds >= durationSeconds / 2);
    }

    /// <summary>
    /// How much listening time a progress report adds. Nothing while paused, nothing for a seek (a jump backwards, or a jump
    /// forwards by more than the clock moved plus a few seconds).
    /// </summary>
    /// <param name="previousTicks">The position at the last report.</param>
    /// <param name="currentTicks">The position now.</param>
    /// <param name="wallSeconds">Seconds on the clock since the last report.</param>
    /// <param name="wasPaused">Whether playback was paused at the last report.</param>
    /// <returns>Seconds to add.</returns>
    public static double ListenedSince(long previousTicks, long currentTicks, double wallSeconds, bool wasPaused)
    {
        if (wasPaused)
        {
            return 0;
        }

        var moved = (currentTicks - previousTicks) / (double)TimeSpan.TicksPerSecond;
        return moved <= 0 || moved > wallSeconds + 3 ? 0 : moved;
    }

    private static string StripSongNoise(string? title)
    {
        var text = SongBrackets().Replace(title ?? string.Empty, " ");
        return SongDashSuffix().Replace(text, string.Empty);
    }

    private static string StripAlbumNoise(string? album)
    {
        var text = AlbumBrackets().Replace(album ?? string.Empty, " ");
        text = SongDashSuffix().Replace(text, string.Empty);
        return AlbumTrailingWords().Replace(text, string.Empty);
    }

    private static string Plain(string? text)
    {
        var builder = new StringBuilder();
        var lastSpace = true;
        foreach (var c in (text ?? string.Empty).Normalize(NormalizationForm.FormKD).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                lastSpace = false;
            }
            else if (c is '\'' or '’')
            {
                // An apostrophe is part of a word ("Don't"), not a break between words
            }
            else if (!lastSpace && char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(' ');
                lastSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex(@"[\(\[][^\)\]]*\b(feat|ft|featuring|remaster|remastered|deluxe|bonus|explicit|clean|mono|stereo|version|edit|mix|live)\b[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex SongBrackets();

    [GeneratedRegex(@"\s+-\s+(.*\b(remaster|remastered|version|mono|stereo)\b.*|feat\b.*|ft\b.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex SongDashSuffix();

    [GeneratedRegex(@"[\(\[][^\)\]]*\b(deluxe|expanded|remaster|remastered|edition|version|anniversary|bonus|special|collector)\b[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex AlbumBrackets();

    [GeneratedRegex(@"\s+(super\s+)?(deluxe|expanded|remastered)(\s+(edition|version))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AlbumTrailingWords();

    [GeneratedRegex(@"\s*\(\d{4}\)\s*$")]
    private static partial Regex TrailingYear();
}
