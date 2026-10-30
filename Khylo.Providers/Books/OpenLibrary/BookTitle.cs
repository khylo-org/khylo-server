using System;
using System.Linq;
using System.Text;

namespace Khylo.Providers.Books.OpenLibrary;

/// <summary>
/// Book titles as people search for them: without subtitles, the author added to a file name, or "The".
/// </summary>
public static class BookTitle
{
    /// <summary>
    /// The plain title: "Frankenstein; or, the modern prometheus" and "Frankenstein - Mary Shelley" both become "Frankenstein".
    /// </summary>
    /// <param name="title">The title as found.</param>
    /// <returns>The plain title.</returns>
    public static string Clean(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var text = title.Trim();
        foreach (var separator in new[] { " - ", "; ", ": ", " (" })
        {
            var at = text.IndexOf(separator, StringComparison.Ordinal);
            if (at > 2)
            {
                text = text[..at];
            }
        }

        return text.Trim();
    }

    /// <summary>
    /// A title as it should be shown: without a subtitle ("Moby Dick; Or, The Whale" becomes "Moby Dick",
    /// "Jane Eyre: An Autobiography" becomes "Jane Eyre") and in title case when a catalog wrote it all in
    /// lowercase ("The war of the worlds" becomes "The War of the Worlds").
    /// </summary>
    /// <param name="title">The title as found.</param>
    /// <returns>The title to show.</returns>
    public static string Tidy(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var text = title.Trim();
        foreach (var separator in new[] { "; ", " / ", ": " })
        {
            var at = text.IndexOf(separator, StringComparison.Ordinal);
            if (at > 2)
            {
                text = text[..at];
            }
        }

        text = text.TrimEnd(',', '.', ' ');
        var words = text.Split(' ');
        if (words.Skip(1).Any(w => w.Length > 3) && words.Skip(1).All(w => w.Length == 0 || !char.IsUpper(w[0])))
        {
            string[] small = ["a", "an", "and", "as", "at", "but", "by", "for", "in", "of", "on", "or", "the", "to", "with"];
            text = string.Join(' ', words.Select((w, i) => w.Length == 0 || (i > 0 && small.Contains(w)) ? w : char.ToUpperInvariant(w[0]) + w[1..]));
        }

        return text;
    }

    /// <summary>
    /// The author written after the title in a file name ("Frankenstein - Mary Shelley"), if there is one.
    /// </summary>
    /// <param name="title">The title as found.</param>
    /// <returns>The author, or null.</returns>
    public static string? AuthorFromName(string? title)
    {
        var at = title?.LastIndexOf(" - ", StringComparison.Ordinal) ?? -1;
        return at > 2 ? title![(at + 3)..].Trim() : null;
    }

    /// <summary>
    /// Whether two titles name the same book (ignoring case, punctuation, subtitles and a leading "The").
    /// </summary>
    /// <param name="a">One title.</param>
    /// <param name="b">The other title.</param>
    /// <returns>True when they match.</returns>
    public static bool SameBook(string? a, string? b)
    {
        var x = Key(Clean(a));
        var y = Key(Clean(b));
        return x.Length > 0 && (x == y || x.StartsWith(y, StringComparison.Ordinal) || y.StartsWith(x, StringComparison.Ordinal));
    }

    private static string Key(string title)
    {
        var lower = title.ToLowerInvariant();
        if (lower.StartsWith("the ", StringComparison.Ordinal))
        {
            lower = lower[4..];
        }

        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower.Where(char.IsLetterOrDigit))
        {
            sb.Append(ch);
        }

        return sb.ToString();
    }
}
