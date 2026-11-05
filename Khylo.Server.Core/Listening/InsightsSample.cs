namespace Khylo.Server.Core.Listening;

/// <summary>
/// One reading of the server's own load.
/// </summary>
/// <param name="Cpu">Processor use in percent.</param>
/// <param name="MemoryUsed">Memory in use, in bytes.</param>
/// <param name="Streams">How many streams are playing.</param>
/// <param name="Transcodes">How many transcodes are running.</param>
/// <param name="BandwidthLocalKbps">Bandwidth to people on the home network.</param>
/// <param name="BandwidthRemoteKbps">Bandwidth to people away from home.</param>
public record InsightsSample(double Cpu, long MemoryUsed, int Streams, int Transcodes, long BandwidthLocalKbps, long BandwidthRemoteKbps);
