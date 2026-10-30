namespace Khylo.Model.System;

/// <summary>
/// Contains real-time system and resource telemetry for the Khylo administration dashboard.
/// </summary>
public class SystemTelemetryDto
{
    /// <summary>
    /// Gets or sets the estimated CPU utilization percentage.
    /// </summary>
    public double CpuPercent { get; set; }

    /// <summary>
    /// Gets or sets the memory used by the process in bytes.
    /// </summary>
    public long ProcessMemoryUsedBytes { get; set; }

    /// <summary>
    /// Gets or sets the total physical memory in bytes.
    /// </summary>
    public long TotalPhysicalMemoryBytes { get; set; }

    /// <summary>
    /// Gets or sets the number of active streams currently playing.
    /// </summary>
    public int ActiveStreamsCount { get; set; }

    /// <summary>
    /// Gets or sets the number of active transcode sessions.
    /// </summary>
    public int ActiveTranscodesCount { get; set; }

    /// <summary>
    /// Gets or sets the number of hardware-accelerated transcode sessions.
    /// </summary>
    public int HardwareTranscodesCount { get; set; }

    /// <summary>
    /// Gets or sets the server uptime in seconds.
    /// </summary>
    public double ServerUptimeSeconds { get; set; }

    /// <summary>
    /// Gets or sets the number of active process threads.
    /// </summary>
    public int ThreadCount { get; set; }
}
