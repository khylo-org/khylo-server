using System;
using System.Runtime.Versioning;
using Khylo.MediaEncoding.Encoder;
using Xunit;

namespace Khylo.MediaEncoding.Tests;

[SupportedOSPlatform("macos")]
public class ApplePlatformHelperTests
{
    [Fact]
    public void GetSysctlValue_CpuBrand_NotEmpty()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS-only test");

        var value = ApplePlatformHelper.GetSysctlValue("machdep.cpu.brand_string");
        Assert.NotEmpty(value);

        // Make sure we don't include the null terminator
        Assert.DoesNotContain("\0", value, StringComparison.Ordinal);
    }
}
