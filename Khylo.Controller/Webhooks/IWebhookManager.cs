using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Model.Webhooks;

namespace Khylo.Controller.Webhooks;

/// <summary>
/// Service managing server-side outbound event webhooks.
/// </summary>
public interface IWebhookManager
{
    /// <summary>
    /// Registers a new webhook.
    /// </summary>
    /// <param name="request">Webhook configuration.</param>
    /// <returns>Created webhook DTO.</returns>
    KhyloWebhookDto CreateWebhook(CreateWebhookRequest request);

    /// <summary>
    /// Gets all registered webhooks.
    /// </summary>
    /// <returns>Read-only list of webhooks.</returns>
    IReadOnlyList<KhyloWebhookDto> GetWebhooks();

    /// <summary>
    /// Deletes a webhook by ID.
    /// </summary>
    /// <param name="id">Webhook identifier.</param>
    /// <returns>True if deleted, false if not found.</returns>
    bool DeleteWebhook(Guid id);

    /// <summary>
    /// Dispatches an event payload to all matching active webhooks asynchronously.
    /// </summary>
    /// <param name="eventType">Event type name (e.g. "playback.start").</param>
    /// <param name="eventData">Event data payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task.</returns>
    Task DispatchEventAsync(string eventType, object eventData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a test ping event to a specific webhook.
    /// </summary>
    /// <param name="id">Webhook identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if test request succeeded, false otherwise.</returns>
    Task<bool> SendTestPingAsync(Guid id, CancellationToken cancellationToken = default);
}
