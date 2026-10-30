namespace Khylo.Controller.Dvd;

/// <summary>A file on a DVD: either a file inside an ISO image or a file in a VIDEO_TS folder.</summary>
public sealed class DvdFile
{
    /// <summary>Gets the upper-case file name, for example VTS_01_1.VOB.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the ISO image or the file on disk that holds the bytes.</summary>
    public required string PhysicalPath { get; init; }

    /// <summary>Gets where the file starts inside <see cref="PhysicalPath"/> (0 for a loose file).</summary>
    public long Offset { get; init; }

    /// <summary>Gets the file length in bytes.</summary>
    public long Length { get; init; }
}
