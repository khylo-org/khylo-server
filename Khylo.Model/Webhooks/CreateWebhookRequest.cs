using System;

namespace Khylo.Model.Webhooks;

/// <summary>
/// Request payload to register a new server webhook.
/// </summary>
public class CreateWebhookRequest
{
    /// <summary>
    /// Gets or sets the friendly name of the webhook integration.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target HTTP POST destination URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets optional signing secret.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// Gets or sets the event types this webhook should receive.
    /// </summary>
    public string[] EnabledEvents { get; set; } = Array.Empty<string>();
}
