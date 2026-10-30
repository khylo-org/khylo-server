namespace Khylo.Api.Helpers.KhyloImport;

/// <summary>
/// The kind of thing an entry is about.
/// </summary>
public enum ImportEntryKind
{
    /// <summary>A movie.</summary>
    Movie,

    /// <summary>A TV episode.</summary>
    Episode,

    /// <summary>A whole show (favorites).</summary>
    Series,

    /// <summary>A song.</summary>
    Track,

    /// <summary>An album (favorites).</summary>
    Album,

    /// <summary>An artist (favorites).</summary>
    Artist,

    /// <summary>An audiobook.</summary>
    AudioBook,

    /// <summary>A music video.</summary>
    MusicVideo
}
