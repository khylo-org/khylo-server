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
            return Task.CompletedTask;
        });

        return _next(context);
    }

    private static void AddIfMissing(IHeaderDictionary headers, string name, string value)
    {
        if (!headers.ContainsKey(name))
        {
            headers[name] = value;
        }
    }
}
