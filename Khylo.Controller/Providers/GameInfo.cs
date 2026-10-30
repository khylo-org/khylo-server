#nullable disable

namespace Khylo.Controller.Providers
{
    /// <summary>
    /// The lookup info for games (ROM-based).
    /// </summary>
    public class GameInfo : ItemLookupInfo
    {
        /// <summary>
        /// Gets or sets the target platform or console system name.
        /// </summary>
        public string Platform { get; set; }

        /// <summary>
        /// Gets or sets the ROM hash or checksum (CRC32/MD5/SHA1).
        /// </summary>
        public string RomChecksum { get; set; }
    }
}
