using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Khylo.Api.Helpers.KhyloImport;

/// <summary>
/// Small helpers for talking to the other server.
/// </summary>
public static class ImportHttp
{
    /// <summary>
    /// Tidies an address: adds http:// when it's missing and drops a trailing slash.
    /// </summary>
    /// <param name="url">The address as typed.</param>
    /// <returns>The address.</returns>
    public static string NormalizeUrl(string url)
    {
        var trimmed = (url ?? string.Empty).Trim().TrimEnd('/');
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "http://" + trimmed;
        }

        return trimmed;
    }

    /// <summary>
    /// Sends a request and reads the JSON answer, with a plain-language error when it fails.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The JSON document.</returns>
    public static async Task<JsonDocument> SendForJsonAsync(HttpClient http, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ImportException($"Couldn't reach {request.RequestUri?.Host}. Check the address and that the server is running. ({ex.Message})");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ImportException($"{request.RequestUri?.Host} took too long to answer.");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ImportException("The server didn't accept the key or token. Check it and try again.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ImportException($"The server answered {(int)response.StatusCode} for {request.RequestUri?.AbsolutePath}.");
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                try
                {
                    return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (JsonException)
                {
                    throw new ImportException("The server's answer wasn't what Khylo expected. Is this the right kind of server?");
                }
            }
        }
    }

    /// <summary>Reads a string property.</summary>
    /// <param name="e">The element.</param>
    /// <param name="name">The property.</param>
    /// <returns>The value, or null.</returns>
    public static string? Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        };
    }

    /// <summary>Reads a whole-number property.</summary>
    /// <param name="e">The element.</param>
    /// <param name="name">The property.</param>
    /// <returns>The value, or null.</returns>
    public static int? Int(JsonElement e, string name)
        => Long(e, name) is { } value && value is >= int.MinValue and <= int.MaxValue ? (int)value : null;

    /// <summary>Reads a long property.</summary>
    /// <param name="e">The element.</param>
    /// <param name="name">The property.</param>
    /// <returns>The value, or null.</returns>
    public static long? Long(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
        {
            return null;
        }

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n))
        {
            return n;
        }

        if (v.ValueKind == JsonValueKind.Number)
        {
            return (long)v.GetDouble();
        }

        return v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s) ? s : null;
    }

    /// <summary>Reads a true/false property.</summary>
    /// <param name="e">The element.</param>
    /// <param name="name">The property.</param>
    /// <returns>The value (false when missing).</returns>
    public static bool Bool(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
