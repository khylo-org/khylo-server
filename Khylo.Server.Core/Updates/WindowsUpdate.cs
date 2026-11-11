using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Updates;

/// <summary>
/// The Windows side of putting a new version in: a folder only the system can open, the script the system's one-time task
/// runs (back up, install, check Khylo answers, or put the old one back) and the checks that this really is the installed
/// copy. Used by the server when it runs with enough rights to update itself, and by the update helper when it doesn't.
/// </summary>
internal static class WindowsUpdate
{
    internal const string TaskName = "Khylo Update";

    /// <summary>The system task that picks up an update the server has staged (the server itself runs without administrator rights).</summary>
    internal const string HelperTaskName = "Khylo Update Helper";

    // Runs as the system account in its own one-time task: back up, install, check Khylo answers, or put the old one back
    private const string UpdateScript = """
        $ErrorActionPreference = 'Continue'
        $log = '{{UPDATE_DIR}}\update.log'
        function Log($m) { Add-Content -Path $log -Value ("{0} {1}" -f (Get-Date -Format s), $m) }
        $install = '{{INSTALL_DIR}}'
        $backup = '{{UPDATE_DIR}}\previous'
        $setup = '{{SETUP}}'
        Log 'Update to {{BUILD}} started'
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $setup).Hash.ToLowerInvariant() -ne '{{SHA256}}') {
            Log 'The installer changed after Khylo checked it, so it was not run'
            Remove-Item $setup -Force -ErrorAction SilentlyContinue
            Set-Content -Path '{{RESULT}}' -Value 'failed|{{BUILD}}'
            schtasks /Delete /TN "Khylo Update" /F | Out-Null
            exit 1
        }
        schtasks /End /TN "Khylo Update Helper" | Out-Null
        schtasks /Change /TN "Khylo Update Helper" /DISABLE | Out-Null
        schtasks /End /TN "Khylo Server" | Out-Null
        Start-Sleep -Seconds 5
        Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue
        robocopy $install $backup /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
        Log 'Backed up the current version'
        $p = Start-Process -FilePath $setup -ArgumentList '/S' -Wait -PassThru
        Log ("Installer finished with {0}" -f $p.ExitCode)
        $ok = $false
        for ($i = 0; $i -lt 36; $i++) {
            Start-Sleep -Seconds 5
            try {
                $r = Invoke-WebRequest -Uri 'http://127.0.0.1:{{PORT}}/health' -UseBasicParsing -TimeoutSec 5
                if ($r.Content -match 'Healthy') { $ok = $true; break }
            } catch { }
        }
        if ($ok) {
            Log 'The new version is answering'
            Set-Content -Path '{{RESULT}}' -Value 'ok|{{BUILD}}'
        } else {
            Log 'The new version did not start; putting the old one back'
            schtasks /End /TN "Khylo Server" | Out-Null
            Start-Sleep -Seconds 5
            robocopy $backup $install /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
            schtasks /Run /TN "Khylo Server" | Out-Null
            Set-Content -Path '{{RESULT}}' -Value 'rolledback|{{BUILD}}'
        }
        Remove-Item $setup -Force -ErrorAction SilentlyContinue
        schtasks /Change /TN "Khylo Update Helper" /ENABLE | Out-Null
        schtasks /Delete /TN "Khylo Update" /F | Out-Null
        """;

