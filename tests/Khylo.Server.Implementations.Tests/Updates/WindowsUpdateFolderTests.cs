using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using Khylo.Server.Core.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Updates;

// The system keeps the update's copies, script and backup in a folder the limited account that runs Khylo can't change or redirect
[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateFolderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "khylo-update-folder-tests-" + Guid.NewGuid().ToString("N"));

    public WindowsUpdateFolderTests()
    {
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public async Task ALinkIsNeverUsedAsTheUpdateFolder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var target = Directory.CreateDirectory(Path.Combine(_dir, "elsewhere")).FullName;
        var link = Path.Combine(_dir, "KhyloUpdate");
        using (var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true }))
        {
            Assert.NotNull(process);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, process.ExitCode);
        }

        var failure = Assert.Throws<InvalidOperationException>(() => WindowsUpdate.CreatePrivateWorkFolder(link, NullLogger.Instance));

        Assert.Contains("link", failure.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(target));
    }

    [Fact]
    public void ANewUpdateFolder_IsOpenOnlyToTheSystemAndAdministrators()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "needs administrator rights, to open a folder only administrators can open");
        var root = Path.Combine(_dir, "KhyloUpdate");

        var work = WindowsUpdate.CreatePrivateWorkFolder(root, NullLogger.Instance);

        Assert.StartsWith(root, work, StringComparison.OrdinalIgnoreCase);
        foreach (var folder in new[] { root, work })
        {
            var security = new DirectoryInfo(folder).GetAccessControl();
            Assert.True(security.AreAccessRulesProtected, $"{folder} still inherits permissions");
            var who = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Select(r => (SecurityIdentifier)r.IdentityReference).ToList();
            Assert.Equal(2, who.Count);
            Assert.Contains(who, sid => sid.IsWellKnown(WellKnownSidType.LocalSystemSid));
            Assert.Contains(who, sid => sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temporary folder that could not be removed is not a failed test
        }
    }
}
