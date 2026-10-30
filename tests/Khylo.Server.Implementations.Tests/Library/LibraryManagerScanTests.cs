using System;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoMoq;
using Khylo.Controller.Configuration;
using Khylo.Model.Configuration;
using Khylo.Model.Tasks;
using Khylo.Naming.Common;
using Khylo.Server.Core.ScheduledTasks.Tasks;
using Moq;
using Xunit;
using ServerLibraryManager = Khylo.Server.Core.Library.LibraryManager;

namespace Khylo.Server.Implementations.Tests.Library;

public class LibraryManagerScanTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartScanInBackground_QueuesOnlyWhenIdle(bool scanRunning)
    {
        var fixture = new Fixture().Customize(new AutoMoqCustomization());
        fixture.Register(() => new NamingOptions());
        var configuration = fixture.Freeze<Mock<IServerConfigurationManager>>();
        configuration.Setup(c => c.Configuration).Returns(new ServerConfiguration());
        configuration.Setup(c => c.ApplicationPaths.ProgramDataPath).Returns("/data");
        var tasks = fixture.Freeze<Mock<ITaskManager>>();
        var manager = fixture.Create<ServerLibraryManager>();
        typeof(ServerLibraryManager).GetProperty(nameof(ServerLibraryManager.IsScanRunning))!.SetValue(manager, scanRunning);

        await manager.StartScanInBackground().ConfigureAwait(true);

        tasks.Verify(t => t.QueueScheduledTask<RefreshMediaLibraryTask>(), scanRunning ? Times.Never() : Times.Once());
        tasks.Verify(t => t.CancelIfRunningAndQueue<RefreshMediaLibraryTask>(), Times.Never());
    }

    [Fact]
    public async Task ValidateMediaLibrary_RestartsScheduledScan()
    {
        var fixture = new Fixture().Customize(new AutoMoqCustomization());
        fixture.Register(() => new NamingOptions());
        var configuration = fixture.Freeze<Mock<IServerConfigurationManager>>();
        configuration.Setup(c => c.Configuration).Returns(new ServerConfiguration());
        configuration.Setup(c => c.ApplicationPaths.ProgramDataPath).Returns("/data");
        var tasks = fixture.Freeze<Mock<ITaskManager>>();
        var manager = fixture.Create<ServerLibraryManager>();

        await manager.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None).ConfigureAwait(true);

        tasks.Verify(t => t.CancelIfRunningAndQueue<RefreshMediaLibraryTask>(), Times.Once());
        tasks.Verify(t => t.QueueScheduledTask<RefreshMediaLibraryTask>(), Times.Never());
    }
}
