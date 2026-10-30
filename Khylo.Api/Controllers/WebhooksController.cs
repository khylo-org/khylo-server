using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Api;
using Khylo.Controller.Webhooks;
using Khylo.Model.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// Khylo outbound webhooks management API.
/// </summary>
[Authorize(Policy = Policies.RequiresElevation)]
[Tags("Webhooks")]
public class WebhooksController : BaseKhyloApiController
{
    private readonly IWebhookManager _webhookManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhooksController"/> class.
    /// </summary>
    /// <param name="webhookManager">The webhook manager.</param>
    public WebhooksController(IWebhookManager webhookManager)
    {
        _webhookManager = webhookManager;
    }

    /// <summary>
    /// Registers a new outbound webhook.
    /// </summary>
    /// <param name="request">Webhook configuration.</param>
    /// <returns>The created webhook.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<KhyloWebhookDto> CreateWebhook([FromBody, Required] CreateWebhookRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest("Target URL is required.");
        }

        var webhook = _webhookManager.CreateWebhook(request);
        return Created($"/Webhooks/{webhook.Id}", webhook);
    }

    /// <summary>
    /// Gets all registered outbound webhooks.
    /// </summary>
    /// <returns>List of webhooks.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<KhyloWebhookDto>> GetWebhooks()
    {
        return Ok(_webhookManager.GetWebhooks());
    }

    /// <summary>
    /// Deletes an outbound webhook by ID.
    /// </summary>
    /// <param name="id">The webhook ID.</param>
    /// <returns>No content on success, or 404 if not found.</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteWebhook([FromRoute] Guid id)
    {
        var deleted = _webhookManager.DeleteWebhook(id);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>
    /// Sends a test ping event to an outbound webhook.
    /// </summary>
    /// <param name="id">The webhook ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status indicating test delivery result.</returns>
    [HttpPost("{id}/Test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> TestWebhook([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await _webhookManager.SendTestPingAsync(id, cancellationToken).ConfigureAwait(false);
        if (!result)
        {
            return StatusCode(StatusCodes.Status502BadGateway, "Webhook test ping failed or returned an error.");
        }

        return Ok();
    }
}
