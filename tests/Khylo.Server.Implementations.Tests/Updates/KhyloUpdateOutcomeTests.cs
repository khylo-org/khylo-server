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

// What the dashboard says about an update must always be about the build that is running. The update's script writes its result
// after the new Khylo has started, so a result can arrive late, and one from an earlier update can be left behind.
public sealed class KhyloUpdateOutcomeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "khylo-outcome-tests-" + Guid.NewGuid().ToString("N"));

    public KhyloUpdateOutcomeTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "update"));
    }

    private static string RunningBuild => typeof(KhyloUpdateService).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "KhyloBuild")?.Value ?? string.Empty;

    private string ResultFile => Path.Combine(_dir, "update", "result.txt");

    [Fact]
    public async Task AResultLeftByAnEarlierUpdate_IsNeverShownAsThisOne()
    {
        // The regression: "Khylo updated to <an older build>" shown while a newer build is running
        await File.WriteAllTextAsync(ResultFile, "ok|DEV-V20.01.01.01", TestContext.Current.CancellationToken);

        var service = await StartedWith(string.Empty);

        Assert.Null(service.GetStatus().LastResult);
        Assert.False(File.Exists(ResultFile));
    }

    [Fact]
    public async Task AResultForTheRunningBuild_SaysItUpdatedToThatBuild()
    {
        await File.WriteAllTextAsync(ResultFile, $"ok|{RunningBuild}", TestContext.Current.CancellationToken);

        var service = await StartedWith(string.Empty);

        Assert.Equal($"Khylo updated to {RunningBuild}.", service.GetStatus().LastResult);
        Assert.False(File.Exists(ResultFile));
    }

    [Fact]
    public async Task ARecentRollBack_IsSaid()
    {
        await File.WriteAllTextAsync(ResultFile, "rolledback|DEV-V99.01.01.01", TestContext.Current.CancellationToken);

        var service = await StartedWith(string.Empty);

        Assert.Contains("went back to the version it had", service.GetStatus().LastResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOldRollBack_IsThrownAway()
    {
        await File.WriteAllTextAsync(ResultFile, "rolledback|DEV-V99.01.01.01", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(ResultFile, DateTime.UtcNow.AddHours(-5));

        var service = await StartedWith(string.Empty);

        Assert.Null(service.GetStatus().LastResult);
    }

    [Fact]
    public async Task AResultWrittenAfterKhyloHasStarted_IsPickedUpWithoutARestart()
    {
        // The real order of events: Khylo starts, answers its health check, and only then does the script write "ok"
        var service = await StartedWith(string.Empty);
        Assert.Null(service.GetStatus().LastResult);

        await File.WriteAllTextAsync(ResultFile, $"ok|{RunningBuild}", TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(10.5), TestContext.Current.CancellationToken);

        Assert.Equal($"Khylo updated to {RunningBuild}.", service.GetStatus().LastResult);
    }

    [Fact]
    public async Task WhileAnUpdateIsInstalling_TheStatusSaysSo_WhatItIsInstalling_AndWhenItBegan()
    {
        var began = DateTime.UtcNow.AddMinutes(-2);

        var service = await StartedWith($"{{\"Channel\":\"dev\",\"PendingBuild\":\"DEV-V99.01.01.01\",\"PendingAt\":\"{began:O}\"}}");
        var status = service.GetStatus();

        Assert.True(status.Installing);
        Assert.Equal("Installing", status.Stage);
        Assert.Equal("DEV-V99.01.01.01", status.UpdatingTo);
        Assert.Equal(began, status.UpdateStartedAt!.Value, TimeSpan.FromSeconds(1));
        Assert.InRange(status.ServerTime, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task AnUpdateThatNeverFinished_IsNoLongerShownAsRunning_AndSaysSo()
    {
        var service = await StartedWith($"{{\"Channel\":\"dev\",\"PendingBuild\":\"DEV-V99.01.01.01\",\"PendingAt\":\"{DateTime.UtcNow.AddMinutes(-30):O}\"}}");
        var status = service.GetStatus();

        Assert.False(status.Installing);
        Assert.Null(status.UpdatingTo);
        Assert.Contains("didn't finish", status.LastResult, StringComparison.Ordinal);
        Assert.Contains(status.UpdateLog, l => l.Text.Contains("didn't finish", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenKhyloComesBackAsTheStagedBuild_TheLogSaysSo()
    {
        var service = await StartedWith($"{{\"Channel\":\"dev\",\"PendingBuild\":\"{RunningBuild}\",\"PendingAt\":\"{DateTime.UtcNow.AddMinutes(-4):O}\"}}");

        var status = service.GetStatus();

        Assert.False(status.Installing);
        Assert.Equal($"Khylo updated to {RunningBuild}.", status.LastResult);
        Assert.Contains(status.UpdateLog, l => l.Text == $"Khylo updated to {RunningBuild}.");
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

    private async Task<KhyloUpdateService> StartedWith(string savedState)
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "khylo_updates.json"), string.IsNullOrEmpty(savedState) ? "{\"Channel\":\"dev\"}" : savedState, TestContext.Current.CancellationToken);
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

        // The service looks at how the last update went as it starts, before it waits for anything
        await service.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(400, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
        return service;
    }
}
