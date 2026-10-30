using System;
using Khylo.Server.Implementations.LiveChannels;
using Xunit;

namespace Khylo.Server.Implementations.Tests.LiveChannels;

public class LiveChannelViewingRegistryTests
{
    private static readonly Guid _userId = Guid.NewGuid();
    private static readonly Guid _itemId = Guid.NewGuid();

    [Fact]
    public void IsChannelPlayback_OnlyForRegisteredDeviceAndItem()
    {
        var registry = new LiveChannelViewingRegistry();
        registry.Register(_userId, "device-a", _itemId, DateTime.UtcNow.AddHours(1));

        Assert.True(registry.IsChannelPlayback(_userId, "device-a", _itemId));
        Assert.False(registry.IsChannelPlayback(_userId, "device-b", _itemId));
        Assert.False(registry.IsChannelPlayback(_userId, "device-a", Guid.NewGuid()));
        Assert.False(registry.IsChannelPlayback(Guid.NewGuid(), "device-a", _itemId));
        Assert.False(registry.IsChannelPlayback(_userId, null, _itemId));
    }

    [Fact]
    public void IsChannelPlayback_ExpiredEntry_IsFalse()
    {
        var registry = new LiveChannelViewingRegistry();
        registry.Register(_userId, "device-a", _itemId, DateTime.UtcNow.AddSeconds(-1));

        Assert.False(registry.IsChannelPlayback(_userId, "device-a", _itemId));
    }

    [Fact]
    public void Release_KeepsShortGraceForFinalStopReport()
    {
        var registry = new LiveChannelViewingRegistry();
        registry.Register(_userId, "device-a", _itemId, DateTime.UtcNow.AddHours(2));

        registry.Release(_userId, "device-a");

        // Still suppressed right after leaving, so the player's stop report doesn't save progress
        Assert.True(registry.IsChannelPlayback(_userId, "device-a", _itemId));
    }
}
