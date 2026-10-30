namespace Khylo.Controller.Dvd;

/// <summary>One stretch of bytes to play, as a range inside an ISO or a whole VOB file.</summary>
public sealed class DvdSegment
{
    /// <summary>Gets the file holding the bytes.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the first byte.</summary>
    public long Start { get; init; }

    /// <summary>Gets or sets the length in bytes.</summary>
    public long Length { get; set; }

    /// <summary>Gets or sets the playing time in seconds.</summary>
    public double Seconds { get; set; }

    /// <summary>Gets a value indicating whether this is a whole loose file (a VIDEO_TS folder rip).</summary>
    public bool IsWholeFile { get; init; }

    /// <summary>Gets the ffmpeg input for this stretch.</summary>
    /// <returns>The file, or the byte range inside the disc image.</returns>
    public string ToFfmpegUrl() => IsWholeFile ? Path : DvdDisc.SubfileUrl(Path, Start, Start + Length);
}
