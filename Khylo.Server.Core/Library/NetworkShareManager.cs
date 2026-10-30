using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Library;

/// <summary>
/// Connects Windows/SMB and NFS shares for libraries and keeps them connected. On Linux (and in Docker) a share is
/// mounted under the shares folder; on Windows it's connected with its login and used by its \\server\share path.
/// Shares are remembered (passwords encrypted) and reconnected at startup and whenever the NAS comes back.
/// </summary>
public sealed class NetworkShareManager : BackgroundService, INetworkShareManager
{
    private static readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _mountTimeout = TimeSpan.FromSeconds(40);
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly ILogger<NetworkShareManager> _logger;
    private readonly string _storePath;
    private readonly string _keyPath;
    private readonly string _secretsDir;
    private readonly string _sharesRoot;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<StoredShare> _shares;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkShareManager"/> class.
    /// </summary>
    /// <param name="appPaths">The application paths.</param>
    /// <param name="logger">The logger.</param>
    public NetworkShareManager(IApplicationPaths appPaths, ILogger<NetworkShareManager> logger)
    {
        _logger = logger;
        _storePath = Path.Combine(appPaths.ConfigurationDirectoryPath, "khylo_network_shares.json");
        _keyPath = Path.Combine(appPaths.ConfigurationDirectoryPath, "khylo_network_shares.key");
        _secretsDir = Path.Combine(appPaths.ConfigurationDirectoryPath, "khylo_share_logins");
        _sharesRoot = Environment.GetEnvironmentVariable("KHYLO_SHARES_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(appPaths.DataPath, "shares");
        _shares = Load();
    }

    /// <inheritdoc />
    public string? WhyCantConnect()
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        if (!OperatingSystem.IsLinux())
        {
            return "Khylo can't connect network shares on this system yet. Connect the share in the system first, then choose its folder.";
        }

        if (!(UseMountHelper() ? ContainerMayMount() : HasSysAdmin()))
        {
            return InContainer()
                ? "Khylo needs permission to connect network shares. Add these lines under the khylo service in docker-compose.yml, then run \"docker compose up -d\":\n\ncap_add:\n  - SYS_ADMIN\n  - DAC_READ_SEARCH\nsecurity_opt:\n  - apparmor:unconfined"
                : "Khylo needs permission to mount drives to connect network shares. In a Proxmox container, it must be a privileged container with the \"cifs\" and \"nfs\" mount options allowed. Otherwise mount the share in the system instead, then choose its folder.";
        }

        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<NetworkShareInfo> GetShares()
        => _shares.Select(s => ToInfo(s, IsConnected(s))).ToList();

    /// <inheritdoc />
    public async Task<NetworkShareInfo> AddShareAsync(AddNetworkShareRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (WhyCantConnect() is { } reason)
        {
            throw new NetworkShareException(reason);
        }

        var kind = string.Equals(request.Kind, "nfs", StringComparison.OrdinalIgnoreCase) ? "nfs" : "smb";
        var (host, remote) = kind == "nfs" ? ParseNfs(request.Address) : ParseSmb(request.Address);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var address = kind == "nfs" ? $"{host}:{remote}" : $@"\\{host}\{remote.Replace('/', '\\')}";
            if (_shares.FirstOrDefault(s => string.Equals(s.Address, address, StringComparison.OrdinalIgnoreCase)) is { } existing)
            {
                throw new NetworkShareException($"{address} is already connected as \"{existing.Name}\".");
            }

            var name = UniqueName(Clean(string.IsNullOrWhiteSpace(request.Name) ? remote.Split('/', '\\').Last(p => p.Length > 0) : request.Name!));
            var share = new StoredShare
            {
                Id = Guid.NewGuid(),
                Name = name,
                Kind = kind,
                Host = host,
                Remote = remote,
                Address = address,
                Username = kind == "smb" && !string.IsNullOrWhiteSpace(request.Username) ? request.Username.Trim() : null,
                Password = kind == "smb" && !string.IsNullOrEmpty(request.Password) ? Protect(request.Password) : null,
                ReadOnly = request.ReadOnly
            };
            share.Path = OperatingSystem.IsWindows() ? address : Path.Combine(_sharesRoot, name);

            try
            {
                await ConnectAsync(share, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Don't leave an empty folder behind for a share that never connected
                RemoveEmptyFolder(share.Path);
                throw;
            }

            _shares.Add(share);
            Save();
            _logger.LogInformation("Connected network share {Address} as {Path}", share.Address, share.Path);
            return ToInfo(share, true);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveShareAsync(Guid id)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var share = _shares.FirstOrDefault(s => s.Id.Equals(id));
            if (share is null)
            {
                return false;
            }

            await DisconnectAsync(share).ConfigureAwait(false);
            _shares.Remove(share);
            Save();
            _logger.LogInformation("Disconnected network share {Address}", share.Address);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Connect everything at startup, then reconnect anything that drops (a NAS rebooting, say)
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_shares.Count > 0 && WhyCantConnect() is null)
            {
                await _lock.WaitAsync(stoppingToken).ConfigureAwait(false);
                try
                {
                    foreach (var share in _shares.Where(s => !IsConnected(s)).ToList())
                    {
                        try
                        {
                            await ConnectAsync(share, stoppingToken).ConfigureAwait(false);
                            _logger.LogInformation("Reconnected network share {Address}", share.Address);
                        }
                        catch (NetworkShareException ex)
                        {
                            _logger.LogWarning("Network share {Address} isn't available: {Reason}", share.Address, ex.Message);
                        }
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }

            await Task.Delay(_checkInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _lock.Dispose();
        base.Dispose();
    }

    private static NetworkShareInfo ToInfo(StoredShare s, bool connected) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Kind = s.Kind,
        Address = s.Address,
        Username = s.Username,
        ReadOnly = s.ReadOnly,
        Path = s.Path,
        Connected = connected
    };

    // ---- Connecting ----------------------------------------------------------------------------

    private async Task ConnectAsync(StoredShare share, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            ConnectWindows(share);
            return;
        }

        Directory.CreateDirectory(share.Path);
        var mode = share.ReadOnly ? "ro" : "rw";
        string[] args;
        string? loginFile = null;
        if (share.Kind == "nfs")
        {
            args = ["-t", "nfs", $"{share.Host}:{share.Remote}", share.Path, "-o", $"{mode},soft,timeo=150,retrans=3,nolock"];
        }
        else
        {
            // The login goes in a private file, never on a command line other processes could see
            Directory.CreateDirectory(_secretsDir);
            loginFile = Path.Combine(_secretsDir, share.Id.ToString("N", CultureInfo.InvariantCulture));
            string login;
            if (share.Username is null)
            {
                login = "username=guest\npassword=\n";
            }
            else
            {
                // Windows-style logins (DOMAIN\user, user@domain) send the domain separately
                var (user, domain) = SplitLogin(share.Username);
                login = $"username={user}\npassword={Unprotect(share.Password)}\n" + (domain is null ? string.Empty : $"domain={domain}\n");
            }

            await File.WriteAllTextAsync(loginFile, login, cancellationToken).ConfigureAwait(false);
            File.SetUnixFileMode(loginFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            args = ["-t", "cifs", $"//{share.Host}/{share.Remote.Replace('\\', '/')}", share.Path, "-o", $"{mode},credentials={loginFile},iocharset=utf8,uid=0,gid=0,soft,nounix,noserverino"];
        }

        try
        {
            // Khylo may be running as an ordinary user (the Docker image): the mount helper does the mounting
            var (code, output) = UseMountHelper()
                ? await RunAsync("sudo", ["-n", MountHelperPath(), "mount", args[1], args[2], args[3], args[5]], cancellationToken).ConfigureAwait(false)
                : await RunAsync("mount", args, cancellationToken).ConfigureAwait(false);
            if (code != 0 || !IsConnected(share))
            {
                throw new NetworkShareException(Explain(share, output));
            }
        }
        finally
        {
            if (loginFile is not null)
            {
                File.Delete(loginFile);
            }
        }
    }

    private async Task DisconnectAsync(StoredShare share)
    {
        if (OperatingSystem.IsWindows())
        {
            _ = NativeMethods.WNetCancelConnection2W(share.Address, 0, true);
            return;
        }

        if (IsConnected(share))
        {
            if (UseMountHelper())
            {
                await RunAsync("sudo", ["-n", MountHelperPath(), "umount", share.Path], CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await RunAsync("umount", ["-l", share.Path], CancellationToken.None).ConfigureAwait(false);
            }
        }

        RemoveEmptyFolder(share.Path);
    }

    private static void RemoveEmptyFolder(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (IOException)
        {
            // Still busy; it's an empty folder and harmless
        }
    }

    private bool IsConnected(StoredShare share)
    {
        if (OperatingSystem.IsWindows())
        {
            return StorageHealth.IsReachable(share.Path, TimeSpan.FromSeconds(5));
        }

        try
        {
            var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(share.Path));
            return File.ReadLines("/proc/mounts")
                .Select(line => line.Split(' '))
                .Any(parts => parts.Length > 2 && string.Equals(Unescape(parts[1]), target, StringComparison.Ordinal));
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void ConnectWindows(StoredShare share)
    {
        // If Windows can already open it (a mapped drive, a saved login), there's nothing to connect
        if (StorageHealth.IsReachable(share.Address, TimeSpan.FromSeconds(10)))
        {
            return;
        }

        // Windows connects to the share itself (\\server\share); folders inside it come along
        var root = string.Join('\\', share.Address.TrimStart('\\').Split('\\').Take(2));
        var resource = new NativeMethods.NetResource { Type = 1, RemoteName = @"\\" + root };
        var password = share.Password is null ? null : Unprotect(share.Password);
        var result = NativeMethods.WNetAddConnection2W(ref resource, password, share.Username, 0);

        // Already connected (by Khylo earlier or by Windows itself) is fine
        if (result is 0 or 85 or 1219 && Directory.Exists(share.Address))
        {
            return;
        }

        if (result == 0)
        {
            throw new NetworkShareException($"Connected to \\\\{root}, but there's no folder {share.Address} inside it.");
        }

        _logger.LogWarning("Windows couldn't connect {Share} (error {Error})", resource.RemoteName, result);
        throw new NetworkShareException(result switch
        {
            5 or 86 or 1326 => $"The user name or password for {share.Address} wasn't accepted.",
            53 => $"Couldn't find {share.Host} on the network. Check the address, and that it's switched on.",
            67 => $"Couldn't find {share.Address}. Check the computer's name (or use its IP address) and the share's name.",
            1219 => $"Windows is already connected to {share.Host} with a different login. Use the same login, or disconnect it in File Explorer first.",
            _ => $"Couldn't connect {share.Address} (Windows error {result})."
        });
    }

    // Turns mount's error output into something an admin can act on
    private static string Explain(StoredShare share, string output)
    {
        var o = output.ToLowerInvariant();
        if (o.Contains("permission denied", StringComparison.Ordinal) && share.Kind == "smb")
        {
            return $"The user name or password for {share.Address} wasn't accepted.";
        }

        if (o.Contains("access denied", StringComparison.Ordinal) || o.Contains("permission denied", StringComparison.Ordinal))
        {
            return $"{share.Host} didn't allow this server to connect to {share.Remote}. Check the share's NFS permissions include this server's address.";
        }

        if (o.Contains("no such file", StringComparison.Ordinal) || o.Contains("not found", StringComparison.Ordinal) || o.Contains("bad network name", StringComparison.Ordinal))
        {
            return $"{share.Host} doesn't have a share called \"{share.Remote}\".";
        }

        if (o.Contains("could not resolve", StringComparison.Ordinal) || o.Contains("resolve address", StringComparison.Ordinal) || o.Contains("unknown host", StringComparison.Ordinal) || o.Contains("failed to resolve", StringComparison.Ordinal))
        {
            return $"Couldn't find {share.Host} on the network. Check the address, or use its IP address.";
        }

        if (o.Contains("timed out", StringComparison.Ordinal) || o.Contains("host is down", StringComparison.Ordinal) || o.Contains("no route", StringComparison.Ordinal) || o.Contains("in progress", StringComparison.Ordinal) || o.Contains("refused", StringComparison.Ordinal) || o.Contains("could not connect", StringComparison.Ordinal) || o.Contains("unable to find suitable address", StringComparison.Ordinal))
        {
            return $"Couldn't reach {share.Host}. Check that it's on and that this server can reach it.";
        }

        if (o.Contains("operation not permitted", StringComparison.Ordinal))
        {
            return "Khylo isn't allowed to connect network shares here. " + (InContainer()
                ? "Add \"cap_add: [SYS_ADMIN, DAC_READ_SEARCH]\" and \"security_opt: [apparmor:unconfined]\" to the khylo service in docker-compose.yml, then run \"docker compose up -d\"."
                : "Run Khylo with permission to mount drives (in Proxmox: a privileged container with the cifs and nfs mount options), or mount the share in the system instead.");
        }

        var detail = output.Trim();
        return $"Couldn't connect {share.Address}." + (detail.Length > 0 ? $" ({detail})" : string.Empty);
    }

    private static async Task<(int Code, string Output)> RunAsync(string file, string[] args, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new NetworkShareException($"This server doesn't have the tools to connect network shares ({file} is missing).", ex);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_mountTimeout);
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(true);
            return (-1, "timed out");
        }

        return (process.ExitCode, (await stderr.ConfigureAwait(false)) + (await stdout.ConfigureAwait(false)));
    }

    // ---- Addresses and names -------------------------------------------------------------------

    // \\nas\Movies, //nas/Movies, smb://nas/Movies, nas/Movies -> (nas, Movies)
    private static (string Host, string Remote) ParseSmb(string address)
    {
        var a = (address ?? string.Empty).Trim().Replace('\\', '/');
        if (a.StartsWith("smb://", StringComparison.OrdinalIgnoreCase) || a.StartsWith("cifs://", StringComparison.OrdinalIgnoreCase))
        {
            a = a[(a.IndexOf("://", StringComparison.Ordinal) + 3)..];
        }

        a = a.Trim('/');
        var slash = a.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == a.Length - 1)
        {
            throw new NetworkShareException("Enter the share like \\\\nas\\Movies: the computer's name or IP address, then the share name.");
        }

        return (a[..slash], a[(slash + 1)..].TrimEnd('/'));
    }

    // nas:/volume1/Music, nfs://nas/volume1/Music -> (nas, /volume1/Music)
    private static (string Host, string Remote) ParseNfs(string address)
    {
        var a = (address ?? string.Empty).Trim();
        if (a.StartsWith("nfs://", StringComparison.OrdinalIgnoreCase))
        {
            a = a[6..];
            var slash = a.IndexOf('/', StringComparison.Ordinal);
            if (slash > 0)
            {
                a = a[..slash] + ":" + a[slash..];
            }
        }

        var colon = a.IndexOf(":/", StringComparison.Ordinal);
        if (colon <= 0)
        {
            throw new NetworkShareException("Enter the share like nas:/volume1/Movies: the computer's name or IP address, a colon, then the shared folder.");
        }

        return (a[..colon], a[(colon + 1)..].TrimEnd('/'));
    }

    private static string Clean(string name)
    {
        var cleaned = new string(name.Trim().Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' ? c : '-').ToArray()).Trim(' ', '.');
        return cleaned.Length > 0 ? cleaned : "Share";
    }

    private string UniqueName(string name)
    {
        var unique = name;
        for (var i = 2; _shares.Any(s => string.Equals(s.Name, unique, StringComparison.OrdinalIgnoreCase)); i++)
        {
            unique = $"{name} {i}";
        }

        return unique;
    }

    // DOMAIN\user, DOMAIN/user and user@domain -> (user, DOMAIN); a plain name has no domain
    private static (string User, string? Domain) SplitLogin(string login)
    {
        var slash = login.IndexOfAny(['\\', '/']);
        if (slash > 0 && slash < login.Length - 1)
        {
            return (login[(slash + 1)..], login[..slash]);
        }

        var at = login.LastIndexOf('@');
        if (at > 0 && at < login.Length - 1)
        {
            return (login[..at], login[(at + 1)..]);
        }

        return (login, null);
    }

    private static string Unescape(string mountPath) => mountPath.Replace("\\040", " ", StringComparison.Ordinal);

    // Where the mount helper lives: the Docker image keeps it in /usr/local/bin, the Linux package elsewhere
    private static string MountHelperPath() => Environment.GetEnvironmentVariable("KHYLO_MOUNT_HELPER") is { Length: > 0 } path ? path : "/usr/local/bin/khylo-mount";

    // Not running as root, but the Docker image carries a small helper that is allowed to mount for us
    private static bool UseMountHelper() => OperatingSystem.IsLinux() && Environment.UserName != "root" && File.Exists(MountHelperPath());

    // With the helper, what matters is whether the container as a whole was given permission to mount (the
    // permission is not in this process's own set, which is empty for an ordinary user, but in the container's)
    private static bool ContainerMayMount()
    {
        try
        {
            var line = File.ReadLines("/proc/self/status").FirstOrDefault(l => l.StartsWith("CapBnd:", StringComparison.Ordinal));
            return line is not null
                && ulong.TryParse(line[7..].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var caps)
                && (caps & (1UL << 21)) != 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool InContainer() => File.Exists("/.dockerenv") || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

    // CAP_SYS_ADMIN (bit 21) is what mounting needs
    private static bool HasSysAdmin()
    {
        try
        {
            var line = File.ReadLines("/proc/self/status").FirstOrDefault(l => l.StartsWith("CapEff:", StringComparison.Ordinal));
            return line is not null
                && ulong.TryParse(line[7..].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var caps)
                && (caps & (1UL << 21)) != 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // ---- Storage ------------------------------------------------------------------------------

    private List<StoredShare> Load()
    {
        try
        {
            if (File.Exists(_storePath))
            {
                return JsonSerializer.Deserialize<List<StoredShare>>(File.ReadAllText(_storePath)) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogError(ex, "Couldn't read the network share list");
        }

        return [];
    }

    private void Save()
    {
        File.WriteAllText(_storePath, JsonSerializer.Serialize(_shares, _jsonOptions));
        LockDown(_storePath);
    }

    private byte[] Key()
    {
        if (!File.Exists(_keyPath))
        {
            File.WriteAllBytes(_keyPath, RandomNumberGenerator.GetBytes(32));
            LockDown(_keyPath);
        }

        return File.ReadAllBytes(_keyPath);
    }

    private string Protect(string secret)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(secret);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(Key(), 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    private string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return string.Empty;
        }

        var data = Convert.FromBase64String(stored);
        var plain = new byte[data.Length - 28];
        using var aes = new AesGcm(Key(), 16);
        aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plain);
        return Encoding.UTF8.GetString(plain);
    }

    private static void LockDown(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private sealed class StoredShare
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Kind { get; set; } = "smb";

        public string Host { get; set; } = string.Empty;

        public string Remote { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public string? Username { get; set; }

        public string? Password { get; set; }

        public bool ReadOnly { get; set; } = true;

        public string Path { get; set; } = string.Empty;
    }

    private static class NativeMethods
    {
        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int WNetAddConnection2W(ref NetResource netResource, string? password, string? username, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int WNetCancelConnection2W(string name, int flags, [MarshalAs(UnmanagedType.Bool)] bool force);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct NetResource
        {
            public int Scope;
            public int Type;
            public int DisplayType;
            public int Usage;
            public string? LocalName;
            public string? RemoteName;
            public string? Comment;
            public string? Provider;
        }
    }
}
