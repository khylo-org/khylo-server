using System.Net;
using System.Net.Sockets;

namespace Khylo.Server.Core.Listening;

/// <summary>
/// Tells whether a connection came from inside the home network.
/// </summary>
public static class ListeningNetwork
{
    /// <summary>
    /// Whether an address (with or without a port) is on the home network: loopback, private or link-local.
    /// </summary>
    /// <param name="remoteEndPoint">The address as the session reports it.</param>
    /// <returns>True when local, false when remote, null when it can't be told.</returns>
    public static bool? IsLocal(string? remoteEndPoint)
    {
        if (string.IsNullOrWhiteSpace(remoteEndPoint))
        {
            return null;
        }

        var text = remoteEndPoint.Trim();
        if (!IPAddress.TryParse(text, out var address) && IPEndPoint.TryParse(text, out var endPoint))
        {
            address = endPoint.Address;
        }

        if (address is null)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
    }
}
