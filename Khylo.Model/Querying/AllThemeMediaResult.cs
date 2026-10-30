#nullable disable
#pragma warning disable CS1591

namespace Khylo.Model.Querying
{
    public class AllThemeMediaResult
    {
        public AllThemeMediaResult()
        {
            ThemeVideosResult = new ThemeMediaResult();

            ThemeSongsResult = new ThemeMediaResult();

            SoundtrackSongsResult = new ThemeMediaResult();
        }

        public ThemeMediaResult ThemeVideosResult { get; set; }

        public ThemeMediaResult ThemeSongsResult { get; set; }

        public ThemeMediaResult SoundtrackSongsResult { get; set; }
    }
}
