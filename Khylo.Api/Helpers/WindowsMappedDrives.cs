using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;
using System.Threading.Tasks;
using Khylo.Api.Models.EnvironmentDtos;
using Microsoft.Win32;

namespace Khylo.Api.Helpers;

/// <summary>
/// Finds the network drives people have mapped on this Windows computer (X: pointing to \\nas\movies).
/// </summary>
/// <remarks>
/// Windows keeps drive letters private to each person's login, so a server that starts with the PC can't use them.
/// But the list of what each person mapped is saved in their settings, and the server can use the addresses directly
/// once it has a login for the share. This finds that list so the folder picker can offer it.
/// </remarks>
public static class WindowsMappedDrives
{
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Gets the mapped drives of everyone logged in, once per address. Empty on other systems.
    /// </summary>
    /// <returns>The mapped drives.</returns>
    public static async Task<IReadOnlyList<MappedDriveDto>> FindAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        List<MappedDriveDto> drives;
        try
        {
            drives = ReadSavedDrives();
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return [];
        }

        // Whether this server can open each one as it is (a share that allows everyone, or one already connected)
        await Task.WhenAll(drives.Select(async drive => drive.Reachable = await CanOpenAsync(drive.Address).ConfigureAwait(false))).ConfigureAwait(false);
        return drives;
    }

    private static async Task<bool> CanOpenAsync(string address)
    {
        try
        {
            return await Task.Run(() => Directory.Exists(address)).WaitAsync(OpenTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static List<MappedDriveDto> ReadSavedDrives()
    {
        var found = new List<MappedDriveDto>();
        foreach (var sid in Registry.Users.GetSubKeyNames())
        {
            // A logged-in person's settings. The "_Classes" entries are something else.
            if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) || sid.EndsWith("_Classes", StringComparison.Ordinal))
            {
                continue;
            }

            using var network = Registry.Users.OpenSubKey(sid + @"\Network");
            if (network is null)
            {
                continue;
            }

            var account = AccountName(sid);
            foreach (var letter in network.GetSubKeyNames())
            {
                using var drive = network.OpenSubKey(letter);
                if (drive?.GetValue("RemotePath") is not string address || string.IsNullOrWhiteSpace(address))
                {
                    continue;
                }

                var user = drive.GetValue("UserName") as string;
                found.Add(new MappedDriveDto
                {
                    Letter = letter.ToUpperInvariant() + ":",
                    Address = address.TrimEnd('\\'),
                    UserName = string.IsNullOrEmpty(user) || user == "0" ? null : user,
                    Account = account
                });
            }
        }

        return found.GroupBy(d => d.Address, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
    }

    [SupportedOSPlatform("windows")]
    private static string AccountName(string sid)
    {
        try
        {
            return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException)
        {
            return sid;
        }
    }
}
