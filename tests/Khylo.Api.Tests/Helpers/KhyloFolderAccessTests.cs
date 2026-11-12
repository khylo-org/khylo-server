using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Khylo.Controller.IO;
using Xunit;

namespace Khylo.Api.Tests.Helpers;

public sealed class KhyloFolderAccessTests : IDisposable
{
    private static readonly KhyloFolderAccess.Places _pc = new(@"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData", @"C:\Users", @"E:\Apps\Khylo");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "khylo-folder-access-" + Guid.NewGuid().ToString("N"));

    public KhyloFolderAccessTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Icacls(_folder, "/remove:d", Environment.UserName, "/T", "/C", "/Q");
        }

        Directory.Delete(_folder, true);
    }

    [Theory]
    [InlineData(@"D:\DFS01")]
    [InlineData(@"D:\DFS01\Movies")]
    [InlineData(@"D:\")]
    [InlineData(@"E:\Media\TV Shows\")]
    [InlineData(@"C:\Media")]
    [InlineData(@"C:\Users\someone\Videos")]
    [InlineData(@"C:\Users\someone\Videos\Home Movies")]
    public void MediaFolders_MayBeOpenedToKhylo(string path)
    {
        Assert.Null(KhyloFolderAccess.WhyNot(path, _pc));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Movies")]
    [InlineData(@"\\nas\Movies")]
    [InlineData(@"\\?\D:\Movies")]
    [InlineData(@"D:Movies")]
    [InlineData(@"D:\Movies\..\..\Windows")]
    [InlineData(@"D:\Movies\.")]
    [InlineData(@"D:\Movies:stream")]
    [InlineData("D:\\Movies\" /grant Everyone:F \"")]
    [InlineData(@"D:/Movies")]
    [InlineData(@"C:\")]
    [InlineData(@"C:")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"c:\windows\System32\config")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files\Khylo")]
    [InlineData(@"C:\Program Files (x86)\Something")]
    [InlineData(@"C:\ProgramData")]
    [InlineData(@"C:\ProgramData\Khylo\data")]
    [InlineData(@"C:\ProgramData\KhyloUpdate")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Users\someone")]
    [InlineData(@"C:\Users\someone\AppData\Roaming")]
    [InlineData(@"D:\$Recycle.Bin")]
    [InlineData(@"D:\System Volume Information")]
    [InlineData(@"E:\Apps\Khylo")]
    [InlineData(@"E:\Apps\Khylo\web")]
    [InlineData(@"E:\Apps")]
    [InlineData(@"E:\")]
    public void EverythingElse_IsRefused_WithAReason(string? path)
    {
        Assert.False(string.IsNullOrWhiteSpace(KhyloFolderAccess.WhyNot(path, _pc)));
    }

    [Fact]
    public void AFolderWhoseNameOnlyStartsLikeWindows_IsNotMistakenForIt()
    {
        Assert.Null(KhyloFolderAccess.WhyNot(@"C:\WindowsMedia", _pc));
        Assert.Null(KhyloFolderAccess.WhyNot(@"C:\Users2\Movies", _pc));
    }

    [Fact]
    public void ARequest_IsReadOnce_ThenGone()
    {
        KhyloFolderAccess.Ask(_folder, @"D:\DFS01", true);

        var taken = KhyloFolderAccess.TakeRequest(_folder);

        Assert.Equal((@"D:\DFS01", true), taken);
        Assert.Null(KhyloFolderAccess.TakeRequest(_folder));
        Assert.False(File.Exists(Path.Combine(KhyloFolderAccess.StageFolder(_folder), KhyloFolderAccess.RequestFileName)));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"Path\":\"  \"}")]
    public void ARequestThatMakesNoSense_IsThrownAway(string text)
    {
        var stage = KhyloFolderAccess.StageFolder(_folder);
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, KhyloFolderAccess.RequestFileName), text);

        Assert.Null(KhyloFolderAccess.TakeRequest(_folder));
        Assert.False(File.Exists(Path.Combine(stage, KhyloFolderAccess.RequestFileName)));
    }

    [Fact]
    public void ARequestThatIsFarTooBig_IsThrownAway()
    {
        var stage = KhyloFolderAccess.StageFolder(_folder);
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, KhyloFolderAccess.RequestFileName), "{\"Path\":\"D:\\\\" + new string('a', 9000) + "\"}");

        Assert.Null(KhyloFolderAccess.TakeRequest(_folder));
    }

    [Fact]
    public void WithNothingWaiting_TheHelperEndsAtOnce()
    {
        Assert.Equal(0, KhyloFolderAccess.RunFromArguments(["--apply-update", "--cachedir", _folder]));
        Assert.Equal(0, KhyloFolderAccess.RunFromArguments(["--apply-update"]));
    }

    [Fact]
    public void OnlyTheSystem_ActsOnARequest()
    {
        if (OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess)
        {
            return;
        }

        KhyloFolderAccess.Ask(_folder, @"D:\DFS01", false);

        Assert.Equal(3, KhyloFolderAccess.RunFromArguments(["--apply-update", "--cachedir", _folder]));
    }

    // The bug: Khylo, running as the limited account, showed a folder it wasn't allowed to open as an empty folder. This is that
    // case on a real folder: the listing the folder picker uses comes back empty without a word, and CanOpen is what tells it apart.
    [Fact]
    public void AFolderWindowsWontLetKhyloOpen_IsToldApartFromAnEmptyOne()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var media = Path.Combine(_folder, "DFS01");
        Directory.CreateDirectory(Path.Combine(media, "Movies"));
        Directory.CreateDirectory(Path.Combine(media, "TV Shows"));
        Assert.True(KhyloFolderAccess.CanOpen(media));

        Icacls(media, "/deny", Environment.UserName + ":(RD)", "/Q");

        var whatThePickerLists = new DirectoryInfo(media).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 });
        Assert.Empty(whatThePickerLists);
        Assert.False(KhyloFolderAccess.CanOpen(media));

        Icacls(media, "/remove:d", Environment.UserName, "/Q");
        Assert.True(KhyloFolderAccess.CanOpen(media));
    }

    [Theory]
    [InlineData(KhyloFolderAccess.InstallKind.Windows, @"D:\DFS01", @"NT AUTHORITY\LOCAL SERVICE", "Properties, Security")]
    [InlineData(KhyloFolderAccess.InstallKind.Linux, "/mnt/media", "khylo", "sudo setfacl -R -m u:khylo:rX \"/mnt/media\"")]
    [InlineData(KhyloFolderAccess.InstallKind.Proxmox, "/media/movies", "khylo", "On the Proxmox host")]
    [InlineData(KhyloFolderAccess.InstallKind.Docker, "/media/movies", "khylo", "docker-compose.yml")]
    [InlineData(KhyloFolderAccess.InstallKind.Mac, "/Volumes/Media", "dan", "Full Disk Access")]
    public void WhatToDo_IsSaidInTheWordsOfThatKindOfServer(KhyloFolderAccess.InstallKind kind, string path, string account, string expected)
    {
        var steps = KhyloFolderAccess.HowToFix(kind, path, account);

        Assert.Contains(expected, steps, StringComparison.Ordinal);
        Assert.Contains(account, steps, StringComparison.Ordinal);
        if (kind != KhyloFolderAccess.InstallKind.Windows)
        {
            Assert.DoesNotContain("Windows", steps, StringComparison.Ordinal);
            Assert.DoesNotContain("right-click", steps, StringComparison.Ordinal);
        }
    }

    // A folder below one Khylo can't even look into must read as "not allowed", not as "missing"
    [Fact]
    public void AFolderBelowOneKhyloCantLookInto_IsNotAllowed_NotMissing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var locked = Path.Combine(_folder, "Locked");
        Directory.CreateDirectory(Path.Combine(locked, "Movies"));
        Icacls(locked, "/deny", Environment.UserName + ":(RD)", "/Q");

        Assert.False(KhyloFolderAccess.CanOpen(locked));
        Assert.True(KhyloFolderAccess.CanOpen(Path.Combine(_folder, "Nowhere", "Movies")));

        Icacls(locked, "/remove:d", Environment.UserName, "/Q");
    }

    [Fact]
    public void AnEmptyFolder_AndAMissingOne_CanBeOpened()
    {
        Assert.True(KhyloFolderAccess.CanOpen(_folder));
        Assert.True(KhyloFolderAccess.CanOpen(Path.Combine(_folder, "not-there")));
        Assert.True(KhyloFolderAccess.CanOpen(null));
    }

    // The very command the helper runs, on a real folder: afterwards Windows lists the limited account on the folder and on what
    // is inside it, with the right kind of access, and whoever could open the folder before still can.
    [Theory]
    [InlineData(false, "(RX)")]
    [InlineData(true, "(M)")]
    public void TheGrant_GivesTheLimitedAccountTheFolderAndWhatIsInside(bool allowChanges, string expected)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var media = Path.Combine(_folder, "Media Folder");
        var inside = Path.Combine(media, "Movies", "A Film (2020)");
        Directory.CreateDirectory(inside);
        File.WriteAllText(Path.Combine(inside, "film.mkv"), "x");
        Assert.DoesNotContain("LOCAL SERVICE", Icacls(media), StringComparison.OrdinalIgnoreCase);

        var (exit, _) = Run([.. KhyloFolderAccess.GrantArguments(media, allowChanges)]);

        Assert.Equal(0, exit);
        var folderLine = Icacls(media).Split('\n').Single(line => line.Contains("LOCAL SERVICE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("(OI)(CI)" + expected, folderLine, StringComparison.Ordinal);
        var fileLine = Icacls(Path.Combine(inside, "film.mkv")).Split('\n').Single(line => line.Contains("LOCAL SERVICE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("(I)" + expected, fileLine, StringComparison.Ordinal);
        Assert.True(KhyloFolderAccess.CanOpen(media));
        Assert.True(KhyloFolderAccess.CanChange(media));
    }

    [Fact]
    public void CanChange_SaysNo_WhereFilesCantBeAdded()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var media = Path.Combine(_folder, "ReadOnly");
        Directory.CreateDirectory(media);
        Assert.True(KhyloFolderAccess.CanChange(media));
        Assert.Empty(Directory.EnumerateFileSystemEntries(media));

        Icacls(media, "/deny", Environment.UserName + ":(WD)", "/Q");

        Assert.False(KhyloFolderAccess.CanChange(media));
        Assert.True(KhyloFolderAccess.CanOpen(media));
    }

    private static string Icacls(params string[] arguments) => Run(arguments).Output;

    private static (int Exit, string Output) Run(string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "icacls.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
