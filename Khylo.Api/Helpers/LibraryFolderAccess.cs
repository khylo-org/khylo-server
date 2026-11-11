using System;
using System.IO;
using System.Security.Principal;

namespace Khylo.Api.Helpers;

/// <summary>
/// When Khylo runs as a limited Windows account, a folder only the person (or the system) can open can't be used for a
/// library. This says so in words the person can act on, instead of leaving them with an empty library.
/// </summary>
public static class LibraryFolderAccess
{
    /// <summary>
    /// Says why Khylo can't open a folder, or returns null when it can (or when this check doesn't apply).
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <returns>The reason, or null.</returns>
    public static string? ProblemOpening(string? path)
    {
        if (!OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (!Directory.Exists(path))
            {
                // Missing folders are reported the way they always were
                return null;
            }

            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            entries.MoveNext();
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return Message(path, WindowsIdentity.GetCurrent().Name);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The sentence shown when Khylo can't open a folder.
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <param name="account">The Windows account Khylo runs as.</param>
    /// <returns>The message.</returns>
    public static string Message(string path, string account)
        => $"Khylo runs as the limited Windows account \"{account}\", which can't open {path}. Give that account read access to the folder " +
           "(right-click it, Properties, Security, Edit, Add, then type the account's name), and add the folder again. " +
           "Or run Khylo's setup again and choose to run Khylo as the system.";
}
