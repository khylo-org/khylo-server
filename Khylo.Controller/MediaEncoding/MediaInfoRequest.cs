#nullable disable

#pragma warning disable CS1591

using Khylo.Model.Dlna;
using Khylo.Model.Dto;

namespace Khylo.Controller.MediaEncoding
{
    public class MediaInfoRequest
    {
        public MediaSourceInfo MediaSource { get; set; }

        public bool ExtractChapters { get; set; }

        public DlnaProfileType MediaType { get; set; }
    }
}
