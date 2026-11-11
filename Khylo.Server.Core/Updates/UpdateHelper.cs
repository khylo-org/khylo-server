using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Khylo.Server.Core.Updates;

/// <summary>
/// How an update gets installed when Khylo itself runs as a limited Windows account, which can't install anything.
/// <para>
/// Khylo does what it always did (finds the update, downloads it, checks Khylo's signature and the checksum) and then puts
/// the files in a folder (update-stage) and says so. A separate task that runs as the system every few minutes
/// (<c>khylo.exe --apply-update</c>, set up by the installer) finds them, and trusts none of it: it copies them to a
/// folder only the system can open, checks the signature and the checksum again, installs only a build newer than the one
/// installed, works out the file names itself, and only then hands the installer to the system's one-time update task.
/// A Khylo that has been taken over can therefore only ask for a newer, genuine Khylo build to be installed.
/// </para>
/// </summary>
public static class UpdateHelper
{
    /// <summary>The name of the marker that says an update is waiting.</summary>
    public const string ReadyFileName = "ready.json";

    private const int MaxReadyBytes = 4096;
    private const int MaxChecksumListBytes = 1024 * 1024;
    private const int MaxSignatureBytes = 1024;
    private const long MaxInstallerBytes = 1024L * 1024 * 1024;
    private const int DefaultPort = 8096;

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Where an update waits for the helper.
    /// </summary>
    /// <param name="cachePath">Khylo's cache folder.</param>
    /// <returns>The folder.</returns>
    public static string StageFolder(string cachePath) => Path.Combine(cachePath, "update-stage");

    /// <summary>
    /// The entry point of <c>khylo.exe --apply-update --cachedir ...</c>. It runs every few minutes as the system, so with
    /// nothing waiting it ends at once, before anything else starts.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code: 0 when there was nothing to do or the update was handed over.</returns>
    public static int RunFromArguments(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var at = Array.IndexOf(args, "--cachedir");
        var cache = at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        if (string.IsNullOrWhiteSpace(cache))
        {
            return 2;
        }

        if (!File.Exists(Path.Combine(StageFolder(cache), ReadyFileName)))
        {
            return 0;
        }

        return OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess ? Apply(cache) : 3;
    }

    /// <summary>
    /// Does everything the helper checks, on a copy in a folder only it can open. Throws when anything is wrong.
    /// </summary>
    /// <param name="stage">The folder the update waits in.</param>
    /// <param name="work">A folder only the system can open, for the copies.</param>
    /// <param name="installedBuild">The build that is installed now.</param>
    /// <param name="arch">The kind of PC: x64 or arm64.</param>
    /// <param name="publicKeyPem">Khylo's public update key.</param>
    /// <returns>What to install.</returns>
    public static VerifiedUpdate Verify(string stage, string work, string installedBuild, string arch, string publicKeyPem)
    {
        RefuseLinks(stage);

        // The marker is used up first, so an update that fails a check can't be tried again and again
        var readyPath = Path.Combine(stage, ReadyFileName);
        var ready = ReadSmall(readyPath, MaxReadyBytes);
        File.Delete(readyPath);

        Request? request;
        try
        {
            request = JsonSerializer.Deserialize<Request>(ready, _json);
        }
        catch (JsonException)
        {
            request = null;
        }

        var build = request?.Build;
        if (!KhyloReleaseFeed.IsValidTag(build))
        {
            throw new InvalidOperationException("The staged update has no valid build number.");
        }

        // Only forward: an older build that is validly signed would put known holes back
        if (!KhyloReleaseFeed.IsNewer(build!, installedBuild))
        {
            throw new InvalidOperationException($"{build} is not newer than the installed {installedBuild}.");
        }

        var version = build!.StartsWith("DEV-V", StringComparison.OrdinalIgnoreCase) ? build[5..] : build.TrimStart('V', 'v');
        var fileName = $"khylo-server-{version}-windows-{arch}-setup.exe";
        var port = request!.Port is >= 1 and <= 65535 ? request.Port : DefaultPort;

        var sums = CopyLimited(Path.Combine(stage, KhyloReleaseFeed.ChecksumsAsset), Path.Combine(work, KhyloReleaseFeed.ChecksumsAsset), MaxChecksumListBytes);
        var signature = CopyLimited(Path.Combine(stage, KhyloReleaseFeed.SignatureAsset), Path.Combine(work, KhyloReleaseFeed.SignatureAsset), MaxSignatureBytes);
        var setup = Path.Combine(work, fileName);
        CopyLimited(Path.Combine(stage, fileName), setup, MaxInstallerBytes);

        // Checked on the copies: what the server could still change is no longer what is checked
        if (!KhyloUpdateSignature.Verify(File.ReadAllBytes(sums), File.ReadAllBytes(signature), publicKeyPem))
        {
            throw new InvalidOperationException("The checksum list doesn't carry Khylo's signature.");
        }

        var expected = KhyloReleaseFeed.ChecksumFor(File.ReadAllText(sums, Encoding.UTF8), fileName)
            ?? throw new InvalidOperationException("The installer isn't in the signed checksum list.");
        string actual;
        using (var stream = File.OpenRead(setup))
        {
            actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The installer doesn't match its signed checksum.");
        }

        return new VerifiedUpdate(build, fileName, setup, actual, port);
    }

