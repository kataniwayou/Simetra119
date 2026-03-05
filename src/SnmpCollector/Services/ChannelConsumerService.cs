using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Services;

/// <summary>
/// BackgroundService that drains VarbindEnvelopes from per-device BoundedChannels and dispatches
/// each as an <see cref="SnmpOidReceived"/> request through the MediatR pipeline via ISender.Send.
///
/// Design principle: only the consumer (never the listener) calls ISender.Send. The listener
/// writes to channels; this service is the single point that bridges the channel backpressure
/// layer to the MediatR IPipelineBehavior pipeline (Logging → Exception → Validation →
/// OidResolution → OtelMetricHandler).
///
/// ISender.Send is used (NOT IPublisher.Publish) because SnmpOidReceived implements
/// IRequest&lt;Unit&gt;. IPublisher.Publish would bypass all IPipelineBehavior behaviors entirely.
/// </summary>
public sealed class ChannelConsumerService : BackgroundService
{
    private readonly IDeviceChannelManager _channelManager;
    private readonly ISender _sender;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly ILogger<ChannelConsumerService> _logger;

    public ChannelConsumerService(
        IDeviceChannelManager channelManager,
        ISender sender,
        PipelineMetricService pipelineMetrics,
        ILogger<ChannelConsumerService> logger)
    {
        _channelManager = channelManager;
        _sender = sender;
        _pipelineMetrics = pipelineMetrics;
        _logger = logger;
    }

    /// <summary>
    /// Spawns one consumer Task per device (via Task.WhenAll) that reads VarbindEnvelopes
    /// from the device's BoundedChannel until the channel is marked complete (graceful shutdown)
    /// or the cancellation token is triggered (host shutdown).
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = _channelManager.DeviceNames
            .Select(name => ConsumeDeviceAsync(name, stoppingToken))
            .ToArray();

        _logger.LogInformation("Channel consumers started for {Count} devices", tasks.Length);

        await Task.WhenAll(tasks);

        _logger.LogInformation("All channel consumers completed");
    }

    /// <summary>
    /// Reads VarbindEnvelopes from the specified device's channel reader via ReadAllAsync,
    /// constructs an SnmpOidReceived with Source=Trap and pre-set DeviceName (no second lookup),
    /// increments the snmp.trap.received counter (PMET-06), then dispatches via ISender.Send
    /// so all IPipelineBehavior behaviors execute. Exceptions are caught and logged at Warning
    /// so the consumer loop continues processing subsequent envelopes.
    /// </summary>
    private async Task ConsumeDeviceAsync(string deviceName, CancellationToken ct)
    {
        var reader = _channelManager.GetReader(deviceName);

        await foreach (var envelope in reader.ReadAllAsync(ct))
        {
            try
            {
                var msg = new SnmpOidReceived
                {
                    Oid        = envelope.Oid,
                    AgentIp    = envelope.AgentIp,
                    DeviceName = envelope.DeviceName,   // pre-resolved by listener — no double lookup
                    Value      = envelope.Value,
                    Source     = SnmpSource.Trap,        // ALWAYS Trap for trap-originated events
                    TypeCode   = envelope.TypeCode,
                };

                _pipelineMetrics.IncrementTrapReceived();    // snmp.trap.received (PMET-06)
                await _sender.Send(msg, ct);                 // ISender.Send — all behaviors execute
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;     // graceful shutdown — exit consumer loop
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Error processing varbind {Oid} for {DeviceName}",
                    envelope.Oid, deviceName);
                // continue to next envelope — do not crash the consumer
            }
        }
    }
}
