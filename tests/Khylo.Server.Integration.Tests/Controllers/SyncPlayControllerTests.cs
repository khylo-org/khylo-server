using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Khylo.Server.Integration.Tests.Controllers;

public sealed class SyncPlayControllerTests : IClassFixture<KhyloApplicationFactory>
{
    private readonly KhyloApplicationFactory _factory;

    public SyncPlayControllerTests(KhyloApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetGroups_Unauthorized_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/SyncPlay/List", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetGroups_InvalidToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader("invalid-token");

        var response = await client.GetAsync("/SyncPlay/List", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
