using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Controller.Configuration;
using Khylo.Controller.Session;
using Khylo.Model.Activity;
using Khylo.Server.Core.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Updates;

// When Khylo runs as a limited account, the update helper installs the update where Khylo can't see how it went: Khylo tells by
// the build it comes back as
public sealed class KhyloPendingUpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "khylo-pending-tests-" + Guid.NewGuid().ToString("N"));

    public KhyloPendingUpdateTests()
    {
        Directory.CreateDirectory(_dir);
    }

    private static string RunningBuild => typeof(KhyloUpdateService).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "KhyloBuild")?.Value ?? string.Empty;

    [Fact]
    public async Task WhenKhyloComesBackAsTheStagedBuild_ItSaysItUpdated_AndForgetsThePending()
    {
        var service = await StartedWith(RunningBuild, DateTime.UtcNow.AddMinutes(-3));

        Assert.Equal($"Khylo updated to {RunningBuild}.", service.GetStatus().LastResult);
        Assert.DoesNotContain("PendingBuild\": \"", await File.ReadAllTextAsync(Path.Combine(_dir, "khylo_updates.json"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenAGoodWhileLaterItIsStillAnotherBuild_ItSaysTheUpdateDidntFinish()
    {
        var service = await StartedWith("DEV-V99.01.01.01", DateTime.UtcNow.AddMinutes(-30));

        var note = service.GetStatus().LastResult;

        Assert.NotNull(note);
        Assert.Contains("DEV-V99.01.01.01 didn't finish", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhileItMayStillBeInstalling_NothingIsSaid_AndThePendingIsKept()
    {
        var service = await StartedWith("DEV-V99.01.01.01", DateTime.UtcNow.AddMinutes(-1));

        Assert.Null(service.GetStatus().LastResult);
        Assert.Contains("DEV-V99.01.01.01", await File.ReadAllTextAsync(Path.Combine(_dir, "khylo_updates.json"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // A temporary folder that could not be removed is not a failed test
        }
    }

    private async Task<KhyloUpdateService> StartedWith(string pendingBuild, DateTime pendingAt)
    {
        await File.WriteAllTextAsync(
            Path.Combine(_dir, "khylo_updates.json"),
            $"{{\"Channel\":\"dev\",\"PendingBuild\":\"{pendingBuild}\",\"PendingAt\":\"{pendingAt:O}\"}}",
            TestContext.Current.CancellationToken);
        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(p => p.ConfigurationDirectoryPath).Returns(_dir);
        paths.SetupGet(p => p.CachePath).Returns(_dir);
        var service = new KhyloUpdateService(
            NullLogger<KhyloUpdateService>.Instance,
            paths.Object,
            Mock.Of<IServerConfigurationManager>(),
            Mock.Of<ISessionManager>(),
            Mock.Of<IActivityManager>(),
            Mock.Of<IHttpClientFactory>());

        // The service reads what the last update did as it starts, before it waits for anything
        await service.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(400, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
        return service;
    }
}