    // The folder the installer put Khylo in, when this is that copy
    internal static string? InstalledFolder()
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } exe)
        {
            return null;
        }

        var folder = Path.GetDirectoryName(exe);
        if (folder is null || !string.Equals(Path.GetFileName(exe), "khylo.exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var key = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64).OpenSubKey(@"Software\Khylo");
        var registered = key?.GetValue("InstallDir") as string;
        return registered is not null && string.Equals(Path.TrimEndingDirectorySeparator(registered), Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase)
            && !folder.Contains('\'', StringComparison.Ordinal)
            ? folder
            : null;
    }

    // Whether the installer set up the update helper (written to the registry by the installer, which only an administrator can change)
    internal static bool HelperInstalled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var key = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64).OpenSubKey(@"Software\Khylo");
        return key?.GetValue("UpdateHelper") is int and 1;
    }

    /// <summary>
    /// Where the system keeps what an update needs (its copies of the installer, the script, the backup). It is beside Khylo's
    /// data folder, not inside it: the account Khylo runs as can change everything inside that one, including swapping a
    /// folder for a link, and the system must never write where another account can redirect it.
    /// </summary>
    /// <returns>The folder's path.</returns>
    internal static string PrivateRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KhyloUpdate");

    // Only the system and administrators, with nothing inherited
    [SupportedOSPlatform("windows")]
    private static DirectorySecurity PrivateAccess()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var account in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(account, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        return security;
    }

    // The installer makes this folder. If it isn't there it is made here, and if it is there it must be a plain folder that the
    // system or the administrators own: one made by another account (which could be a link, or open to that account) isn't used.
    [SupportedOSPlatform("windows")]
    internal static string EnsurePrivateRoot(string root)
    {
        var info = new DirectoryInfo(root);
        if (!info.Exists)
        {
            info.Create(PrivateAccess());
            info.Refresh();
        }

        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null)
        {
            throw new InvalidOperationException("The update folder is a link, so Khylo won't use it.");
        }

        var owner = info.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is null || !(owner.IsWellKnown(WellKnownSidType.LocalSystemSid) || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)))
        {
            throw new InvalidOperationException("The update folder belongs to another account, so Khylo won't use it.");
        }

        info.SetAccessControl(PrivateAccess());
        return root;
    }

    // A new folder for each update, created with its permissions already set (only the system and administrators), so
    // nobody else on this computer can swap the installer or the script between the check and the run
    [SupportedOSPlatform("windows")]
    internal static string CreatePrivateWorkFolder(ILogger logger) => CreatePrivateWorkFolder(PrivateRoot(), logger);

    [SupportedOSPlatform("windows")]
    internal static string CreatePrivateWorkFolder(string root, ILogger logger)
    {
        EnsurePrivateRoot(root);
        RemoveOldWorkFolders(root, logger);

        var folder = Path.Combine(root, "run-" + Guid.NewGuid().ToString("N"));
        new DirectoryInfo(folder).Create(PrivateAccess());
        return folder;
    }

    // A separate one-time system task runs the update, so it isn't stopped when the installer stops Khylo's own task
    internal static void StartInstaller(string work, string setup, string sha256, string build, int port, string resultPath, string installDir, ILogger logger)
    {
        var script = Path.Combine(work, "update.ps1");
        File.WriteAllText(script, UpdateScript
            .Replace("{{UPDATE_DIR}}", work, StringComparison.Ordinal)
            .Replace("{{INSTALL_DIR}}", installDir, StringComparison.Ordinal)
            .Replace("{{SETUP}}", setup, StringComparison.Ordinal)
            .Replace("{{SHA256}}", sha256, StringComparison.Ordinal)
            .Replace("{{RESULT}}", resultPath, StringComparison.Ordinal)
            .Replace("{{BUILD}}", build, StringComparison.Ordinal)
            .Replace("{{PORT}}", port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));

        Run("schtasks.exe", "/Create", "/TN", TaskName, "/TR", $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{script}\"", "/SC", "ONCE", "/ST", "00:00", "/RU", "SYSTEM", "/RL", "HIGHEST", "/F");
        Run("schtasks.exe", "/Run", "/TN", TaskName);
        logger.LogWarning("Handed Khylo {Build} to the installer; Khylo will restart in a moment", build);
    }

    private static void RemoveOldWorkFolders(string updateDir, ILogger logger)
    {
        foreach (var folder in Directory.EnumerateDirectories(updateDir, "run-*"))
        {
            try
            {
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(folder) > TimeSpan.FromDays(1))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug("Couldn't remove the old update folder {Folder}: {Reason}", folder, ex.Message);
            }
        }
    }

    private static void Run(string file, params string[] args)
    {
        var info = new ProcessStartInfo(file) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Windows wouldn't start the update.");
        process.WaitForExit(30_000);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Windows wouldn't schedule the update: " + process.StandardError.ReadToEnd().Trim());
        }
    }
}
