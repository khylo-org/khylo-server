using System.Linq;
using System.Reflection;

namespace Khylo.Common
{
    /// <summary>
    /// The Khylo build number (for example DEV-V26.11.05.01) this server was built as. It is stamped into the build from the hub's
    /// VERSION file, and is the only version people are shown.
    /// </summary>
    public static class KhyloBuildInfo
    {
        /// <summary>
        /// Gets the build number, or "DEV" for a local build that was not stamped.
        /// </summary>
        public static string Number { get; } = typeof(KhyloBuildInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "KhyloBuild")?.Value is { Length: > 0 } stamped ? stamped : "DEV";
    }
}
