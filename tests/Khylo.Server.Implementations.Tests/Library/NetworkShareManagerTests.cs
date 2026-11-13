using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Controller.Library;
using Khylo.Server.Core.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Library;

public sealed class NetworkShareManagerTests : IDisposable
{
    private const string Host = "khylo-test-no-such-server.invalid";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "khylo-shares-" + Guid.NewGuid().ToString("N"));

    public NetworkShareManagerTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Icacls(_folder, "/remove:d", Environment.UserDomainName + "\\" + Environment.UserName, "/T", "/C", "/Q");
        }

        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
            // Left behind in the temp folder
        }
    }

    private static void Icacls(params string[] args)
    {
        var info = new ProcessStartInfo("icacls") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        using var p = Process.Start(info)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
    }

    private static string WhyWontOpen(string address)
        => (string)typeof(NetworkShareManager).GetMethod("WhyFolderWontOpen", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [address])!;

    private NetworkShareManager ManagerWith(params (string Name, string Address, string Username)[] stored)
    {
        var shares = Array.ConvertAll(stored, s => new
        {
            Id = Guid.NewGuid(),
            Name = s.Name,
            Kind = "smb",
            Host = s.Address.TrimStart('\\').Split('\\')[0],
            Remote = s.Address.TrimStart('\\').Split('\\')[1],
            Address = s.Address,
            Username = s.Username,
            Password = (string?)null,
            ReadOnly = true,
            Path = s.Address
        });
        File.WriteAllText(Path.Combine(_folder, "khylo_network_shares.json"), JsonSerializer.Serialize(shares));

        var paths = new Mock<IApplicationPaths>();
        paths.Setup(p => p.ConfigurationDirectoryPath).Returns(_folder);
        paths.Setup(p => p.DataPath).Returns(_folder);
        return new NetworkShareManager(paths.Object, NullLogger<NetworkShareManager>.Instance);
    }

    private static AddNetworkShareRequest Add(string address, string? user = "bb\\someone", string? password = "pw")
        => new() { Kind = "smb", Address = address, Username = user, Password = password, ReadOnly = true };

    // The bug: "Add a network share" said the share was "already connected" and offered no way to fix the saved login
    [Fact]
    public async Task AddingAShareKhyloHas_WithTheDotSpelledTheOtherWay_IsTheSameShare_NotANewOneAndNotARefusal()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows connects shares itself");
        using var manager = ManagerWith(("dfs01", $@"\\{Host}\dfs01", "bb\\old"));

        var ex = await Assert.ThrowsAsync<NetworkShareException>(() => manager.AddShareAsync(Add($@"\\{Host}.\dfs01"), CancellationToken.None));

        // It tried to connect (to a server that doesn't exist); it did not say "already connected", and nothing was added
        Assert.DoesNotContain("already connected", ex.Message, StringComparison.OrdinalIgnoreCase);
        var shares = manager.GetShares();
        Assert.Single(shares);

        // The connection failed, so the saved login is unchanged
        Assert.Equal("bb\\old", shares[0].Username);
    }

    [Fact]
    public async Task BothSpellingsSaved_AreBothGivenTheNewLoginTogether_AndKeptWhenItFails()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows connects shares itself");
        using var manager = ManagerWith(("dfs01", $@"\\{Host}\dfs01", "bb\\old"), ("dfs01 2", $@"\\{Host}.\dfs01", "bb\\old"));

        await Assert.ThrowsAsync<NetworkShareException>(() => manager.AddShareAsync(Add($@"\\{Host}.\dfs01", "bb\\new"), CancellationToken.None));

        var shares = manager.GetShares();
        Assert.Equal(2, shares.Count);
        Assert.All(shares, s => Assert.Equal("bb\\old", s.Username));
    }

    [Fact]
    public async Task ADifferentShare_IsStillAddedAsNew_NotTakenForTheOne()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows connects shares itself");
        using var manager = ManagerWith(("dfs01", $@"\\{Host}\dfs01", "bb\\old"));

        // It does not connect (no such server), but it must not be mistaken for dfs01, so the failure is about connecting, not about a login being updated
        await Assert.ThrowsAsync<NetworkShareException>(() => manager.AddShareAsync(Add($@"\\{Host}\music"), CancellationToken.None));

        var shares = manager.GetShares();
        Assert.Single(shares);
        Assert.Equal("bb\\old", shares[0].Username);
    }

    // The bug: access refused by Windows was reported as "there's no folder"
    [Fact]
    public void AFolderWindowsRefuses_IsReportedAsRefused_NotAsMissing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Uses Windows permissions");
        var locked = Path.Combine(_folder, "locked");
        Directory.CreateDirectory(locked);
        Icacls(locked, "/deny", Environment.UserDomainName + "\\" + Environment.UserName + ":(R,RD)");

        var message = WhyWontOpen(locked);

        Assert.Contains("refused access", message, StringComparison.Ordinal);
        Assert.DoesNotContain("no folder", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderThatIsReallyMissing_SaysSo()
    {
        var message = WhyWontOpen(Path.Combine(_folder, "nothing-here"));

        Assert.Contains("no folder", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADottedServerName_IsNamedAsTheLikelyCause_AndTheFixIsGiven()
    {
        var message = WhyWontOpen($@"\\{Host}.\dfs01\Movies");

        Assert.Contains("dot after the server's name", message, StringComparison.Ordinal);
        Assert.Contains($@"\\{Host}\dfs01\Movies", message, StringComparison.Ordinal);
    }

    [Fact]
    public void APlainServerName_GetsNoAdviceAboutADot()
    {
        var message = WhyWontOpen($@"\\{Host}\dfs01\Movies");

        Assert.DoesNotContain("dot after", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDashboardsStatus_CarriesTheReasonAndTheNote_ForAnUnreachableDottedLibrary()
    {
        var path = $@"\\{Host}.\dfs01\Movies";
        StorageHealth.Keep([path]);
        StorageHealth.Record("Movies", path, false);
        StorageHealth.SetShareProblem($@"\\{Host}\dfs01", "Windows refused it");

        var status = StorageHealth.Snapshot();
        var mine = Assert.Single(status, s => s.Path == path);

        Assert.Equal("Windows refused it", mine.Reason);
        Assert.Contains("dot after the server's name", mine.Note, StringComparison.Ordinal);

        // Once the share works the reason goes
        StorageHealth.SetShareProblem($@"\\{Host}\dfs01", null);
        StorageHealth.Record("Movies", path, true);
        Assert.Null(Assert.Single(StorageHealth.Snapshot(), s => s.Path == path).Reason);
        StorageHealth.Keep([]);
    }
}
