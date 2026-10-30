using Khylo.Server.Implementations.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Khylo.Server.Integration.Tests;

public sealed class HostedServiceRegistrationTests : IClassFixture<KhyloApplicationFactory>
{
    private readonly KhyloApplicationFactory _factory;

    public HostedServiceRegistrationTests(KhyloApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void DeviceAccessHost_IsRegisteredAsHostedService()
    {
        _ = _factory.CreateClient();

        var hostedServices = _factory.Services.GetServices<IHostedService>();

        Assert.Contains(hostedServices, service => service is DeviceAccessHost);
    }
}
