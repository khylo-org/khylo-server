using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Khylo.Server.Integration.Tests.Middleware
{
    public sealed class RobotsRedirectionMiddlewareTests : IClassFixture<KhyloApplicationFactory>
    {
        private readonly KhyloApplicationFactory _factory;

        public RobotsRedirectionMiddlewareTests(KhyloApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task RobotsDotTxtRedirects()
        {
            var client = _factory.CreateClient(
                new WebApplicationFactoryClientOptions()
                {
                    AllowAutoRedirect = false
                });

            var response = await client.GetAsync("robots.txt", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("web/robots.txt", response.Headers.Location?.ToString());
        }
    }
}
