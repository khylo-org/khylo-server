namespace Khylo.Controller.Dvd;

/// <summary>A subtitle track on the disc.</summary>
public sealed class DvdSubtitleTrack
{
    /// <summary>Gets the position among the title's subtitle tracks, from 0.</summary>
    public int Index { get; init; }

    /// <summary>Gets the three-letter language code, or null.</summary>
    public string? Language { get; init; }

    /// <summary>Gets the language's name, or null.</summary>
    public string? LanguageName { get; init; }

    /// <summary>Gets a note such as "Forced" or "Director's commentary", or null.</summary>
    public string? Note { get; init; }
}
