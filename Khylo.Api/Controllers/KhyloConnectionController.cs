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
/// Every way to reach this server, so apps can connect directly at home and over the web address when
/// away, and switch by themselves when the network changes. Public: apps read it before signing in.
/// </summary>
/// <remarks>
/// How apps use it: try each home address in order with a short timeout (a second or two) and use the
/// first that answers with this <see cref="KhyloConnectionInfo.ServerId"/>; otherwise use the web
/// address. Check again whenever the device's network changes, and when a request fails.
/// </remarks>
[Route("Khylo/Connection")]
public class KhyloConnectionController : BaseKhyloApiController
{
    private readonly IServerConfigurationManager _config;
    private readonly INetworkManager _networkManager;
    private readonly IServerApplicationHost _appHost;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloConnectionController"/> class.
    /// </summary>
    /// <param name="config">The server configuration.</param>
    /// <param name="networkManager">The network manager.</param>
    /// <param name="appHost">The application host.</param>
    public KhyloConnectionController(IServerConfigurationManager config, INetworkManager networkManager, IServerApplicationHost appHost)
    {
        _config = config;
        _networkManager = networkManager;
        _appHost = appHost;
    }

    /// <summary>
    /// Gets the server's home and web addresses.
    /// </summary>
    /// <response code="200">The addresses.</response>
    /// <returns>The connection info.</returns>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<KhyloConnectionInfo> Get()
    {
        var network = _config.GetNetworkConfiguration();
        var baseUrl = (network.BaseUrl ?? string.Empty).TrimEnd('/');

        var home = new List<string>();
        if (Normalize(_config.Configuration.KhyloHomeAddress, "http") is { } configured)
        {
            home.Add(configured);
        }

        // The server's own addresses on the home network
        foreach (var ip in _networkManager.GetInternalBindAddresses()
            .Select(d => d.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !a.ToString().StartsWith("169.254.", StringComparison.Ordinal)))
        {
            home.Add($"http://{ip}:{network.InternalHttpPort}{baseUrl}");
        }

        return new KhyloConnectionInfo
        {
            ServerId = _appHost.SystemId,
            ServerName = _appHost.FriendlyName,
            HomeAddresses = home.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            WebAddress = Normalize(_config.Configuration.KhyloWebAddress, "https")
        };
    }

    private static string? Normalize(string? address, string defaultScheme)
    {
        var trimmed = address?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{defaultScheme}://{trimmed}";
    }
}
