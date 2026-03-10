using MediatR;
using Microsoft.Extensions.Logging;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Pipeline.Behaviors;

/// <summary>
/// Pipeline behavior that resolves the OID on every SnmpOidReceived notification to a human-readable
/// metric name via IOidMapService, enriching the notification in-place before passing it downstream.
/// Always calls next() — never short-circuits the pipeline.
/// Other notification types pass through to next() unmodified.
/// </summary>
public sealed class OidResolutionBehavior<TNotification, TResponse>
    : IPipelineBehavior<TNotification, TResponse>
    where TNotification : notnull
{
    private readonly IOidMapService _oidMapService;
    private readonly ILogger<OidResolutionBehavior<TNotification, TResponse>> _logger;

    public OidResolutionBehavior(
        IOidMapService oidMapService,
        ILogger<OidResolutionBehavior<TNotification, TResponse>> logger)
    {
        _oidMapService = oidMapService;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TNotification notification,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (notification is SnmpOidReceived msg)
        {
            msg.MetricName = _oidMapService.Resolve(msg.Oid);

            if (msg.MetricName == OidMapService.Unknown)
                _logger.LogDebug("OID {Oid} not found in OidMap", msg.Oid);
            else
                _logger.LogDebug("OID {Oid} resolved to {MetricName}", msg.Oid, msg.MetricName);
        }

        return await next();
    }
}
