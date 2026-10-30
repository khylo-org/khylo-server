using System;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Khylo.Server.Integration.Tests.Controllers;

public sealed class VideosControllerTests : IClassFixture<KhyloApplicationFactory>
{
    private readonly KhyloApplicationFactory _factory;
    private static string? _accessToken;

    public VideosControllerTests(KhyloApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DeleteAlternateSources_NonexistentItemId_NotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        var response = await client.DeleteAsync($"Videos/{Guid.NewGuid()}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
