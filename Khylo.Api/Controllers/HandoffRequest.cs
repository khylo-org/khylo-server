using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Api.Extensions;
using Khylo.Controller.Session;
using Khylo.Model.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// A handoff.
/// </summary>
public class HandoffRequest
{
    /// <summary>Gets or sets the session that is playing; leave empty for this device.</summary>
    public string FromSessionId { get; set; } = string.Empty;

    /// <summary>Gets or sets the session that should take over; leave empty for this device.</summary>
    public string ToSessionId { get; set; } = string.Empty;

    /// <summary>Gets or sets where the playing device is right now, when it is the one asking.</summary>
    public long? PositionTicks { get; set; }

    /// <summary>Gets or sets the audio track in use, when the playing device is the one asking.</summary>
    public int? AudioStreamIndex { get; set; }

    /// <summary>Gets or sets the subtitle track in use, when the playing device is the one asking.</summary>
    public int? SubtitleStreamIndex { get; set; }
}
