using System.Linq;
using Khylo.LiveTv.Recordings;
using Xunit;

namespace Khylo.LiveTv.Tests;

public class NetworkPathTests
{
    [Fact]
    public void Variants_GivesTheShareWithAndWithoutTheDot()
    {
        Assert.Equal([@"\\bb02\dfs01\DVR", @"\\bb02.\dfs01\DVR"], NetworkPath.Variants(@"\\bb02\dfs01\DVR").ToArray());
        Assert.Equal([@"\\bb02.\dfs01\DVR", @"\\bb02\dfs01\DVR"], NetworkPath.Variants(@"\\bb02.\dfs01\DVR").ToArray());
    }

    [Fact]
    public void Variants_LeavesOrdinaryPathsAlone()
    {
        Assert.Equal([@"D:\DVR"], NetworkPath.Variants(@"D:\DVR").ToArray());
    }

    [Fact]
    public void Normalize_MakesBothSpellingsEqual()
    {
        Assert.Equal(NetworkPath.Normalize(@"\\bb02.\dfs01\DVR\"), NetworkPath.Normalize(@"\\bb02\dfs01\DVR"));
    }
}
