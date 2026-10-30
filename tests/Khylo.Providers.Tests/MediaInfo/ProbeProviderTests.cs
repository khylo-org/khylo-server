using Khylo.Controller.Chapters;
using Khylo.Controller.Configuration;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Entities.TV;
using Khylo.Controller.Library;
using Khylo.Controller.LiveTv;
using Khylo.Controller.Lyrics;
using Khylo.Controller.MediaEncoding;
using Khylo.Controller.Persistence;
using Khylo.Controller.Providers;
using Khylo.Controller.Subtitles;
using Khylo.Model.Globalization;
using Khylo.Model.IO;
using Khylo.Model.MediaInfo;
using Khylo.Naming.Common;
using Khylo.Providers.MediaInfo;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Khylo.Providers.Tests.MediaInfo;

public class ProbeProviderTests
{
    private readonly ProbeProvider _probeProvider;

    public ProbeProviderTests()
    {
        var mediaSourceManager = new Mock<IMediaSourceManager>();
        mediaSourceManager.Setup(m => m.GetPathProtocol(It.IsAny<string>()))
            .Returns(MediaProtocol.File);

        // prep BaseItem and Video for calls made that expect managers
        BaseItem.MediaSourceManager = mediaSourceManager.Object;
        Video.RecordingsManager = Mock.Of<IRecordingsManager>();

        _probeProvider = new ProbeProvider(
            mediaSourceManager.Object,
            Mock.Of<IMediaEncoder>(),
            Mock.Of<IBlurayExaminer>(),
            Mock.Of<ILocalizationManager>(),
            Mock.Of<IChapterManager>(),
            Mock.Of<IServerConfigurationManager>(),
            Mock.Of<ISubtitleManager>(),
            Mock.Of<ILibraryManager>(),
            Mock.Of<IFileSystem>(),
            NullLoggerFactory.Instance,
            new NamingOptions(),
            Mock.Of<ILyricManager>(),
            Mock.Of<IMediaAttachmentRepository>(),
            Mock.Of<IMediaStreamRepository>());
    }

    [Fact]
    public void HasChanged_NeverProbedVideo_ReturnsTrue()
    {
        // A probe that threw leaves the item like this while the refresh is stamped as done, and the
        // file's modification time never changes afterwards, so nothing else would ask for a retry.
        var item = new Episode { Path = "/media/show/S01E01.mkv" };

        Assert.True(_probeProvider.HasChanged(item, Mock.Of<IDirectoryService>()));
    }

    [Fact]
    public void HasChanged_NeverProbedAudio_ReturnsTrue()
    {
        var item = new Audio { Path = "/media/music/track.flac" };

        Assert.True(_probeProvider.HasChanged(item, Mock.Of<IDirectoryService>()));
    }

    [Theory]
    [InlineData(12345L, null)]
    [InlineData(null, 2480000)]
    [InlineData(12345L, 2480000)]
    public void HasChanged_ProbedVideo_ReturnsFalse(long? runTimeTicks, int? totalBitrate)
    {
        var item = new Episode
        {
            Path = "/media/show/S01E01.mkv",
            RunTimeTicks = runTimeTicks,
            TotalBitrate = totalBitrate
        };

        Assert.False(_probeProvider.HasChanged(item, Mock.Of<IDirectoryService>()));
    }

    [Fact]
    public void HasChanged_VirtualItemWithoutMediaInfo_ReturnsFalse()
    {
        var item = new Episode { Path = "/media/show/S01E01.mkv", IsVirtualItem = true };

        Assert.False(_probeProvider.HasChanged(item, Mock.Of<IDirectoryService>()));
    }

    [Fact]
    public void HasChanged_PlaceHolderWithoutMediaInfo_ReturnsFalse()
    {
        var item = new Episode { Path = "/media/show/S01E01.disc", IsPlaceHolder = true };

        Assert.False(_probeProvider.HasChanged(item, Mock.Of<IDirectoryService>()));
    }

    [Fact]
    public void HasChanged_ShortcutWithoutMediaInfo_ReturnsFalse()
    {
        // A .strm is only probed when remote content probing is enabled, so an empty one is expected.
        var item = new Episode { Path = "/media/show/S01E01.strm", IsShortcut = true };

        Assert.False(_probeProvider.HasChanged(item, Mock.Of<IDirectoryService>()));
    }
}
