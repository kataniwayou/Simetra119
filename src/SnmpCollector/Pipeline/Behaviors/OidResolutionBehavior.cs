using MediatR;
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

    public OidResolutionBehavior(IOidMapService oidMapService)
    {
        _oidMapService = oidMapService;
    }

    public async Task<TResponse> Handle(
        TNotification notification,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (notification is SnmpOidReceived msg)
        {
            msg.MetricName = _oidMapService.Resolve(msg.Oid);
        }

        return await next();
    }
}
