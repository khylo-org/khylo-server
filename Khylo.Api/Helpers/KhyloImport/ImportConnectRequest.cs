namespace Khylo.Api.Helpers.KhyloImport;

/// <summary>
/// How to reach the other server.
/// </summary>
public class ImportConnectRequest
{
    /// <summary>Gets or sets which kind of server it is.</summary>
    public ImportSourceKind Source { get; set; }

    /// <summary>Gets or sets the server's address, for example http://192.168.1.10:32400.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the admin API key (Khylo/Emby) or the Plex token.</summary>
    public string Token { get; set; } = string.Empty;
}
