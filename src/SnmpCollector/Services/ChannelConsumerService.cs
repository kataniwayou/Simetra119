using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Services;

/// <summary>
/// BackgroundService that drains VarbindEnvelopes from the single shared ITrapChannel and dispatches
/// each as an <see cref="SnmpOidReceived"/> request through the MediatR pipeline via ISender.Send.
///
/// Design principle: only the consumer (never the listener) calls ISender.Send. The listener
/// writes to the shared channel; this service is the single point that bridges the channel
/// backpressure layer to the MediatR IPipelineBehavior pipeline (Logging -> Exception ->
/// Validation -> OidResolution -> OtelMetricHandler).
///
/// ISender.Send is used (NOT IPublisher.Publish) because SnmpOidReceived implements
/// IRequest&lt;Unit&gt;. IPublisher.Publish would bypass all IPipelineBehavior behaviors entirely.
/// </summary>
public sealed class ChannelConsumerService : BackgroundService
{
    private readonly ITrapChannel _trapChannel;
    private readonly ISender _sender;
    private readonly ICorrelationService _correlation;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly ILogger<ChannelConsumerService> _logger;

    public ChannelConsumerService(
        ITrapChannel trapChannel,
        ISender sender,
        ICorrelationService correlation,
        PipelineMetricService pipelineMetrics,
        ILogger<ChannelConsumerService> logger)
    {
        _trapChannel = trapChannel;
        _sender = sender;
        _correlation = correlation;
        _pipelineMetrics = pipelineMetrics;
        _logger = logger;
    }

    /// <summary>
    /// Single consumer loop that reads VarbindEnvelopes from the shared trap channel,
    /// constructs SnmpOidReceived with Source=Trap and pre-set DeviceName (extracted
    /// from community string by the listener), increments the snmp.trap.received counter,
    /// then dispatches via ISender.Send so all IPipelineBehavior behaviors execute.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Trap channel consumer started");

        await foreach (var envelope in _trapChannel.Reader.ReadAllAsync(stoppingToken))
        {
            _correlation.OperationCorrelationId = _correlation.CurrentCorrelationId;
            try
            {
                var msg = new SnmpOidReceived
                {
                    Oid        = envelope.Oid,
                    AgentIp    = envelope.AgentIp,
                    DeviceName = envelope.DeviceName,
                    Value      = envelope.Value,
                    Source     = SnmpSource.Trap,
                    TypeCode   = envelope.TypeCode,
                };

                _pipelineMetrics.IncrementTrapReceived(envelope.DeviceName);
                await _sender.Send(msg, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Error processing varbind {Oid} for {DeviceName}",
                    envelope.Oid, envelope.DeviceName);
            }
        }

        _logger.LogInformation("Trap channel consumer completed");
    }
}
