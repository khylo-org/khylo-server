using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;
using System.Text.Json;

namespace Khylo.Controller.IO;

/// <summary>
/// Lets a Khylo that runs as the limited Windows account (LocalService) be given access to a media folder from Khylo's own
/// pages, instead of the owner changing Windows permissions by hand.
/// <para>
/// The limited account can't change who may open a folder, so the server only writes down a request (in its cache folder).
/// The helper task the installer sets up, which runs as the system, finds it and trusts none of it: it checks the folder
/// itself (a real folder on a drive of this PC, never one of Windows' own or a person's profile) and only then lets the
/// limited account read it (or change it, when asked). A Khylo that has been taken over can therefore ask for media-style
/// folders to be opened to it, but never for Windows, the programs, other people's settings, or Khylo's own program.
/// </para>
/// </summary>
public static class KhyloFolderAccess
{
    /// <summary>The Windows name of the limited account, as a security ID (LocalService).</summary>
    public const string LimitedAccountSid = "S-1-5-19";

    /// <summary>The name of the file a request waits in.</summary>
    public const string RequestFileName = "request.json";

    private const int MaxRequestBytes = 4096;
    private const int WaitForWindowsMinutes = 8;

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// How a Khylo server is installed, as far as it matters for who may read a folder.
    /// </summary>
    public enum InstallKind
    {
        /// <summary>The Windows installer.</summary>
        Windows,

        /// <summary>The Linux package, run by systemd as the khylo user.</summary>
        Linux,

        /// <summary>The Proxmox container.</summary>
        Proxmox,

        /// <summary>The Docker image.</summary>
        Docker,

        /// <summary>The Mac package.</summary>
        Mac
    }

    /// <summary>
    /// Where a request waits for the helper.
    /// </summary>
    /// <param name="cachePath">Khylo's cache folder.</param>
    /// <returns>The folder.</returns>
    public static string StageFolder(string cachePath) => Path.Combine(cachePath, "access-stage");

    /// <summary>
    /// Says whether Khylo can list what is in a folder. A folder that Windows won't let Khylo open counts as "no"; a folder
    /// that is missing, or anything else, counts as "yes" (those are reported the way they always were).
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <returns>False only when Windows refuses.</returns>
    public static bool CanOpen(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        try
        {
            if (!Directory.Exists(path))
            {
                // A folder Khylo may not even look at reports as missing; the folders above it tell the two apart
                return !HiddenFromKhylo(path);
            }

            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            entries.MoveNext();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return true;
        }
    }

