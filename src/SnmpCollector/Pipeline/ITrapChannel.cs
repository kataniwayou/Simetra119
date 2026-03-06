using System.Threading.Channels;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Single shared channel interface for trap varbind buffering.
/// Replaces the per-device IDeviceChannelManager with a single BoundedChannel
/// that accepts traps from any device identified by community string convention.
/// </summary>
public interface ITrapChannel
{
    /// <summary>
    /// Channel writer used by SnmpTrapListenerService to enqueue VarbindEnvelopes.
    /// </summary>
    ChannelWriter<VarbindEnvelope> Writer { get; }

    /// <summary>
    /// Channel reader used by ChannelConsumerService to drain VarbindEnvelopes.
    /// </summary>
    ChannelReader<VarbindEnvelope> Reader { get; }

    /// <summary>
    /// Marks the channel writer as complete, signaling the consumer to drain and finish.
    /// Called during graceful shutdown. Idempotent (TryComplete).
    /// </summary>
    void Complete();

    /// <summary>
    /// Asynchronously waits for the channel reader to finish processing all remaining items
    /// after Complete() has been called.
    /// </summary>
    Task WaitForDrainAsync(CancellationToken cancellationToken);
}
