using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Controller.Webhooks;
using Khylo.Model.Webhooks;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Implementations.Webhooks;

/// <summary>
/// Service managing server-side outbound event webhooks.
/// </summary>
public class WebhookManager : IWebhookManager, IDisposable
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };

    private readonly ILogger<WebhookManager> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _filePath;
    private readonly ConcurrentDictionary<Guid, KhyloWebhookDto> _webhooks = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookManager"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="appPaths">Application paths.</param>
    public WebhookManager(
        ILogger<WebhookManager> logger,
        IHttpClientFactory httpClientFactory,
        IApplicationPaths appPaths)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _filePath = Path.Combine(appPaths.ConfigurationDirectoryPath, "khylo_webhooks.json");
        LoadWebhooks();
    }

    /// <inheritdoc/>
    public KhyloWebhookDto CreateWebhook(CreateWebhookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Url);

        var webhook = new KhyloWebhookDto
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(request.Name) ? request.Url : request.Name.Trim(),
            Url = request.Url.Trim(),
            Secret = string.IsNullOrWhiteSpace(request.Secret) ? null : request.Secret.Trim(),
            EnabledEvents = request.EnabledEvents is not null && request.EnabledEvents.Length > 0
                ? request.EnabledEvents
                : ["*"],
            IsActive = true,
            DateCreated = DateTime.UtcNow
        };

        _webhooks[webhook.Id] = webhook;
        SaveWebhooks();
        _logger.LogInformation("Registered outbound webhook {WebhookId} -> {Url}", webhook.Id, webhook.Url);
        return webhook;
    }

    /// <inheritdoc/>
    public IReadOnlyList<KhyloWebhookDto> GetWebhooks()
    {
        return _webhooks.Values.OrderByDescending(w => w.DateCreated).ToList();
    }

    /// <inheritdoc/>
    public bool DeleteWebhook(Guid id)
    {
        if (_webhooks.TryRemove(id, out var removed))
        {
            SaveWebhooks();
            _logger.LogInformation("Deleted outbound webhook {WebhookId} ({Url})", id, removed.Url);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task DispatchEventAsync(string eventType, object eventData, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        var targetWebhooks = _webhooks.Values
            .Where(w => w.IsActive && (w.EnabledEvents.Contains("*") || w.EnabledEvents.Contains(eventType, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        if (targetWebhooks.Count == 0)
        {
            return;
        }

        var payload = new
        {
            @event = eventType,
            timestamp = DateTime.UtcNow,
            data = eventData
        };

        var json = JsonSerializer.Serialize(payload, _jsonSerializerOptions);

        var client = _httpClientFactory.CreateClient("KhyloWebhooks");
        foreach (var webhook in targetWebhooks)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                if (!string.IsNullOrEmpty(webhook.Secret))
                {
                    var signature = ComputeHmacSha256(json, webhook.Secret);
                    request.Headers.Add("X-Khylo-Signature", $"sha256={signature}");
                }

                request.Headers.Add("X-Khylo-Event", eventType);

                var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Webhook {WebhookId} returned non-success HTTP status {StatusCode}", webhook.Id, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to deliver webhook event {EventType} to {Url}", eventType, webhook.Url);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SendTestPingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_webhooks.TryGetValue(id, out var webhook))
        {
            return false;
        }

        var payload = new
        {
            @event = "server.ping",
            timestamp = DateTime.UtcNow,
            data = new
            {
                message = "Khylo server outbound webhook test ping.",
                server = "Khylo"
            }
        };

        var json = JsonSerializer.Serialize(payload, _jsonSerializerOptions);
        var client = _httpClientFactory.CreateClient("KhyloWebhooks");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            if (!string.IsNullOrEmpty(webhook.Secret))
            {
                var signature = ComputeHmacSha256(json, webhook.Secret);
                request.Headers.Add("X-Khylo-Signature", $"sha256={signature}");
            }

            request.Headers.Add("X-Khylo-Event", "server.ping");

            var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Test ping to webhook {WebhookId} failed", id);
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and optionally managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _fileLock.Dispose();
            }

            _disposed = true;
        }
    }

    private static string ComputeHmacSha256(string content, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var contentBytes = Encoding.UTF8.GetBytes(content);
        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(contentBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private void LoadWebhooks()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<KhyloWebhookDto>>(json);
                if (list is not null)
                {
                    foreach (var item in list)
                    {
                        _webhooks[item.Id] = item;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Khylo webhooks from {Path}", _filePath);
        }
    }

    private void SaveWebhooks()
    {
        Task.Run(async () =>
        {
            await _fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var list = _webhooks.Values.ToList();
                var json = JsonSerializer.Serialize(list, _jsonSerializerOptions);
                await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save Khylo webhooks to {Path}", _filePath);
            }
            finally
            {
                _fileLock.Release();
            }
        });
    }
}
