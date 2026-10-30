using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Khylo.Api.Extensions;
using Khylo.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// The signed-in person's notifications: new things in their libraries. Read and cleared on the server, so it's
/// the same on every device.
/// </summary>
[Route("Khylo/Notifications")]
[Authorize]
public class KhyloNotificationsController : BaseKhyloApiController
{
    private readonly INotificationInbox _inbox;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloNotificationsController"/> class.
    /// </summary>
    /// <param name="inbox">The notification inbox.</param>
    public KhyloNotificationsController(INotificationInbox inbox)
    {
        _inbox = inbox;
    }

    /// <summary>
    /// Gets the signed-in person's notifications, newest first.
    /// </summary>
    /// <response code="200">The notifications.</response>
    /// <returns>The notifications and how many are unread.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<NotificationList> Get()
    {
        var items = _inbox.Get(User.GetUserId());
        return new NotificationList { Items = items, UnreadCount = items.Count(n => !n.Read) };
    }

    /// <summary>
    /// Marks one notification as read.
    /// </summary>
    /// <param name="id">The notification.</param>
    /// <response code="204">Done.</response>
    /// <returns>No content.</returns>
    [HttpPost("{id:guid}/Read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult MarkRead([FromRoute, Required] Guid id)
    {
        _inbox.MarkRead(User.GetUserId(), id);
        return NoContent();
    }

    /// <summary>
    /// Marks every notification as read.
    /// </summary>
    /// <response code="204">Done.</response>
    /// <returns>No content.</returns>
    [HttpPost("Read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult MarkAllRead()
    {
        _inbox.MarkAllRead(User.GetUserId());
        return NoContent();
    }

    /// <summary>
    /// Removes one notification.
    /// </summary>
    /// <param name="id">The notification.</param>
    /// <response code="204">Done.</response>
    /// <returns>No content.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Remove([FromRoute, Required] Guid id)
    {
        _inbox.Remove(User.GetUserId(), id);
        return NoContent();
    }

    /// <summary>
    /// Removes every notification.
    /// </summary>
    /// <response code="204">Done.</response>
    /// <returns>No content.</returns>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Clear()
    {
        _inbox.Clear(User.GetUserId());
        return NoContent();
    }

    /// <summary>
    /// A person's notifications.
    /// </summary>
    public class NotificationList
    {
        /// <summary>Gets or sets the notifications, newest first.</summary>
        public IReadOnlyList<KhyloNotification> Items { get; set; } = [];

        /// <summary>Gets or sets how many are unread.</summary>
        public int UnreadCount { get; set; }
    }
}
