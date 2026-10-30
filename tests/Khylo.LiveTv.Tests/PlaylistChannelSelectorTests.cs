using Khylo.Controller.LiveTv;
using Khylo.Model.LiveTv;
using Xunit;

namespace Khylo.LiveTv.Tests
{
    public class PlaylistChannelSelectorTests
    {
        private static ChannelInfo Channel(string id, string group, string path = "http://provider/live/u/p/1.ts", string name = "Channel")
            => new() { Id = id, ChannelGroup = group, Path = path, Name = name };

        [Fact]
        public void IsIncluded_SelectionOff_IncludesEverything()
        {
            var tuner = new TunerHostInfo { EnableChannelSelection = false };

            Assert.True(PlaylistChannelSelector.IsIncluded(tuner, Channel("a", "Anything")));
        }

        [Fact]
        public void IsIncluded_NothingPicked_ImportsNothing()
        {
            var tuner = new TunerHostInfo { EnableChannelSelection = true };

            Assert.False(PlaylistChannelSelector.IsIncluded(tuner, Channel("a", "USA | Sports")));
        }

        [Fact]
        public void IsIncluded_GroupPicked_IncludesItsChannelsExceptExcluded()
        {
            var tuner = new TunerHostInfo
            {
                EnableChannelSelection = true,
                IncludedGroups = ["usa | sports"],
                ExcludedChannels = ["b"]
            };

            Assert.True(PlaylistChannelSelector.IsIncluded(tuner, Channel("a", "USA | Sports")));
            Assert.False(PlaylistChannelSelector.IsIncluded(tuner, Channel("b", "USA | Sports")));
            Assert.False(PlaylistChannelSelector.IsIncluded(tuner, Channel("c", "UK | Sports")));
        }

        [Fact]
        public void IsIncluded_SingleChannelPicked_IncludedOutsideItsGroup()
        {
            var tuner = new TunerHostInfo { EnableChannelSelection = true, IncludedChannels = ["c"] };

            Assert.True(PlaylistChannelSelector.IsIncluded(tuner, Channel("c", "UK | Sports")));
        }

        [Theory]
        [InlineData("http://provider:8080/movie/u/p/123.mkv", "Movies", PlaylistChannelKind.OnDemand)]
        [InlineData("http://provider:8080/series/u/p/456.mp4", "Series", PlaylistChannelKind.OnDemand)]
        [InlineData("http://provider:8080/u/p/789", "XXX | Adults", PlaylistChannelKind.Adult)]
        [InlineData("http://provider:8080/u/p/790", "USA | News", PlaylistChannelKind.Live)]
        [InlineData("http://provider:8080/u/p/791", "Sussex Local", PlaylistChannelKind.Live)]
        public void GetKind_RecognizesOnDemandAndAdultEntries(string path, string group, PlaylistChannelKind expected)
        {
            Assert.Equal(expected, PlaylistChannelSelector.GetKind(Channel("x", group, path)));
        }
    }
}
