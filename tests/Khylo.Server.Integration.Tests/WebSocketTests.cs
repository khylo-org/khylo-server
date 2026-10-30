using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Khylo.Server.Integration.Tests
{
    public sealed class WebSocketTests : IClassFixture<KhyloApplicationFactory>
    {
        private readonly KhyloApplicationFactory _factory;

        public WebSocketTests(KhyloApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task WebSocket_Unauthenticated_ThrowsInvalidOperationException()
        {
            var server = _factory.Server;
            var client = server.CreateWebSocketClient();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.ConnectAsync(
                    new UriBuilder(server.BaseAddress)
                    {
                        Scheme = "ws",
                        Path = "websocket"
                    }.Uri,
                    CancellationToken.None));
        }
    }
}
