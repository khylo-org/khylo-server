using System;
using System.Net;
using System.Threading.Tasks;
using Khylo.Api.Constants;
using Khylo.Controller.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Middleware;

/// <summary>
/// Learns the server's public web address (for example https://khylo.example.com) the first time someone
/// opens Khylo through one, so sign-in QR codes and links work away from home without any setup. Runs
/// after the forwarded headers, so a reverse proxy's real host and https are seen. Admins can change it.
/// </summary>
public class KhyloWebAddressMiddleware
{
    private static volatile bool _known;

    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloWebAddressMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next delegate in the pipeline.</param>
    public KhyloWebAddressMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Handles a request.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <param name="config">The server configuration.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>A task.</returns>
    public async Task Invoke(HttpContext context, IServerConfigurationManager config, ILogger<KhyloWebAddressMiddleware> logger)
    {
        await _next(context).ConfigureAwait(false);

        // Learned only from a signed-in admin's own visit: anyone can put any name in a request, and the address
        // ends up in invitation links and sign-in QR codes, so a stranger must never be able to choose it
        if (!_known)
        {
            if (!string.IsNullOrWhiteSpace(config.Configuration.KhyloWebAddress))
            {
                _known = true;
            }
            else if (context.User.IsInRole(UserRoles.Administrator)
                && context.Response.StatusCode < 400
                && LooksPublic(context.Request.Host.Host))
            {
                var address = $"{context.Request.Scheme}://{context.Request.Host}";
                config.Configuration.KhyloWebAddress = address;
                config.SaveConfiguration();
                _known = true;
                logger.LogInformation("Khylo's web address is {Address}; sign-in codes and links will use it", address);
            }
        }
    }

    // A real domain name: not an IP address, localhost or a home-network-only name
    private static bool LooksPublic(string host)
        => !string.IsNullOrEmpty(host)
            && host.Contains('.', StringComparison.Ordinal)
            && !IPAddress.TryParse(host, out _)
            && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".home", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
}
