using System;
using System.Collections.Generic;

namespace Khylo.Controller.Library;

/// <summary>
/// Each person's notifications (new things in their libraries), kept on the server so reading and clearing them
/// works the same on every device.
/// </summary>
public interface INotificationInbox
{
    /// <summary>
    /// Gets a person's notifications, newest first.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The notifications.</returns>
    IReadOnlyList<KhyloNotification> Get(Guid userId);

    /// <summary>
    /// Marks one notification as read.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="id">The notification.</param>
    void MarkRead(Guid userId, Guid id);

    /// <summary>
    /// Marks all of a person's notifications as read.
    /// </summary>
    /// <param name="userId">The user.</param>
    void MarkAllRead(Guid userId);

    /// <summary>
    /// Removes one notification.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="id">The notification.</param>
    void Remove(Guid userId, Guid id);

    /// <summary>
    /// Removes all of a person's notifications.
    /// </summary>
    /// <param name="userId">The user.</param>
    void Clear(Guid userId);
}

/// <summary>
/// One notification.
/// </summary>
public class KhyloNotification
{
    /// <summary>Gets or sets the id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets what it's about, for example "NewMedia".</summary>
    public string Kind { get; set; } = "NewMedia";

    /// <summary>Gets or sets the headline, for example "3 new episodes of Bonanza".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the smaller line under the headline.</summary>
    public string? Text { get; set; }

    /// <summary>Gets or sets the item to open when it's clicked, if any.</summary>
    public Guid? ItemId { get; set; }

    /// <summary>Gets or sets the item whose picture to show.</summary>
    public Guid? ImageItemId { get; set; }

    /// <summary>Gets or sets when it happened (UTC).</summary>
    public DateTime Date { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets a value indicating whether it has been read.</summary>
    public bool Read { get; set; }
}
