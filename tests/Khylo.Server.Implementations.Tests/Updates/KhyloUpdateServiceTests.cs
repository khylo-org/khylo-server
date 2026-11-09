using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using Khylo.Common.Configuration;
using Khylo.Controller.Configuration;
using Khylo.Controller.Session;
using Khylo.Model.Activity;
using Khylo.Server.Core.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Updates;

public sealed class KhyloUpdateServiceTests : IDisposable
{
    private const string Note = "Khylo updated to DEV-V20.01.01.07.";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "khylo-update-tests-" + Guid.NewGuid().ToString("N"));

    public KhyloUpdateServiceTests()
    {
        Directory.CreateDirectory(_dir);
    }

    // The build this test run reports as the one running, read the way the service reads it
    private static string RunningBuild => typeof(KhyloUpdateService).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "KhyloBuild")?.Value ?? string.Empty;

    [Fact]
    public void Note_FromAnotherBuild_IsNotShown()
    {
        var service = ServiceWithSavedNote(DateTime.UtcNow, "DEV-V20.01.01.07");

        Assert.Null(service.GetStatus().LastResult);
    }

    // The reported case: a note saved by an older server, with no build recorded, kept showing for a day after later installs
    [Fact]
    public void Note_SavedWithoutABuild_IsNotShown()
    {
        var service = ServiceWithSavedNote(DateTime.UtcNow, null);

        Assert.Null(service.GetStatus().LastResult);
    }

    [Fact]
    public void Note_FromThisBuild_IsShown()
    {
        var service = ServiceWithSavedNote(DateTime.UtcNow, RunningBuild);

        Assert.Equal(Note, service.GetStatus().LastResult);
    }

    [Fact]
    public void Note_FromThisBuild_GoesAfterADay()
    {
        var service = ServiceWithSavedNote(DateTime.UtcNow.AddHours(-25), RunningBuild);

        Assert.Null(service.GetStatus().LastResult);
    }

    [Fact]
    public void Dismissing_RemovesTheNote()
    {
        var service = ServiceWithSavedNote(DateTime.UtcNow, RunningBuild);

        var status = service.DismissResult();

        Assert.Null(status.LastResult);
        Assert.Null(service.GetStatus().LastResult);
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

    private KhyloUpdateService ServiceWithSavedNote(DateTime writtenAt, string? build)
    {
        var buildField = build is null ? string.Empty : $",\"LastResultBuild\":\"{build}\"";
        File.WriteAllText(
            Path.Combine(_dir, "khylo_updates.json"),
            $"{{\"Channel\":\"dev\",\"LastResult\":\"{Note}\",\"LastResultAt\":\"{writtenAt:O}\"{buildField}}}");

        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(p => p.ConfigurationDirectoryPath).Returns(_dir);
        paths.SetupGet(p => p.CachePath).Returns(_dir);

        return new KhyloUpdateService(
            NullLogger<KhyloUpdateService>.Instance,
            paths.Object,
            Mock.Of<IServerConfigurationManager>(),
            Mock.Of<ISessionManager>(),
            Mock.Of<IActivityManager>(),
            Mock.Of<IHttpClientFactory>());
    }
}
