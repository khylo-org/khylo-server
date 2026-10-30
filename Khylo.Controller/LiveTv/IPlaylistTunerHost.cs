using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Model.LiveTv;

namespace Khylo.Controller.LiveTv;

/// <summary>
/// A tuner backed by a channel playlist (M3U), whose channels can be picked individually.
/// </summary>
public interface IPlaylistTunerHost
{
    /// <summary>
    /// Gets every channel in the tuner's playlist, ignoring the channel selection.
    /// </summary>
    /// <param name="tuner">The tuner.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>All playlist channels.</returns>
    Task<List<ChannelInfo>> GetAllPlaylistChannels(TunerHostInfo tuner, CancellationToken cancellationToken);
}
