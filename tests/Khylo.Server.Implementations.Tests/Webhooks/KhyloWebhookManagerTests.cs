#nullable disable

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Model.Webhooks;
using Khylo.Server.Implementations.Webhooks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Webhooks
{
    public sealed class KhyloWebhookManagerTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly Mock<IApplicationPaths> _appPathsMock;
        private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
        private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;

        public KhyloWebhookManagerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "KhyloWebhookTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _appPathsMock = new Mock<IApplicationPaths>();
            _appPathsMock.Setup(a => a.ConfigurationDirectoryPath).Returns(_tempDir);

            _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
            _httpMessageHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

            var httpClient = new HttpClient(_httpMessageHandlerMock.Object);

            _httpClientFactoryMock = new Mock<IHttpClientFactory>();
            _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        }

        [Fact]
        public void CreateWebhook_ValidRequest_RegistersAndPersistsWebhook()
        {
            using var manager = new WebhookManager(
                NullLogger<WebhookManager>.Instance,
                _httpClientFactoryMock.Object,
                _appPathsMock.Object);

            var request = new CreateWebhookRequest
            {
                Name = "Discord Notifications",
                Url = "https://discord.example.com/webhook/123",
                Secret = "supersecretkey",
                EnabledEvents = new[] { "playback.start", "playback.stop" }
            };

            var created = manager.CreateWebhook(request);

            Assert.NotNull(created);
            Assert.NotEqual(Guid.Empty, created.Id);
            Assert.Equal("Discord Notifications", created.Name);
            Assert.Equal("https://discord.example.com/webhook/123", created.Url);
            Assert.Equal("supersecretkey", created.Secret);
            Assert.True(created.IsActive);
            Assert.Contains("playback.start", created.EnabledEvents);

            var all = manager.GetWebhooks();
            Assert.Single(all);
            Assert.Equal(created.Id, all[0].Id);
        }

        [Fact]
        public void DeleteWebhook_ExistingId_RemovesWebhook()
        {
            using var manager = new WebhookManager(
                NullLogger<WebhookManager>.Instance,
                _httpClientFactoryMock.Object,
                _appPathsMock.Object);

            var created = manager.CreateWebhook(new CreateWebhookRequest
            {
                Name = "Temporary Webhook",
                Url = "https://example.com/temp"
            });

            Assert.Single(manager.GetWebhooks());

            var deleted = manager.DeleteWebhook(created.Id);
            Assert.True(deleted);
            Assert.Empty(manager.GetWebhooks());
        }

        [Fact]
        public async Task DispatchEventAsync_MatchingEvent_SendsSignedPayload()
        {
            using var manager = new WebhookManager(
                NullLogger<WebhookManager>.Instance,
                _httpClientFactoryMock.Object,
                _appPathsMock.Object);

            manager.CreateWebhook(new CreateWebhookRequest
            {
                Name = "Signed Integration",
                Url = "https://receiver.example.com/events",
                Secret = "khylosecret",
                EnabledEvents = new[] { "playback.start" }
            });

            await manager.DispatchEventAsync("playback.start", new { itemId = Guid.NewGuid(), user = "admin" }, TestContext.Current.CancellationToken);

            _httpMessageHandlerMock.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post
                    && req.RequestUri.ToString() == "https://receiver.example.com/events"
                    && req.Headers.Contains("X-Khylo-Signature")
                    && req.Headers.Contains("X-Khylo-Event")),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task SendTestPingAsync_ExistingWebhook_SendsPingSuccessfully()
        {
            using var manager = new WebhookManager(
                NullLogger<WebhookManager>.Instance,
                _httpClientFactoryMock.Object,
                _appPathsMock.Object);

            var created = manager.CreateWebhook(new CreateWebhookRequest
            {
                Name = "Ping Target",
                Url = "https://receiver.example.com/ping"
            });

            var result = await manager.SendTestPingAsync(created.Id, TestContext.Current.CancellationToken);

            Assert.True(result);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
