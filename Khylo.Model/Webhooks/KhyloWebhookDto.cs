using System;

namespace Khylo.Model.Webhooks;

/// <summary>
/// Represents a configured outbound webhook destination.
/// </summary>
public class KhyloWebhookDto
{
    /// <summary>
    /// Gets or sets the webhook identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the friendly name of the webhook integration.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target URL to receive HTTP POST payloads.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional HMAC-SHA256 signing secret for payload verification.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// Gets or sets the list of enabled event types (e.g. "playback.start", "playback.stop", "user.created", "invitation.redeemed", "storage.warning", "*").
    /// </summary>
    public string[] EnabledEvents { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets a value indicating whether this webhook is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the date and time when the webhook was created.
    /// </summary>
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}