    /// <summary>
    /// Gets the stage folder ready for a new update: it exists, isn't a link to somewhere else, and holds nothing old.
    /// </summary>
    /// <param name="cachePath">Khylo's cache folder.</param>
    /// <returns>The stage folder.</returns>
    public static string PrepareStageFolder(string cachePath)
    {
        var stage = StageFolder(cachePath);
        Directory.CreateDirectory(stage);
        foreach (var old in Directory.EnumerateFiles(stage))
        {
            File.Delete(old);
        }

        return stage;
    }

    /// <summary>
    /// Tells the helper an update is waiting: the checksum list and its signature go in first, and the marker last.
    /// </summary>
    /// <param name="stage">The stage folder (the installer is already in it).</param>
    /// <param name="checksums">The signed checksum list.</param>
    /// <param name="signature">Its signature.</param>
    /// <param name="build">The build.</param>
    /// <param name="port">The port Khylo answers on.</param>
    public static void HandOver(string stage, byte[] checksums, byte[] signature, string build, int port)
    {
        File.WriteAllBytes(Path.Combine(stage, KhyloReleaseFeed.ChecksumsAsset), checksums);
        File.WriteAllBytes(Path.Combine(stage, KhyloReleaseFeed.SignatureAsset), signature);
        var temporary = Path.Combine(stage, ReadyFileName + ".tmp");
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new Request { Build = build, Port = port }));
        File.Move(temporary, Path.Combine(stage, ReadyFileName), true);
    }

    [SupportedOSPlatform("windows")]
    private static int Apply(string cache)
    {
        var stage = StageFolder(cache);
        string? work = null;
        string? installerName = null;
        try
        {
            var installDir = WindowsUpdate.InstalledFolder() ?? throw new InvalidOperationException("Khylo's folder wasn't found.");
            work = WindowsUpdate.CreatePrivateWorkFolder(NullLogger.Instance);
            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            var plan = Verify(stage, work, Khylo.Common.KhyloBuildInfo.Number, arch, KhyloUpdateSignature.PublicKeyPem);
            installerName = plan.FileName;

            // How it went is written where only the system can open it (the stage can be changed by the limited account, and the
            // system must never write into a place it could point somewhere else); Khylo sees the outcome by the build it comes back as
            WindowsUpdate.StartInstaller(work, plan.SetupPath, plan.Sha256, plan.Build, plan.Port, Path.Combine(work, "result.txt"), installDir, NullLogger.Instance);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            if (work is not null)
            {
                TryWrite(Path.Combine(work, "update.log"), $"{DateTime.UtcNow:s} The staged update was refused: {ex.Message}{Environment.NewLine}");
            }

            return 1;
        }
        finally
        {
            CleanStage(stage, installerName);
        }
    }

    // What was staged is used up either way. Only the files the stage is meant to hold are removed, by name, and only from a plain
    // folder: the limited account can replace the folder with a link while the helper works, and the helper must never empty
    // wherever that points.
    private static void CleanStage(string stage, string? installerName)
    {
        try
        {
            RefuseLinks(stage);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        foreach (var name in new[] { ReadyFileName, ReadyFileName + ".tmp", KhyloReleaseFeed.ChecksumsAsset, KhyloReleaseFeed.SignatureAsset, installerName }.OfType<string>())
        {
            TryDelete(Path.Combine(stage, name));
        }
    }

    // The stage can be written by the limited account, which could replace it (or the files in it) with a link to somewhere
    // else, so a link is never followed
    private static void RefuseLinks(string stage)
    {
        var folder = new DirectoryInfo(stage);
        if (!folder.Exists || folder.Attributes.HasFlag(FileAttributes.ReparsePoint) || folder.LinkTarget is not null
            || (folder.Parent is { } parent && (parent.Attributes.HasFlag(FileAttributes.ReparsePoint) || parent.LinkTarget is not null)))
        {
            throw new InvalidOperationException("The stage folder isn't a plain folder.");
        }
    }

    private static byte[] ReadSmall(string path, int maxBytes)
    {
        using var source = OpenPlain(path);
        if (source.Length > maxBytes)
        {
            throw new InvalidOperationException("A staged file is bigger than it should be.");
        }

        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        return buffer.ToArray();
    }

    // A copy with a size limit, made from a file that isn't a link; the copy is what gets checked
    private static string CopyLimited(string from, string to, long maxBytes)
    {
        using var source = OpenPlain(from);
        if (source.Length > maxBytes)
        {
            throw new InvalidOperationException("A staged file is bigger than it should be.");
        }

        using var target = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            copied += read;
            if (copied > maxBytes)
            {
                throw new InvalidOperationException("A staged file is bigger than it should be.");
            }

            target.Write(buffer, 0, read);
        }

        return to;
    }

    private static FileStream OpenPlain(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null)
        {
            throw new InvalidOperationException("A staged file is missing or isn't a plain file.");
        }

        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static void TryWrite(string path, string text)
    {
        try
        {
            File.WriteAllText(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nowhere to say it; the update just didn't happen
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next time
        }
    }

    /// <summary>
    /// What the helper settled on installing, after checking everything itself.
    /// </summary>
    /// <param name="Build">The build to install.</param>
    /// <param name="FileName">The installer's file name (worked out here, never taken from the stage).</param>
    /// <param name="SetupPath">Where the installer's copy is.</param>
    /// <param name="Sha256">The installer's checksum, as it is in Khylo's signed list.</param>
    /// <param name="Port">The port Khylo answers on, to check it started.</param>
    public sealed record VerifiedUpdate(string Build, string FileName, string SetupPath, string Sha256, int Port);

    private sealed class Request
    {
        public string? Build { get; set; }

        public int Port { get; set; }
    }
}
