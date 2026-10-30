#pragma warning disable CS1591

using Khylo.Model.Dto;

namespace Khylo.Model.MediaInfo
{
    public class LiveStreamResponse
    {
        public LiveStreamResponse(MediaSourceInfo mediaSource)
        {
            MediaSource = mediaSource;
        }

        public MediaSourceInfo MediaSource { get; }
    }
}
