namespace Khylo.Model.Entities
{
    /// <summary>
    /// Represents the classified disc/image type for an ISO or optical image file.
    /// </summary>
    public enum IsoType
    {
        /// <summary>DVD video disc image.</summary>
        Dvd = 0,

        /// <summary>Blu-ray disc image.</summary>
        BluRay = 1,

        /// <summary>PlayStation 1 game disc image.</summary>
        PlayStation1 = 10,

        /// <summary>PlayStation 2 game disc image.</summary>
        PlayStation2 = 11,

        /// <summary>PlayStation Portable game image.</summary>
        Psp = 12,

        /// <summary>Sega Saturn game disc image.</summary>
        SegaSaturn = 13,

        /// <summary>Sega Dreamcast game disc image.</summary>
        SegaDreamcast = 14,

        /// <summary>PC/software disc image.</summary>
        PcSoftware = 20,

        /// <summary>Generic data disc image.</summary>
        DataDisc = 21,

        /// <summary>Classification is ambiguous or needs administrator review.</summary>
        Unknown = 99,

        /// <summary>Administrator has manually overridden the classification.</summary>
        ManualOverride = 100,
    }
}