    /// <summary>
    /// Says whether Khylo can add and remove files in a folder (needed for recordings and for deleting from Khylo).
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <returns>True when a file could be made and removed.</returns>
    public static bool CanChange(string path)
    {
        try
        {
            var probe = Path.Combine(path, ".khylo-access-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Says whether this Khylo runs as the limited Windows account.
    /// </summary>
    /// <returns>True for LocalService on Windows.</returns>
    public static bool RunsAsLimitedAccount()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return string.Equals(identity.User?.Value, LimitedAccountSid, StringComparison.Ordinal);
    }

    /// <summary>
    /// The name of the Windows account Khylo runs as, for messages.
    /// </summary>
    /// <returns>The name, or null when this isn't Windows.</returns>
    public static string? AccountName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return identity.Name;
    }

    /// <summary>
    /// The name of the account Khylo runs as on this system, for messages.
    /// </summary>
    /// <returns>The Windows account, or the user's name elsewhere.</returns>
    public static string RunsAs() => AccountName() ?? Environment.UserName;

    /// <summary>
    /// Works out how this Khylo was installed, as far as it matters for who may read a folder.
    /// </summary>
    /// <returns>The kind of install.</returns>
    public static InstallKind HowKhyloRuns()
    {
        if (OperatingSystem.IsWindows())
        {
            return InstallKind.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return InstallKind.Mac;
        }

        if (File.Exists("/.dockerenv") || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            return InstallKind.Docker;
        }

        try
        {
            // What the Proxmox installer makes is an LXC container, which says so here
            if (File.Exists("/run/systemd/container") && File.ReadAllText("/run/systemd/container").Contains("lxc", StringComparison.OrdinalIgnoreCase))
            {
                return InstallKind.Proxmox;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Then it is treated as plain Linux
        }

        return InstallKind.Linux;
    }

    /// <summary>
    /// What the owner has to do so Khylo can open a folder, for the way this Khylo is installed.
    /// </summary>
    /// <param name="kind">How Khylo is installed.</param>
    /// <param name="path">The folder, as Khylo sees it.</param>
    /// <param name="account">The account or user Khylo runs as.</param>
    /// <returns>The steps, in words for the owner.</returns>
    public static string HowToFix(InstallKind kind, string path, string account) => kind switch
    {
        InstallKind.Windows => $"In Windows, give \"{account}\" read access to the folder (right-click it, Properties, Security, Edit, Add), then press refresh here. " +
            "Or run Khylo's setup again and choose to run Khylo as the system.",
        InstallKind.Mac => "macOS protects this folder. Open System Settings > Privacy & Security > Full Disk Access, add /usr/local/khylo/khylo and turn it on, " +
            "then restart Khylo (sudo launchctl kickstart -k system/org.khylo.server). If it is already on, the folder's own permissions don't let " +
            $"\"{account}\" read it.",
        InstallKind.Docker => $"Khylo runs inside Docker as the user \"{account}\" (ID {UserId()}), and {path} is a folder you passed into the container. " +
            "On the computer that runs Docker, make the folder readable by everyone (chmod -R a+rX on it), or run the container as the user who owns " +
            "the media (add user: \"UID:GID\" to docker-compose.yml, and make /config and /cache belong to that user). Then press refresh here.",
        InstallKind.Proxmox => $"{path} comes from the Proxmox host, and the container's user \"{account}\" isn't allowed to read it. " +
            "On the Proxmox host, make the folder readable by everyone (chmod -R a+rX on it). Then press refresh here.",
        _ => $"Khylo runs as the user \"{account}\", which isn't allowed to read this folder. On the server, run: sudo setfacl -R -m u:{account}:rX \"{path}\" " +
            $"(or add {account} to the group that owns your media: sudo usermod -aG GROUP {account}, then sudo systemctl restart khylo). " +
            "Every folder above it must let that user through as well. Then press refresh here."
    };

    /// <summary>
    /// Says whether the helper that grants access is there: the installer set it up and Khylo runs as the limited account.
    /// </summary>
    /// <returns>True when a request would be picked up.</returns>
    public static bool HelperAvailable()
    {
        if (!OperatingSystem.IsWindows() || !RunsAsLimitedAccount())
        {
            return false;
        }

        return ReadSetting("UpdateHelper") is int and 1;
    }

    /// <summary>
    /// Says why a folder may not be opened to Khylo this way, or null when it may. The same check runs in the server (to say
    /// so at once) and again in the helper (which believes nothing the server tells it).
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <param name="places">Where Windows and its people's folders are on this PC.</param>
    /// <returns>The reason, in words for the owner, or null.</returns>
    public static string? WhyNot(string? path, Places places)
    {
        ArgumentNullException.ThrowIfNull(places);
        if (string.IsNullOrWhiteSpace(path) || path.Length > 400 || path.IndexOfAny(['"', '<', '>', '|', '*', '?', '\0', '/']) >= 0)
        {
            return "That isn't a folder on this PC.";
        }

        // A drive letter, a colon and a backslash: nothing on the network, no device names, nothing relative
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\' || path.IndexOf(':', 2) >= 0)
        {
            return "Khylo can only be given access to a folder on one of this PC's own drives. A network folder uses the login you give Khylo for it.";
        }

        var parts = path[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')))
        {
            return "That isn't a folder on this PC.";
        }

        var full = Trim(path);
        if (full.Length == 2 && SameDrive(full, places.Windows))
        {
            return "That is the whole drive Windows is on. Pick the folder your media is in.";
        }

        if (parts.Any(part => part.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)
            || part.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)))
        {
            return "That folder belongs to Windows.";
        }

        foreach (var system in new[] { places.Windows, places.ProgramFiles, places.ProgramFilesX86, places.ProgramData }.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (IsAtOrUnder(full, Trim(system!)))
            {
                return "That folder belongs to Windows or to the programs on this PC, so Khylo isn't given access to it. Keep your media in a folder of its own.";
            }
        }

        // Khylo's own program is run by the system (the helper), so the limited account must never be let into it
        if (!string.IsNullOrWhiteSpace(places.Khylo) && (IsAtOrUnder(full, Trim(places.Khylo)) || IsAtOrUnder(Trim(places.Khylo), full)))
        {
            return "That folder is, or holds, Khylo's own program. Keep your media in a folder of its own.";
        }

        if (!string.IsNullOrWhiteSpace(places.Users))
        {
            var users = Trim(places.Users);
            if (IsAtOrUnder(full, users))
            {
                var inside = full.Length > users.Length ? full[(users.Length + 1)..].Split('\\') : [];
                if (inside.Length < 2)
                {
                    return "That is a person's whole Windows profile. Pick the folder inside it that your media is in (for example Videos).";
                }

                if (inside.Skip(1).Any(part => part.Equals("AppData", StringComparison.OrdinalIgnoreCase)))
                {
                    return "That folder holds a person's program settings, so Khylo isn't given access to it.";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Writes down a request for the helper. The folder must already have passed <see cref="WhyNot"/>.
    /// </summary>
    /// <param name="cachePath">Khylo's cache folder.</param>
    /// <param name="path">The folder to open to Khylo.</param>
    /// <param name="allowChanges">Whether Khylo may also add, change and remove files in it.</param>
    public static void Ask(string cachePath, string path, bool allowChanges)
    {
        var stage = StageFolder(cachePath);
        Directory.CreateDirectory(stage);
        var temporary = Path.Combine(stage, RequestFileName + ".tmp");
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new Request { Path = path, AllowChanges = allowChanges }));
        File.Move(temporary, Path.Combine(stage, RequestFileName), true);
    }

    /// <summary>
    /// Reads the waiting request and uses it up, so a request that is refused isn't tried again and again.
    /// </summary>
    /// <param name="cachePath">Khylo's cache folder.</param>
    /// <returns>The folder asked for and whether changes were asked for, or null when nothing (usable) waits.</returns>
    public static (string Path, bool AllowChanges)? TakeRequest(string cachePath)
    {
        var stage = new DirectoryInfo(StageFolder(cachePath));
        var file = new FileInfo(Path.Combine(stage.FullName, RequestFileName));
        if (!stage.Exists || !file.Exists)
        {
            return null;
        }

        // The limited account can write here, and could leave a link to somewhere else: a link is never followed
        if (IsLink(stage) || (stage.Parent is { } parent && IsLink(parent)) || IsLink(file))
        {
            return null;
        }

        byte[] bytes;
        using (var source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (source.Length > MaxRequestBytes)
            {
                bytes = [];
            }
            else
            {
                bytes = new byte[source.Length];
                source.ReadExactly(bytes);
            }
        }

        file.Delete();
        try
        {
            var request = JsonSerializer.Deserialize<Request>(bytes, _json);
            return string.IsNullOrWhiteSpace(request?.Path) ? null : (request.Path, request.AllowChanges);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The helper's part, run as the system by <c>khylo.exe --apply-update</c>: with nothing waiting it ends at once.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <returns>0 when there was nothing to do or access was given.</returns>
    public static int RunFromArguments(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var at = Array.IndexOf(args, "--cachedir");
        var cache = at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        if (string.IsNullOrWhiteSpace(cache) || !File.Exists(Path.Combine(StageFolder(cache), RequestFileName)))
        {
            return 0;
        }

        if (!OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess)
        {
            return 3;
        }

        try
        {
            return Apply(cache) ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return 1;
        }
    }

    /// <summary>
    /// Where Windows keeps itself, its programs and its people on this PC.
    /// </summary>
    /// <returns>The places.</returns>
    public static Places PlacesOnThisPc()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var profiles = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory).TrimEnd('\\'));
        profiles = string.IsNullOrEmpty(profiles) ? null : Path.GetDirectoryName(profiles);
        return new Places(
            windows,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            profiles ?? (windows.Length >= 2 ? windows[..2] + @"\Users" : null),
            (OperatingSystem.IsWindows() ? ReadSetting("InstallDir") as string : null) ?? AppContext.BaseDirectory);
    }

    /// <summary>
    /// The arguments for Windows' own permissions tool: the limited account may read the folder and everything in it (or
    /// change it), and nothing else about the folder is touched.
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <param name="allowChanges">Whether to allow changes too.</param>
    /// <returns>The arguments, one per entry.</returns>
    public static string[] GrantArguments(string path, bool allowChanges)
        => [path, "/grant", string.Create(CultureInfo.InvariantCulture, $"*{LimitedAccountSid}:(OI)(CI){(allowChanges ? "M" : "RX")}"), "/C", "/Q"];

    [SupportedOSPlatform("windows")]
    private static bool Apply(string cache)
    {
        if (TakeRequest(cache) is not { } request)
        {
            return false;
        }

        // Only for the limited account the installer set Khylo up with: any other way of running has no use for this
        if (!string.Equals(ReadSetting("RunMode") as string, "limited", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (WhyNot(request.Path, PlacesOnThisPc()) is not null)
        {
            return false;
        }

        var folder = new DirectoryInfo(Trim(request.Path) + (Trim(request.Path).Length == 2 ? @"\" : string.Empty));
        if (!folder.Exists || !string.Equals(Trim(folder.FullName), Trim(request.Path), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A real folder all the way up (no links to somewhere else), on a drive that is part of this PC
        for (var at = folder; at is not null; at = at.Parent)
        {
            if (at.Parent is not null && IsLink(at))
            {
                return false;
            }
        }

        if (new DriveInfo(folder.Root.FullName).DriveType is not (DriveType.Fixed or DriveType.Removable))
        {
            return false;
        }

        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "icacls.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in GrantArguments(folder.FullName, request.AllowChanges))
        {
            start.ArgumentList.Add(argument);
        }

        // Windows rewrites the permissions of everything inside, which takes minutes on a very big folder. The helper's task
        // may only run so long, so past that it leaves Windows to finish by itself.
        using var process = Process.Start(start);
        if (process is null)
        {
            return false;
        }

        return !process.WaitForExit(TimeSpan.FromMinutes(WaitForWindowsMinutes)) || process.ExitCode == 0;
    }

    [SupportedOSPlatform("windows")]
    private static object? ReadSetting(string name)
    {
        using var key = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64).OpenSubKey(@"Software\Khylo");
        return key?.GetValue(name);
    }

    // True when the folder is there, or may be there, but Khylo isn't allowed to see it: the nearest folder above it that
    // Khylo can see either lists it, or can't be listed at all. False when it is simply missing.
    private static bool HiddenFromKhylo(string path)
    {
        try
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (!Directory.Exists(parent))
            {
                return HiddenFromKhylo(parent);
            }

            return Directory.EnumerateDirectories(parent).Any(found => string.Equals(Path.GetFileName(found), name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string UserId()
    {
        try
        {
            // The numeric user ID, which is what file permissions outside a container go by
            return File.ReadLines("/proc/self/status").FirstOrDefault(line => line.StartsWith("Uid:", StringComparison.Ordinal))?
                .Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "?";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "?";
        }
    }

    private static bool IsLink(FileSystemInfo info) => info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null;

    private static string Trim(string path) => path.TrimEnd('\\');

    private static bool SameDrive(string drive, string? other)
        => !string.IsNullOrEmpty(other) && other.Length >= 2 && drive.AsSpan(0, 2).Equals(other.AsSpan(0, 2), StringComparison.OrdinalIgnoreCase);

    private static bool IsAtOrUnder(string path, string folder)
        => path.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || (path.Length > folder.Length && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && path[folder.Length] == '\\');

    /// <summary>
    /// Where Windows keeps itself, its programs and its people's folders.
    /// </summary>
    /// <param name="Windows">The Windows folder.</param>
    /// <param name="ProgramFiles">The programs folder.</param>
    /// <param name="ProgramFilesX86">The folder of 32-bit programs.</param>
    /// <param name="ProgramData">The folder programs keep their data in.</param>
    /// <param name="Users">The folder people's profiles are in.</param>
    /// <param name="Khylo">The folder Khylo's program is in.</param>
    public sealed record Places(string? Windows, string? ProgramFiles, string? ProgramFilesX86, string? ProgramData, string? Users, string? Khylo);

    private sealed class Request
    {
        public string? Path { get; set; }

        public bool AllowChanges { get; set; }
    }
}
