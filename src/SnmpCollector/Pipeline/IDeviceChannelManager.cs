using System.Threading.Channels;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Manages per-device BoundedChannels that buffer trap varbinds between the listener
/// (producer) and the consumer service (consumer). Created once at startup for all
/// registered devices; channels are not added or removed at runtime.
/// </summary>
public interface IDeviceChannelManager
{
    /// <summary>
    /// Gets the channel writer for the specified device.
    /// Used by SnmpTrapListenerService to enqueue VarbindEnvelopes received from UDP traps.
    /// </summary>
    ChannelWriter<VarbindEnvelope> GetWriter(string deviceName);

    /// <summary>
    /// Gets the channel reader for the specified device.
    /// Used by ChannelConsumerService to drain VarbindEnvelopes and publish to MediatR.
    /// </summary>
    ChannelReader<VarbindEnvelope> GetReader(string deviceName);

    /// <summary>
    /// The set of device names for which channels were created at startup.
    /// ChannelConsumerService iterates this to spawn one consumer Task per device.
    /// </summary>
    IReadOnlyCollection<string> DeviceNames { get; }

    /// <summary>
    /// Marks all channel writers as complete, signaling consumers to drain remaining items
    /// and then finish. Called during graceful shutdown before the application exits.
    /// </summary>
    void CompleteAll();
}
