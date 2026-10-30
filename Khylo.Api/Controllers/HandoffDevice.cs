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
/// One of the person's devices, as shown in the handoff list.
/// </summary>
public class HandoffDevice
{
    /// <summary>Gets or sets the session to use when handing off.</summary>
    public string? SessionId { get; set; }

    /// <summary>Gets or sets the device's name (for example "Living Room TV").</summary>
    public string? DeviceName { get; set; }

    /// <summary>Gets or sets the app on the device.</summary>
    public string? Client { get; set; }

    /// <summary>Gets or sets the kind of device.</summary>
    public string? DeviceType { get; set; }

    /// <summary>Gets or sets a value indicating whether something is playing on the device.</summary>
    public bool IsPlaying { get; set; }

    /// <summary>Gets or sets what is playing.</summary>
    public Guid? NowPlayingItemId { get; set; }

    /// <summary>Gets or sets the name of what is playing.</summary>
    public string? NowPlayingName { get; set; }

    /// <summary>Gets or sets the show it belongs to, for episodes.</summary>
    public string? NowPlayingSeriesName { get; set; }

    /// <summary>Gets or sets how far along it is.</summary>
    public long? PositionTicks { get; set; }

    /// <summary>Gets or sets how long it is.</summary>
    public long? RunTimeTicks { get; set; }
}
