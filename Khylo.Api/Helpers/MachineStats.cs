using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Khylo.Api.Helpers;

/// <summary>
/// How busy the whole computer Khylo runs on is (not just Khylo): processor use, memory and time since it started.
/// Read straight from the system, never by running a command.
/// </summary>
public static class MachineStats
{
    private static readonly object _lock = new();
    private static (DateTime At, ulong Idle, ulong Total)? _last;
    private static double _lastPercent;

    /// <summary>
    /// Gets how much of the processor the whole computer is using, 0 to 100, since the last time it was asked
    /// (the first answer covers a short moment, so it can be rough).
    /// </summary>
    /// <returns>The percentage, or null if this system can't say.</returns>
    public static double? CpuPercent()
    {
        (ulong Idle, ulong Total)? now = OperatingSystem.IsWindows() ? WindowsTimes() : OperatingSystem.IsLinux() ? LinuxTimes() : null;
        if (now is null)
        {
            return null;
        }

        lock (_lock)
        {
            var sample = (At: DateTime.UtcNow, Idle: now.Value.Idle, Total: now.Value.Total);
            if (_last is { } previous && sample.Total > previous.Total)
            {
                var total = sample.Total - previous.Total;
                var idle = sample.Idle - previous.Idle;
                _lastPercent = Math.Clamp((1.0 - ((double)idle / total)) * 100.0, 0.0, 100.0);
            }

            // Asked again within a second: keep the last answer rather than measuring a tiny slice
            if (_last is null || (sample.At - _last.Value.At).TotalSeconds >= 1)
            {
                _last = sample;
            }

            return Math.Round(_lastPercent, 1);
        }
    }

    /// <summary>
    /// Gets the computer's memory.
    /// </summary>
    /// <returns>Bytes in use and bytes in total, or nulls if this system can't say.</returns>
    public static (long? Used, long? Total) Memory()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                if (GlobalMemoryStatusEx(ref status))
                {
                    return ((long)(status.TotalPhys - status.AvailPhys), (long)status.TotalPhys);
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                var values = File.ReadLines("/proc/meminfo")
                    .Select(l => l.Split(':', 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0], p => long.TryParse(p[1].Replace("kB", string.Empty, StringComparison.Ordinal).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb) ? kb * 1024 : 0);
                if (values.TryGetValue("MemTotal", out var total) && values.TryGetValue("MemAvailable", out var available))
                {
                    return (total - available, total);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            // Fall through: unknown
        }

        return (null, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
    }

    /// <summary>
    /// Gets how long the computer has been on.
    /// </summary>
    /// <returns>The time since it started.</returns>
    public static TimeSpan Uptime() => TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>
    /// Gets a friendly name for the operating system.
    /// </summary>
    /// <returns>For example "Windows 11".</returns>
    public static string OperatingSystemName()
    {
        if (OperatingSystem.IsWindows())
        {
            return Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macOS";
        }

        if (OperatingSystem.IsLinux())
        {
            try
            {
                var pretty = File.ReadLines("/etc/os-release").FirstOrDefault(l => l.StartsWith("PRETTY_NAME=", StringComparison.Ordinal));
                if (pretty is not null)
                {
                    return pretty["PRETTY_NAME=".Length..].Trim('"');
                }
            }
            catch (IOException)
            {
                // Plain "Linux" is fine
            }

            return "Linux";
        }

        return RuntimeInformation.OSDescription;
    }

    /// <summary>
    /// Gets whether Khylo is running inside a container.
    /// </summary>
    /// <returns>True in Docker and similar.</returns>
    public static bool InContainer()
        => File.Exists("/.dockerenv") || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

    /// <summary>
    /// Gets the processor use of one process since it was last asked about (or since it started, the first time).
    /// </summary>
    /// <param name="process">The process.</param>
    /// <param name="previous">What was recorded last time.</param>
    /// <returns>The percentage of one whole computer, and what to record for next time.</returns>
    public static (double Percent, (DateTime At, TimeSpan Cpu) Sample) ProcessCpu(Process process, (DateTime At, TimeSpan Cpu)? previous)
    {
        var now = (At: DateTime.UtcNow, Cpu: process.TotalProcessorTime);
        var from = previous ?? (At: process.StartTime.ToUniversalTime(), Cpu: TimeSpan.Zero);
        var wall = Math.Max(0.001, (now.At - from.At).TotalSeconds);
        var percent = (now.Cpu - from.Cpu).TotalSeconds / wall / Environment.ProcessorCount * 100.0;
        return (Math.Round(Math.Clamp(percent, 0.0, 100.0), 1), now);
    }

    private static (ulong Idle, ulong Total)? WindowsTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return null;
        }

        // Kernel time includes the idle time
        return (idle, kernel + user);
    }

    private static (ulong Idle, ulong Total)? LinuxTimes()
    {
        try
        {
            var parts = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(p => ulong.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            return parts.Length >= 5 ? (parts[3] + parts[4], parts.Aggregate(0UL, (a, b) => a + b)) : null;
        }
        catch (Exception ex) when (ex is IOException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
