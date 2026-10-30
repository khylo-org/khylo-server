using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Khylo.Controller.Library;

/// <summary>
/// Network shares (Windows/SMB and NFS) that Khylo connects itself, so media on a NAS or another computer
/// can be added to libraries like any local folder.
/// </summary>
public interface INetworkShareManager
{
    /// <summary>
    /// Gets a value indicating whether this server can connect shares, and if not, why.
    /// </summary>
    /// <returns>Null when it can; otherwise a plain explanation for the admin.</returns>
    string? WhyCantConnect();

    /// <summary>
    /// Gets the shares Khylo has been asked to connect.
    /// </summary>
    /// <returns>The shares, without their passwords.</returns>
    IReadOnlyList<NetworkShareInfo> GetShares();

    /// <summary>
    /// Connects a new share and remembers it.
    /// </summary>
    /// <param name="request">What to connect.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The connected share.</returns>
    /// <exception cref="NetworkShareException">The share couldn't be connected; the message says why.</exception>
    Task<NetworkShareInfo> AddShareAsync(AddNetworkShareRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Disconnects a share and forgets it. The files on the share are not touched.
    /// </summary>
    /// <param name="id">The share id.</param>
    /// <returns>True if it was found.</returns>
    Task<bool> RemoveShareAsync(Guid id);
}
