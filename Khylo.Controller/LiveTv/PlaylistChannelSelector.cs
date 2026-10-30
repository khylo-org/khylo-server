using System;
using System.Linq;
using System.Text.RegularExpressions;
using Khylo.Model.LiveTv;

namespace Khylo.Controller.LiveTv;

/// <summary>
/// Applies a tuner's channel selection and recognizes on-demand and adult playlist entries.
/// </summary>
public static partial class PlaylistChannelSelector
{
    /// <summary>
    /// Whether a playlist channel is brought into Live TV under the tuner's selection.
    /// </summary>
    /// <param name="tuner">The tuner.</param>
    /// <param name="channel">The playlist channel.</param>
    /// <returns>Whether the channel is imported.</returns>
    public static bool IsIncluded(TunerHostInfo tuner, ChannelInfo channel)
    {
        ArgumentNullException.ThrowIfNull(tuner);
        ArgumentNullException.ThrowIfNull(channel);

        if (!tuner.EnableChannelSelection)
        {
            return true;
        }

        if (tuner.IncludedChannels?.Contains(channel.Id, StringComparer.Ordinal) == true)
        {
            return true;
        }

        return tuner.IncludedGroups?.Contains(channel.ChannelGroup ?? string.Empty, StringComparer.OrdinalIgnoreCase) == true
            && tuner.ExcludedChannels?.Contains(channel.Id, StringComparer.Ordinal) != true;
    }

    /// <summary>
    /// Works out whether an entry is a live channel, an on-demand title or an adult channel.
    /// Xtream-style providers put movies and series under /movie/ and /series/ paths.
    /// </summary>
    /// <param name="channel">The playlist channel.</param>
    /// <returns>The kind of entry.</returns>
    public static PlaylistChannelKind GetKind(ChannelInfo channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var path = channel.Path ?? string.Empty;
        if (path.Contains("/movie/", StringComparison.OrdinalIgnoreCase) || path.Contains("/series/", StringComparison.OrdinalIgnoreCase))
        {
            return PlaylistChannelKind.OnDemand;
        }

        if (AdultRegex().IsMatch(channel.ChannelGroup ?? string.Empty) || AdultRegex().IsMatch(channel.Name ?? string.Empty))
        {
            return PlaylistChannelKind.Adult;
        }

        return PlaylistChannelKind.Live;
    }

    [GeneratedRegex(@"\b(xxx|adults?|18\+|porn)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AdultRegex();
}
