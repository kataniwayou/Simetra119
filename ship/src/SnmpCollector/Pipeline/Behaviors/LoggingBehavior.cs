using MediatR;
using Microsoft.Extensions.Logging;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Pipeline.Behaviors;

/// <summary>
/// Outermost pipeline behavior that logs every SnmpOidReceived notification at Debug level
/// and increments <c>snmp.event.published</c> (PMET-01). Open generic over TNotification
/// so MediatR registers it for all notification types, but logging and counting only fire
/// when the notification is SnmpOidReceived.
/// Always calls next() -- never short-circuits the pipeline.
/// </summary>
public sealed class LoggingBehavior<TNotification, TResponse>
    : IPipelineBehavior<TNotification, TResponse>
    where TNotification : notnull
{
    private readonly ILogger<LoggingBehavior<TNotification, TResponse>> _logger;
    private readonly PipelineMetricService _metrics;

    public LoggingBehavior(
        ILogger<LoggingBehavior<TNotification, TResponse>> logger,
        PipelineMetricService metrics)
    {
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<TResponse> Handle(
        TNotification notification,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (notification is SnmpOidReceived msg)
        {
            _metrics.IncrementPublished(msg.DeviceName ?? "unknown");
            _logger.LogDebug(
                "SnmpOidReceived OID={Oid} Agent={Agent} Source={Source}",
                msg.Oid,
                msg.AgentIp,
                msg.Source);
        }

        return await next();
    }
}
