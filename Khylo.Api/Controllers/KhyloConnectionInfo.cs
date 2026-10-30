using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Khylo.Common.Net;
using Khylo.Controller;
using Khylo.Controller.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// Every way to reach this server.
/// </summary>
public class KhyloConnectionInfo
{
    /// <summary>Gets or sets the server's id; an address only counts when it answers with this id.</summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>Gets or sets the server's name.</summary>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the addresses that work on the home network, best first.</summary>
    public IReadOnlyList<string> HomeAddresses { get; set; } = [];

    /// <summary>Gets or sets the address that works from anywhere, if the server has one.</summary>
    public string? WebAddress { get; set; }
}
