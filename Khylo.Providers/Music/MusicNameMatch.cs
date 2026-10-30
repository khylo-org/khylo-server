using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Khylo.Providers.Music;

/// <summary>
/// Matches artist and album names between the user's files and online sources by name alone, for
/// files that carry no MusicBrainz ids (Apple Music and most store downloads). Only a match on the
/// whole name counts, so a short name never picks up a different artist.
/// </summary>
public static partial class MusicNameMatch
{
    /// <summary>
    /// Reduces a name to its letters and digits: case, accents, punctuation, a leading "The" and
    /// "&amp;" versus "and" don't matter.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The comparable form.</returns>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var text = name.Normalize(NormalizationForm.FormD).ToLowerInvariant().Replace("&", " and ", StringComparison.Ordinal);
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) || c == '$' ? c : ' ');
        }

        var words = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && words[0] == "the")
        {
            words.RemoveAt(0);
        }

        return string.Concat(words);
    }

    /// <summary>
    /// Whether two artist names are the same artist.
    /// </summary>
    /// <param name="a">One name.</param>
    /// <param name="b">The other name.</param>
    /// <returns>Whether they match.</returns>
    public static bool SameArtist(string? a, string? b)
    {
        var left = Normalize(a);
        return left.Length > 0 && left == Normalize(b);
    }

    /// <summary>
    /// Whether two album titles are the same album, ignoring store suffixes like " - EP", " - Single"
    /// and edition notes like "(Deluxe Edition)" or "[Remastered]".
    /// </summary>
    /// <param name="a">One title.</param>
    /// <param name="b">The other title.</param>
    /// <returns>Whether they match.</returns>
    public static bool SameAlbum(string? a, string? b)
    {
        var left = Normalize(StripAlbumExtras(a));
        return left.Length > 0 && left == Normalize(StripAlbumExtras(b));
    }

    /// <summary>
    /// A short, file-name-safe key for caching lookups by name.
    /// </summary>
    /// <param name="parts">The names making up the key.</param>
    /// <returns>The key.</returns>
    public static string CacheKey(params string?[] parts)
        => "name-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts.Select(Normalize)))))[..32].ToLowerInvariant();

    private static string StripAlbumExtras(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var stripped = StoreSuffix().Replace(title, string.Empty);
        stripped = EditionNote().Replace(stripped, string.Empty);
        return stripped.Trim();
    }

    [GeneratedRegex(@"\s+-\s+(EP|Single)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex StoreSuffix();

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*(deluxe|edition|remaster|expanded|anniversary|version|bonus|explicit|clean)[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex EditionNote();
}
