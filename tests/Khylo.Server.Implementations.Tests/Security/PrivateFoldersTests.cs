using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Khylo.Server.Core.Security;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Security;

public sealed class PrivateFoldersTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "khylo-private-tests-" + Guid.NewGuid().ToString("N"));

    public PrivateFoldersTests()
    {
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void FolderOpenToEveryUser_IsClosed_KeepingAdministratorsAndWhoeverRunsKhylo()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(_dir, "child"));
        File.WriteAllText(Path.Combine(_dir, "child", "secret.txt"), "x");
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var open = new DirectoryInfo(_dir).GetAccessControl();
        open.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        open.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(_dir).SetAccessControl(open);
        Assert.Contains(users, Sids(_dir));

        Assert.True(PrivateFolders.EnsurePrivate(_dir));

        foreach (var path in new[] { _dir, Path.Combine(_dir, "child"), Path.Combine(_dir, "child", "secret.txt") })
        {
            var sids = Sids(path);
            Assert.DoesNotContain(users, sids);
            Assert.DoesNotContain(everyone, sids);
            Assert.Contains(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), sids);
            Assert.Contains(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), sids);
            Assert.Contains(WindowsIdentity.GetCurrent().User!, sids);
        }

        Assert.Equal("x", File.ReadAllText(Path.Combine(_dir, "child", "secret.txt")));
    }

    [Fact]
    public void FolderThatIsAlreadyPrivate_IsLeftAlone_AndAMissingFolderIsFine()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // A new folder in the user's temporary area is not open to everyone, so there is nothing to change
        if (!HasBroadAccess(_dir))
        {
            Assert.False(PrivateFolders.EnsurePrivate(_dir));
        }

        Assert.False(PrivateFolders.EnsurePrivate(Path.Combine(_dir, "not-there")));
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

    [SupportedOSPlatform("windows")]
    private static System.Collections.Generic.List<SecurityIdentifier> Sids(string path)
    {
        var rules = File.Exists(path)
            ? new FileInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
            : new DirectoryInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));
        return rules.Cast<FileSystemAccessRule>().Select(r => (SecurityIdentifier)r.IdentityReference).ToList();
    }

    [SupportedOSPlatform("windows")]
    private static bool HasBroadAccess(string path)
    {
        var broad = new[] { WellKnownSidType.BuiltinUsersSid, WellKnownSidType.AuthenticatedUserSid, WellKnownSidType.WorldSid }
            .Select(s => new SecurityIdentifier(s, null))
            .ToList();
        return Sids(path).Any(broad.Contains);
    }
}
