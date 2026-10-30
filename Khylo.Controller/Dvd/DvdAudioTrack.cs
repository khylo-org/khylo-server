namespace Khylo.Controller.Dvd;

/// <summary>An audio track on the disc.</summary>
public sealed class DvdAudioTrack
{
    /// <summary>Gets the position among the title's audio tracks, from 0.</summary>
    public int Index { get; init; }

    /// <summary>Gets the ffmpeg codec name.</summary>
    public string Codec { get; init; } = "ac3";

    /// <summary>Gets the channel count.</summary>
    public int Channels { get; init; }

    /// <summary>Gets the three-letter language code, or null.</summary>
    public string? Language { get; init; }

    /// <summary>Gets the language's name, or null.</summary>
    public string? LanguageName { get; init; }

    /// <summary>Gets a note such as "Director's commentary", or null.</summary>
    public string? Note { get; init; }
}
