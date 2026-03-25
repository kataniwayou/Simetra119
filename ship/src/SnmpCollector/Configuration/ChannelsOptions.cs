using System.ComponentModel.DataAnnotations;

namespace SnmpCollector.Configuration;

/// <summary>
/// Configuration for per-device BoundedChannel capacity. Bound from "Channels" section.
/// </summary>
public sealed class ChannelsOptions
{
    public const string SectionName = "Channels";

    /// <summary>
    /// Maximum number of varbind envelopes buffered per device channel.
    /// When the channel is full, the oldest item is dropped (DropOldest policy).
    /// Default: 1,000 items per device.
    /// </summary>
    [Range(1, 100_000)]
    public int BoundedCapacity { get; set; } = 1_000;
}
