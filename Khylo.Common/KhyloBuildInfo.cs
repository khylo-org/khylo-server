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

        /// <summary>
        /// Gets a value indicating whether the developer test tools are on: made-up listening history and an early look at the
        /// yearly reviews. They are off unless the server is started with KHYLO_TEST_TOOLS=1. The build number can't decide it:
        /// every build's number starts with DEV.
        /// </summary>
        public static bool TestToolsEnabled => IsOn(System.Environment.GetEnvironmentVariable("KHYLO_TEST_TOOLS"));

        private static bool IsOn(string? value)
            => string.Equals(value, "1", System.StringComparison.Ordinal) || string.Equals(value, "true", System.StringComparison.OrdinalIgnoreCase);
    }
}
