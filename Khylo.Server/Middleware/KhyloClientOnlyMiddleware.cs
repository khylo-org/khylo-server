using System.Threading.Tasks;
using Khylo.Controller.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Middleware;

/// <summary>
/// Refuses every request from an app that is not a Khylo app (any other media app), before anything else on the
/// server looks at it: no server details, no sign-in, no library. See <see cref="KhyloClientRules"/>.
/// </summary>
public class KhyloClientOnlyMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloClientOnlyMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next delegate in the pipeline.</param>
    public KhyloClientOnlyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Handles a request.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>A task.</returns>
    public async Task Invoke(HttpContext context, ILogger<KhyloClientOnlyMiddleware> logger)
    {
        if (KhyloClientRules.MustRefuse(context.Request))
        {
            logger.LogWarning(
                "Refused an app that is not a Khylo app: {Client} asked for {Path}",
                KhyloClientRules.DeclaredClient(context.Request) ?? "(no name)",
                context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(KhyloClientRules.RefusedMessage).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }
}
