using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Khylo.Server.Core.Updates;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Updates;

public sealed class UpdateHelperTests : IDisposable
{
    private const string Newer = "DEV-V26.11.09.02";
    private const string Installed = "DEV-V26.11.09.01";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "khylo-helper-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa _khylo = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _stage;
    private readonly string _work;

    public UpdateHelperTests()
    {
        _stage = UpdateHelper.PrepareStageFolder(Path.Combine(_root, "cache"));
        _work = Path.Combine(_root, "work");
        Directory.CreateDirectory(_work);
    }

    private string Key => _khylo.ExportSubjectPublicKeyInfoPem();

    [Fact]
    public void AGenuineNewerBuild_IsAccepted_AndTheMarkerIsUsedUp()
    {
        var installer = Stage(Newer);

        var plan = UpdateHelper.Verify(_stage, _work, Installed, "x64", Key);

        Assert.Equal(Newer, plan.Build);
        Assert.Equal("khylo-server-26.11.09.02-windows-x64-setup.exe", plan.FileName);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant(), plan.Sha256);
        Assert.Equal(8096, plan.Port);
        Assert.Equal(installer, File.ReadAllBytes(plan.SetupPath));
        Assert.StartsWith(_work, plan.SetupPath, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_stage, UpdateHelper.ReadyFileName)));
    }

    [Theory]
    [InlineData("DEV-V26.11.09.01")]
    [InlineData("DEV-V26.11.08.11")]
    [InlineData("DEV-V25.01.01.01")]
    public void ABuildThatIsNotNewer_IsRefused_EvenWhenGenuinelySigned(string build)
    {
        Stage(build);

        var failure = Assert.Throws<InvalidOperationException>(() => UpdateHelper.Verify(_stage, _work, Installed, "x64", Key));

        Assert.Contains("not newer", failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_stage, UpdateHelper.ReadyFileName)));
    }

    [Fact]
    public void AChecksumListSignedWithAnotherKey_IsRefused()
    {
        Stage(Newer);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Throws<InvalidOperationException>(() => UpdateHelper.Verify(_stage, _work, Installed, "x64", other.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public void AnInstallerThatWasChangedAfterTheListWasSigned_IsRefused()
    {
        Stage(Newer);
        var path = Path.Combine(_stage, "khylo-server-26.11.09.02-windows-x64-setup.exe");
        var bytes = File.ReadAllBytes(path);
        bytes[10] ^= 1;
        File.WriteAllBytes(path, bytes);

        var failure = Assert.Throws<InvalidOperationException>(() => UpdateHelper.Verify(_stage, _work, Installed, "x64", Key));

        Assert.Contains("signed checksum", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInstallerThatIsNotInTheSignedList_IsRefused()
    {
        Stage(Newer, listedName: "khylo-server-26.11.09.02-windows-arm64-setup.exe");

        Assert.Throws<InvalidOperationException>(() => UpdateHelper.Verify(_stage, _work, Installed, "x64", Key));
    }

    [Theory]
    [InlineData("DEV-V26.11.09.02'; Remove-Item C:\\ -Recurse; '")]
    [InlineData("DEV-V26.11.09.02\\..\\..\\evil")]
    [InlineData("latest")]
    [InlineData("")]
    public void ABuildNameThatIsNotABuildNumber_IsRefused(string build)
    {
        UpdateHelper.HandOver(_stage, [1], [2], build, 8096);

        Assert.Throws<InvalidOperationException>(() => UpdateHelper.Verify(_stage, _work, Installed, "x64", Key));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    [InlineData(-5)]
    public void ANonsensePort_BecomesTheUsualOne(int port)
    {
        Stage(Newer, port: port);

        Assert.Equal(8096, UpdateHelper.Verify(_stage, _work, Installed, "x64", Key).Port);
    }

    [Fact]
    public void AGoodPort_IsKept()
    {
        Stage(Newer, port: 8123);

        Assert.Equal(8123, UpdateHelper.Verify(_stage, _work, Installed, "x64", Key).Port);
    }

    [Fact]
    public void FilesThatAreTooBig_OrMissing_AreRefused()
    {
        Stage(Newer);
        File.WriteAllBytes(Path.Combine(_stage, "SHA256SUMS.txt"), new byte[(1024 * 1024) + 1]);
        Assert.Throws<InvalidOperationException>(() => UpdateHelper.Verify(_stage, _work, Installed, "x64", Key));

        var again = new UpdateHelperTests();
        again.Stage(Newer);
        File.Delete(Path.Combine(again._stage, "SHA256SUMS.txt.sig"));
        Assert.Throws<InvalidOperationException>(() => UpdateHelper.Verify(again._stage, again._work, Installed, "x64", again.Key));
        again.Dispose();
    }

    [Fact]
    public void TheInstallerNameIsWorkedOutFromTheBuild_NotTakenFromTheStage()
    {
        // Only an installer for the other kind of PC is staged
        Stage(Newer, arch: "arm64");

        Assert.Throws<InvalidOperationException>(() => UpdateHelper.Verify(_stage, _work, Installed, "x64", Key));
    }

    [Fact]
    public void PreparingTheStage_ClearsOldFiles()
    {
        File.WriteAllText(Path.Combine(_stage, "old-installer.exe"), "x");
        File.WriteAllText(Path.Combine(_stage, "SHA256SUMS.txt"), "x");

        UpdateHelper.PrepareStageFolder(Path.Combine(_root, "cache"));

        Assert.Empty(Directory.GetFiles(_stage));
    }

    [Fact]
    public void WithNothingWaiting_TheHelperEndsAtOnce_AndTouchesNothing()
    {
        File.WriteAllText(Path.Combine(_stage, "somebody-elses-file.txt"), "x");
        var before = Directory.GetFiles(_stage).Length;

        Assert.Equal(0, UpdateHelper.RunFromArguments(["--apply-update", "--cachedir", Path.Combine(_root, "cache")]));
        Assert.Equal(2, UpdateHelper.RunFromArguments(["--apply-update"]));
        Assert.Equal(before, Directory.GetFiles(_stage).Length);
    }

    [Fact]
    public void ARealUpdate_IsNeverStartedFromAnOrdinaryTestRun()
    {
        Stage(Newer);

        // Not elevated: refused (3). Elevated: this isn't an installed copy, so it fails its own check (1). Either way nothing is installed
        var code = UpdateHelper.RunFromArguments(["--apply-update", "--cachedir", Path.Combine(_root, "cache")]);

        Assert.Contains(code, new[] { 1, 3 });
    }

    public void Dispose()
    {
        _khylo.Dispose();
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // A temporary folder that could not be removed is not a failed test
        }
    }

    // What the server puts in the stage: the installer, the signed checksum list, and the marker last
    private byte[] Stage(string build, string? listedName = null, string arch = "x64", int port = 8096)
    {
        var version = build[5..];
        var fileName = $"khylo-server-{version}-windows-{arch}-setup.exe";
        var installer = RandomNumberGenerator.GetBytes(4096);
        File.WriteAllBytes(Path.Combine(_stage, fileName), installer);
        var sums = Encoding.UTF8.GetBytes($"{Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant()}  {listedName ?? fileName}\n");
        var signature = _khylo.SignData(sums, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        UpdateHelper.HandOver(_stage, sums, signature, build, port);
        return installer;
    }
}
