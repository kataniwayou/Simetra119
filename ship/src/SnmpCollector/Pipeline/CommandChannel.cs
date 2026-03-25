using System.Threading.Channels;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Bounded channel implementation for SNMP SET command dispatch.
/// <para>
/// Uses <see cref="BoundedChannelFullMode.DropWrite"/> with capacity 16.
/// When the channel is full, TryWrite returns false and the caller (SnapshotJob)
/// logs the drop and increments the command-dropped counter. DropWrite mode does
/// not invoke itemDropped callbacks -- the caller handles failure via the return value.
/// </para>
/// <para>
/// SingleWriter=false: multiple SnapshotJob tenant evaluations may write concurrently.
/// SingleReader=true: only CommandWorkerService reads from this channel.
/// </para>
/// </summary>
public sealed class CommandChannel : ICommandChannel
{
    private readonly Channel<CommandRequest> _channel;

    public CommandChannel()
    {
        var options = new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false,
        };

        _channel = Channel.CreateBounded<CommandRequest>(options);
    }

    /// <inheritdoc/>
    public ChannelWriter<CommandRequest> Writer => _channel.Writer;

    /// <inheritdoc/>
    public ChannelReader<CommandRequest> Reader => _channel.Reader;
}
