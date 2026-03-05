using MediatR;
using Microsoft.Extensions.Logging;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Pipeline.Behaviors;

/// <summary>
/// Outermost pipeline behavior that logs every SnmpOidReceived notification at Debug level.
/// Open generic over TNotification so MediatR registers it for all notification types,
/// but logging only fires when the notification is SnmpOidReceived.
/// Always calls next() -- never short-circuits the pipeline.
/// </summary>
public sealed class LoggingBehavior<TNotification, TResponse>
    : IPipelineBehavior<TNotification, TResponse>
    where TNotification : notnull
{
    private readonly ILogger<LoggingBehavior<TNotification, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TNotification, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TNotification notification,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (notification is SnmpOidReceived msg)
        {
            _logger.LogDebug(
                "SnmpOidReceived OID={Oid} Agent={Agent} Source={Source}",
                msg.Oid,
                msg.AgentIp,
                msg.Source);
        }

        return await next();
    }
}
