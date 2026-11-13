using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Configuration;
using Khylo.Model.Configuration;
using Khylo.Model.Updates;
using Khylo.Server.Migrations.Routines;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Khylo.Server.Tests.Migrations;

public class RemoveOtherProjectPluginRepositoryTests
{
    private static string Other(string path) => "https://repo." + string.Concat("jelly", "fin.org") + path;

    private static (RemoveOtherProjectPluginRepository Migration, ServerConfiguration Configuration, Mock<IServerConfigurationManager> Manager) Setup(params RepositoryInfo[] repositories)
    {
        var configuration = new ServerConfiguration { PluginRepositories = repositories };
        var manager = new Mock<IServerConfigurationManager>();
        manager.SetupGet(m => m.Configuration).Returns(configuration);
        return (new RemoveOtherProjectPluginRepository(manager.Object, NullLogger<RemoveOtherProjectPluginRepository>.Instance), configuration, manager);
    }

    [Theory]
    [InlineData("https://repo.jellyfin.org/files/plugin/manifest.json", true)]
    [InlineData("https://repo.jellyfin.org/releases/plugin/manifest-stable.json", true)]
    [InlineData("HTTPS://REPO.JELLYFIN.ORG/x", true)]
    [InlineData("https://jellyfin.org/x", true)]
    [InlineData("https://plugins.example.com/manifest.json", false)]
    [InlineData("https://notjellyfin.org.example.com/manifest.json", false)]
    [InlineData("https://example.com/jellyfin.org/manifest.json", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheOtherProjectsCatalogueIsRecognised(string? url, bool other)
        => Assert.Equal(other, RemoveOtherProjectPluginRepository.IsOtherProjectCatalogue(url));

    [Fact]
    public async Task TheOtherProjectsCatalogue_IsRemoved_AndAnAdminsOwnRepositoryIsKept()
    {
        var (migration, configuration, manager) = Setup(
            new RepositoryInfo { Name = "Other", Url = Other("/files/plugin/manifest.json") },
            new RepositoryInfo { Name = "Mine", Url = "https://plugins.example.com/manifest.json" },
            new RepositoryInfo { Name = "Other old", Url = Other("/releases/plugin/manifest-stable.json") });

        await migration.PerformAsync(CancellationToken.None);

        var kept = Assert.Single(configuration.PluginRepositories);
        Assert.Equal("Mine", kept.Name);
        manager.Verify(m => m.SaveConfiguration(), Times.Once);
    }

    [Fact]
    public async Task AServerWithNone_IsLeftAloneAndNotSaved()
    {
        var (migration, configuration, manager) = Setup(new RepositoryInfo { Name = "Mine", Url = "https://plugins.example.com/manifest.json" });

        await migration.PerformAsync(CancellationToken.None);

        Assert.Single(configuration.PluginRepositories);
        manager.Verify(m => m.SaveConfiguration(), Times.Never);
    }

    [Fact]
    public async Task AServerWithNoRepositoriesAtAll_IsFine()
    {
        var (migration, configuration, manager) = Setup();

        await migration.PerformAsync(CancellationToken.None);

        Assert.Empty(configuration.PluginRepositories);
        manager.Verify(m => m.SaveConfiguration(), Times.Never);
    }
}
