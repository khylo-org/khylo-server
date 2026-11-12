using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace Khylo.Controller.Security;

/// <summary>
/// Which apps may talk to this server. Only Khylo's own apps may: any other media app
/// must never connect, sign in or read anything. Every Khylo app introduces itself with a name that starts with "Khylo"
/// (Khylo Web, Khylo TV, Khylo iOS, Khylo Music iOS, Khylo Manager, Khylo on Android), so anything that names itself
/// otherwise is refused. Admin-made API keys (for tools such as a script) are not apps and are handled where the key is checked.
/// </summary>
public static class KhyloClientRules
{
    /// <summary>The word an app's name must start with.</summary>
    public const string RequiredPrefix = "Khylo";

    /// <summary>What a refused app is told.</summary>
    public const string RefusedMessage = "This server only works with Khylo apps.";

    private static readonly string[] SignInPaths =
    {
        "/Users/AuthenticateByName",
        "/Users/AuthenticateWithQuickConnect",
        "/QuickConnect/Initiate"
    };

    /// <summary>
    /// Says whether an app name belongs to a Khylo app.
    /// </summary>
    /// <param name="client">The name the app gave itself.</param>
    /// <returns>True for a Khylo app.</returns>
    public static bool IsKhyloClient(string? client)
        => !string.IsNullOrWhiteSpace(client)
           && client.Trim().StartsWith(RequiredPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Finds the name an app gave itself in a request, wherever an app might put it: the standard and the old
    /// "X-Emby-Authorization" header, the older separate "X-Emby-Client" header, or the address.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The name, or null when the request does not say.</returns>
    public static string? DeclaredClient(HttpRequest request)
    {
        foreach (var header in new[] { "Authorization", "X-Emby-Authorization", "X-MediaBrowser-Authorization" })
        {
            var found = ClientFromHeader(request.Headers[header].ToString());
            if (found is not null)
            {
                return found;
            }
        }

        foreach (var name in new[] { "X-Emby-Client", "X-MediaBrowser-Client" })
        {
            var value = request.Headers[name].ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            value = request.Query[name].ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the Client part of a header like: MediaBrowser Client="Name", Device="x", DeviceId="y", Version="1".
    /// </summary>
    /// <param name="header">The header's value.</param>
    /// <returns>The name, or null when there is none.</returns>
    public static string? ClientFromHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        var space = header.IndexOf(' ', StringComparison.Ordinal);
        var parts = space < 0 ? header : header[(space + 1)..];
        foreach (var part in SplitParts(parts))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && part[..eq].Trim().Equals("Client", StringComparison.OrdinalIgnoreCase))
            {
                var value = WebUtility.UrlDecode(part[(eq + 1)..].Trim().Trim('"'));
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    /// <summary>
    /// Says whether a request must be refused. An app that names itself as something other than a Khylo app is refused
    /// everywhere. The requests that start a sign-in must name a Khylo app, so an app
    /// that says nothing about itself does not get through there either. Anything else that names no app (a browser fetching
    /// a picture) still needs a sign-in, and the sign-in is only ever given to a Khylo app.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>True when the request must be refused.</returns>
    public static bool MustRefuse(HttpRequest request)
    {
        var client = DeclaredClient(request);
        if (client is not null)
        {
            return !IsKhyloClient(client);
        }

        var path = request.Path.Value ?? string.Empty;
        foreach (var start in SignInPaths)
        {
            if (path.EndsWith(start, StringComparison.OrdinalIgnoreCase) || path.Contains(start + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SplitParts(string value)
    {
        var start = 0;
        var quoted = false;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '"')
            {
                quoted = !quoted;
            }
            else if (value[i] == ',' && !quoted)
            {
                yield return value[start..i];
                start = i + 1;
            }
        }

        yield return value[start..];
    }
}
