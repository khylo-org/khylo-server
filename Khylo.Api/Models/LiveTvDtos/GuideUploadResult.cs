namespace Khylo.Api.Models.LiveTvDtos;

/// <summary>
/// Where an uploaded guide was saved.
/// </summary>
public class GuideUploadResult
{
    /// <summary>
    /// Gets or sets the path of the saved file on the server.
    /// </summary>
    public string Path { get; set; } = string.Empty;
}
