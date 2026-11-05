namespace Khylo.Api.Controllers;

/// <summary>
/// Whether a person's listening is recorded.
/// </summary>
public class ListeningSettingsDto
{
    /// <summary>
    /// Gets or sets a value indicating whether recording is on.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
