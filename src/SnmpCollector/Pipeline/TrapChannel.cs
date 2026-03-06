using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Single shared BoundedChannel implementation for trap varbind buffering.
/// Uses DropOldest backpressure to handle trap storms without blocking the UDP listener.
/// Drop events increment the snmp.trap.dropped counter via PipelineMetricService.
/// </summary>
public sealed class TrapChannel : ITrapChannel
{
    private readonly Channel<VarbindEnvelope> _channel;
    private readonly ILogger<TrapChannel> _logger;

    public TrapChannel(
        IOptions<ChannelsOptions> channelsOptions,
        PipelineMetricService pipelineMetrics,
        ILogger<TrapChannel> logger)
    {
        _logger = logger;
        var capacity = channelsOptions.Value.BoundedCapacity;

        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false,
        };

        _channel = Channel.CreateBounded<VarbindEnvelope>(options, itemDropped: envelope =>
        {
            pipelineMetrics.IncrementTrapDropped(envelope.DeviceName);
        });

        _logger.LogInformation("Trap channel created (capacity {Capacity})", capacity);
    }

    /// <inheritdoc/>
    public ChannelWriter<VarbindEnvelope> Writer => _channel.Writer;

    /// <inheritdoc/>
    public ChannelReader<VarbindEnvelope> Reader => _channel.Reader;

    /// <inheritdoc/>
    public void Complete() => _channel.Writer.TryComplete();

    /// <inheritdoc/>
    public async Task WaitForDrainAsync(CancellationToken cancellationToken)
    {
        await _channel.Reader.Completion.WaitAsync(cancellationToken);
        _logger.LogInformation("Trap channel drained");
    }
}
