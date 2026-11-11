using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Khylo.Server.Middleware;

/// <summary>
/// Adds the response headers that tell browsers to be careful with what Khylo sends: don't guess file types,
/// don't let other sites put Khylo in a frame, and don't pass the address (which can hold an access key)
/// on to other sites.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityHeadersMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next step in the request pipeline.</param>
    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Adds the headers to the response.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <returns>A task.</returns>
    public Task Invoke(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            AddIfMissing(headers, "X-Content-Type-Options", "nosniff");
            AddIfMissing(headers, "Referrer-Policy", "same-origin");
            AddIfMissing(headers, "X-Frame-Options", "SAMEORIGIN");

            // Only the parts of a content policy that can't break the players and readers: no framing by other
            // sites, no plug-in objects, no rewriting of the page's base address
            AddIfMissing(headers, "Content-Security-Policy", "frame-ancestors 'self'; object-src 'none'; base-uri 'self'");

            // Tell browsers to use https for this name from now on (checked here, after the forwarded headers are applied, so
            // a server behind a proxy that does the https is covered too)
            if (ShouldSendHsts(context.Request.IsHttps, context.Request.Host.Host))
            {
                AddIfMissing(headers, "Strict-Transport-Security", "max-age=15552000");
            }

            return Task.CompletedTask;
        });

        return _next(context);
    }

    /// <summary>
    /// Says whether a response should carry the header that makes browsers insist on https for this name. Only over
    /// https, and only for a real internet name: browsers ignore it for numbered addresses anyway, and a home name
    /// (khylo.local, a single word) often has a self-made certificate that a browser must still be allowed to accept.
    /// </summary>
    /// <param name="isHttps">Whether the request came in over https.</param>
    /// <param name="host">The name the request was sent to.</param>
    /// <returns>True when the header should be sent.</returns>
    public static bool ShouldSendHsts(bool isHttps, string? host)
    {
        var name = host?.Trim().Trim('[', ']').TrimEnd('.').ToLowerInvariant();
        if (!isHttps || string.IsNullOrEmpty(name) || IPAddress.TryParse(name, out _) || !name.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        return !new[] { ".local", ".lan", ".home", ".internal", ".home.arpa", ".localdomain", ".localhost", ".test", ".example" }
            .Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal));
    }

    private static void AddIfMissing(IHeaderDictionary headers, string name, string value)
    {
        if (!headers.ContainsKey(name))
        {
            headers[name] = value;
        }
    }
}
