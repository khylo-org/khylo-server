using System.Collections.Generic;

namespace Khylo.Api.Models.EnvironmentDtos;

/// <summary>
/// Whether Khylo can open a folder, and what can be done when it can't.
/// </summary>
public class FolderAccessDto
{
    /// <summary>
    /// Gets or sets a value indicating whether Khylo can see what is in the folder.
    /// </summary>
    public bool CanOpen { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Khylo can add and remove files in the folder.
    /// </summary>
    public bool CanChange { get; set; }

    /// <summary>
    /// Gets or sets the account or user Khylo runs as.
    /// </summary>
    public string? Account { get; set; }

    /// <summary>
    /// Gets or sets what the owner has to do so Khylo can open the folder, for the way this Khylo is installed.
    /// </summary>
    public string? HowToFix { get; set; }

    /// <summary>
    /// Gets or sets how many folders inside this one Khylo is not allowed to open (only when asked for).
    /// </summary>
    public int BlockedBelow { get; set; }

    /// <summary>
    /// Gets or sets a few of those folders, by name.
    /// </summary>
    public IReadOnlyList<string>? BlockedSample { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every folder inside was looked at.
    /// </summary>
    public bool BlockedComplete { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Khylo can be given access to the folder from its own pages.
    /// </summary>
    public bool CanGive { get; set; }

    /// <summary>
    /// Gets or sets why Khylo can't be given access to this folder from its own pages, when it could for another folder.
    /// </summary>
    public string? WhyNotGive { get; set; }
}
