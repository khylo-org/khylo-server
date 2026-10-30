namespace Khylo.Api.Helpers.KhyloImport;

/// <summary>
/// The media server watch history is imported from.
/// </summary>
public enum ImportSourceKind
{
    /// <summary>Khylo (or Emby, which speaks the same language).</summary>
    Khylo,

    /// <summary>Plex Media Server.</summary>
    Plex
}
