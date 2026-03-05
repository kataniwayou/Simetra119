using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.Telemetry;
using System.Threading.Channels;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Singleton that creates and owns one BoundedChannel&lt;VarbindEnvelope&gt; per registered device.
/// Channels use DropOldest backpressure to handle trap storms without blocking the UDP listener.
/// Drop events increment the snmp.trap.dropped counter and log a Warning every 100 drops per device.
/// </summary>
public sealed class DeviceChannelManager : IDeviceChannelManager
{
    private readonly Dictionary<string, Channel<VarbindEnvelope>> _channels;
    private readonly Dictionary<string, DropCounter> _dropCounters;
    private readonly ILogger<DeviceChannelManager> _logger;

    /// <summary>Thread-safe drop counter using Interlocked.Increment on a long field.</summary>
    private sealed class DropCounter
    {
        public long Count;
    }

    public DeviceChannelManager(
        IDeviceRegistry deviceRegistry,
        IOptions<ChannelsOptions> channelsOptions,
        PipelineMetricService pipelineMetrics,
        ILogger<DeviceChannelManager> logger)
    {
        _logger = logger;
        _channels = new Dictionary<string, Channel<VarbindEnvelope>>(StringComparer.Ordinal);
        _dropCounters = new Dictionary<string, DropCounter>(StringComparer.Ordinal);

        var capacity = channelsOptions.Value.BoundedCapacity;

        foreach (var device in deviceRegistry.AllDevices)
        {
            var deviceName = device.Name;
            var dropCounter = new DropCounter();
            _dropCounters[deviceName] = dropCounter;

            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = true,
                AllowSynchronousContinuations = false,
            };

            var channel = Channel.CreateBounded<VarbindEnvelope>(options, itemDropped: _ =>
            {
                pipelineMetrics.IncrementTrapDropped(deviceName);
                var count = Interlocked.Increment(ref dropCounter.Count);
                if (count % 100 == 0)
                {
                    logger.LogWarning(
                        "Trap channel for {DeviceName} has dropped {Count} varbinds (capacity {Capacity})",
                        deviceName, count, capacity);
                }
            });

            _channels[deviceName] = channel;
        }

        _logger.LogInformation(
            "Device channels created for {Count} devices (capacity {Capacity})",
            _channels.Count, capacity);
    }

    /// <inheritdoc/>
    public ChannelWriter<VarbindEnvelope> GetWriter(string deviceName)
        => _channels[deviceName].Writer;

    /// <inheritdoc/>
    public ChannelReader<VarbindEnvelope> GetReader(string deviceName)
        => _channels[deviceName].Reader;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> DeviceNames
        => _channels.Keys;

    /// <inheritdoc/>
    public void CompleteAll()
    {
        foreach (var (_, channel) in _channels)
        {
            channel.Writer.TryComplete();
        }
    }
}
