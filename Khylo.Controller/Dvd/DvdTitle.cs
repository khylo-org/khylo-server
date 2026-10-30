using System.Collections.Generic;

namespace Khylo.Controller.Dvd;

/// <summary>A title on the disc: the main feature, an episode, an extra.</summary>
public sealed class DvdTitle
{
    /// <summary>Gets the title number on the disc, from 1.</summary>
    public int Number { get; init; }

    /// <summary>Gets the title set (VTS) holding the title.</summary>
    public int TitleSet { get; init; }

    /// <summary>Gets the length in seconds.</summary>
    public double Seconds { get; init; }

    /// <summary>Gets the chapters.</summary>
    public IReadOnlyList<DvdChapter> Chapters { get; init; } = [];

    /// <summary>Gets the audio tracks that play with this title.</summary>
    public IReadOnlyList<DvdAudioTrack> Audio { get; init; } = [];

    /// <summary>Gets the subtitle tracks that play with this title.</summary>
    public IReadOnlyList<DvdSubtitleTrack> Subtitles { get; init; } = [];

    /// <summary>Gets the picture width.</summary>
    public int Width { get; init; }

    /// <summary>Gets the picture height.</summary>
    public int Height { get; init; }

    /// <summary>Gets the frame rate.</summary>
    public double FrameRate { get; init; }

    /// <summary>Gets a value indicating whether the picture is widescreen.</summary>
    public bool IsWidescreen { get; init; }

    /// <summary>Gets the pieces to play, in order.</summary>
    public IReadOnlyList<DvdSegment> Segments { get; init; } = [];
}
