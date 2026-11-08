using System;
using System.Threading;
using System.Threading.Tasks;

namespace Khylo.Controller.Updates;

/// <summary>
/// Keeps Khylo itself up to date: looks for newer builds on the public release page, tells the admin, and
/// (on installs that can) installs them, on request or at a quiet hour.
/// </summary>
public interface IKhyloUpdateService
{
    /// <summary>
    /// Gets what's known about updates right now.
    /// </summary>
    /// <returns>The status.</returns>
    KhyloUpdateStatus GetStatus();

    /// <summary>
    /// Asks the release page whether there's a newer build.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The status afterwards.</returns>
    Task<KhyloUpdateStatus> CheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Changes the update settings.
    /// </summary>
    /// <param name="channel">"stable" or "dev".</param>
    /// <param name="autoInstall">Whether to install updates by itself at the quiet hour.</param>
    /// <returns>The status afterwards.</returns>
    KhyloUpdateStatus SetSettings(string channel, bool autoInstall);

    /// <summary>
    /// Ignores the build that's on offer; the next newer one is offered again.
    /// </summary>
    /// <returns>The status afterwards.</returns>
    KhyloUpdateStatus SkipLatest();

    /// <summary>
    /// Clears the note about how the last update went.
    /// </summary>
    /// <returns>The status afterwards.</returns>
    KhyloUpdateStatus DismissResult();

    /// <summary>
    /// Downloads, checks and installs the build on offer. Khylo restarts as part of it.
    /// </summary>
    /// <param name="startedBy">Who asked, for the activity log ("automatically" for the quiet hour).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that finishes once the update has been handed over to the installer.</returns>
    Task InstallAsync(string startedBy, CancellationToken cancellationToken);
}
