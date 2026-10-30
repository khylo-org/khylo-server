#nullable disable

#pragma warning disable CS1591

using System.Collections.Generic;

namespace Khylo.Controller.Providers
{
    public class MusicVideoInfo : ItemLookupInfo
    {
        public IReadOnlyList<string> Artists { get; set; }
    }
}
