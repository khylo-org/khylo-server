using Khylo.Controller.Streaming;

namespace Khylo.Api.Models.StreamingDtos;

/// <summary>
/// The hls video request dto.
/// </summary>
public class HlsVideoRequestDto : VideoRequestDto
{
    /// <summary>
    /// Gets or sets a value indicating whether enable adaptive bitrate streaming.
    /// </summary>
    public bool EnableAdaptiveBitrateStreaming { get; set; }
}
