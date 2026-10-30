#nullable disable

#pragma warning disable CS1591

using Khylo.Model.LiveTv;

namespace Khylo.Controller.Entities
{
    public interface IHasProgramAttributes
    {
        bool IsMovie { get; set; }

        bool IsSports { get; }

        bool IsNews { get; }

        bool IsKids { get; }

        bool IsRepeat { get; set; }

        bool IsSeries { get; set; }

        ProgramAudio? Audio { get; set; }

        string EpisodeTitle { get; set; }

        string ServiceName { get; set; }
    }
}
