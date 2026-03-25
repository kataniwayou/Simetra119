using MediatR;
using Microsoft.Extensions.Logging;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Pipeline.Behaviors;

/// <summary>
/// Second pipeline behavior (inside LoggingBehavior) that catches all unhandled exceptions
/// from downstream behaviors and handlers. Logs a Warning, increments the pipeline error
/// counter, and swallows the exception -- never re-throws.
/// Open generic over TNotification so MediatR registers it for all notification types,
/// but it acts as a transparent pass-through for non-SnmpOidReceived notifications as well
/// (the try/catch is always active to guard any pipeline errors).
/// </summary>
public sealed class ExceptionBehavior<TNotification, TResponse>
    : IPipelineBehavior<TNotification, TResponse>
    where TNotification : notnull
{
    private readonly ILogger<ExceptionBehavior<TNotification, TResponse>> _logger;
    private readonly PipelineMetricService _metrics;

    public ExceptionBehavior(
        ILogger<ExceptionBehavior<TNotification, TResponse>> logger,
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
        try
        {
            return await next();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Pipeline exception for {NotificationType}",
                typeof(TNotification).Name);

            var deviceName = (notification is SnmpOidReceived msg) ? msg.DeviceName ?? "unknown" : "unknown";
            _metrics.IncrementErrors(deviceName);

            return default!;
        }
    }
}
