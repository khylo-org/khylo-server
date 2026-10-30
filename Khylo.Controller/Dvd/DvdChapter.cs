namespace Khylo.Controller.Dvd;

/// <summary>A chapter on the disc.</summary>
public sealed class DvdChapter
{
    /// <summary>Gets the chapter number, from 1.</summary>
    public int Number { get; init; }

    /// <summary>Gets where the chapter starts, in seconds from the start of the title.</summary>
    public double StartSeconds { get; init; }
}
