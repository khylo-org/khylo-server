using System;

namespace Khylo.Controller.Updates;

/// <summary>
/// What the dashboard shows about updates.
/// </summary>
public class KhyloUpdateStatus
{
    /// <summary>Gets or sets the build running now, for example DEV-V26.11.04.01 (empty on a developer's own build).</summary>
    public string CurrentBuild { get; set; } = string.Empty;

    /// <summary>Gets or sets the channel: "stable" (finished releases) or "dev" (every build).</summary>
    public string Channel { get; set; } = "stable";

    /// <summary>Gets or sets a value indicating whether updates install by themselves at the quiet hour.</summary>
    public bool AutoInstall { get; set; }

    /// <summary>Gets or sets the quiet hour (0 to 23, local time) automatic updates wait for.</summary>
    public int InstallHour { get; set; } = 3;

    /// <summary>Gets or sets the newest build on this channel, if the last check found one.</summary>
    public KhyloUpdateInfo? Latest { get; set; }

    /// <summary>Gets or sets a value indicating whether <see cref="Latest"/> is newer than what's running and not skipped.</summary>
    public bool UpdateAvailable { get; set; }

    /// <summary>Gets or sets when the release page was last asked.</summary>
    public DateTime? LastChecked { get; set; }

    /// <summary>Gets or sets a value indicating whether this install can update itself.</summary>
    public bool CanInstall { get; set; }

    /// <summary>Gets or sets, when it can't, why in plain words.</summary>
    public string? WhyNot { get; set; }

    /// <summary>Gets or sets a value indicating whether an update is being installed right now.</summary>
    public bool Installing { get; set; }

    /// <summary>Gets or sets what the update is doing now: "Downloading", "Checking" or "Installing".</summary>
    public string? Stage { get; set; }

    /// <summary>Gets or sets how much of the download is done (0 to 100), while it is downloading.</summary>
    public int? Percent { get; set; }

    /// <summary>Gets or sets what happened with the last update, in plain words.</summary>
    public string? LastResult { get; set; }

    /// <summary>Gets or sets a problem with the last check, in plain words.</summary>
    public string? CheckError { get; set; }
}
