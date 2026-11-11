using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Security;

/// <summary>
/// Keeps the folders that hold Khylo's logins, database, settings and plug-ins to the people who should open them. On
/// Windows a folder made under ProgramData (or on a data drive) is open to every user of the PC by default, which would
/// let any of them read the logins or add a plug-in. The Windows installer sets this up; this does the same when Khylo
/// starts, so a portable copy or a folder chosen by hand is covered too.
/// </summary>
public static class PrivateFolders
{
    /// <summary>
    /// Makes the folders private, in the background so starting Khylo isn't held up by a big folder. Nothing happens off Windows.
    /// </summary>
    /// <param name="folders">The folders.</param>
    /// <param name="logger">The logger.</param>
    public static void ProtectInBackground(IEnumerable<string> folders, ILogger logger)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var list = folders.ToList();
        _ = Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            foreach (var folder in list)
            {
                try
                {
                    if (EnsurePrivate(folder))
                    {
                        logger.LogInformation("Made {Folder} private to administrators and the account Khylo runs as", folder);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
                {
                    // A limited account (which can open the folders but not change who else can) isn't expected to manage this;
                    // the installer set the permissions, so that is only worth a line for the developer
                    if (Environment.IsPrivilegedProcess)
                    {
                        logger.LogWarning("Couldn't make {Folder} private: {Reason}", folder, ex.Message);
                    }
                    else
                    {
                        logger.LogDebug("Left the permissions of {Folder} as the installer set them: {Reason}", folder, ex.Message);
                    }
                }
            }
        });
    }

    /// <summary>
    /// Takes Users, Authenticated Users and Everyone out of a folder's permissions, keeping the system, administrators and
    /// the account running Khylo. Nothing else is touched, and a folder that is already private is left alone.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <returns>True when the permissions were changed.</returns>
    [SupportedOSPlatform("windows")]
    public static bool EnsurePrivate(string folder)
    {
        var info = new DirectoryInfo(folder);
        if (!info.Exists)
        {
            return false;
        }

        var broad = new[] { WellKnownSidType.BuiltinUsersSid, WellKnownSidType.AuthenticatedUserSid, WellKnownSidType.WorldSid }
            .Select(sid => new SecurityIdentifier(sid, null))
            .ToList();
        var security = info.GetAccessControl();
        var shared = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Any(rule => rule.AccessControlType == AccessControlType.Allow && broad.Contains((SecurityIdentifier)rule.IdentityReference));
        if (!shared)
        {
            return false;
        }

        // Stop inheriting (keeping what was inherited as ordinary entries), then take out the everyone-groups. Whoever
        // runs Khylo is given the folder first, since their access may have come only from one of those groups.
        security.SetAccessRuleProtection(true, true);
        var keep = new List<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null)
        };
        if (WindowsIdentity.GetCurrent().User is { } current)
        {
            keep.Add(current);
        }

        foreach (var sid in keep)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        foreach (var sid in broad)
        {
            security.PurgeAccessRules(sid);
        }

        info.SetAccessControl(security);
        return true;
    }
}
