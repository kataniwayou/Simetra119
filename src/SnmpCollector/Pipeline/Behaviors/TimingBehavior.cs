using System.Diagnostics;
using MediatR;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Pipeline.Behaviors;

/// <summary>
/// Outermost pipeline behavior that measures full pipeline processing time for SnmpOidReceived
/// messages and records it as snmp.pipeline.duration (Q050). Wraps all other behaviors including
/// ExceptionBehavior, so pipeline exceptions are still measured.
/// </summary>
public sealed class TimingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly PipelineMetricService _metrics;

    public TimingBehavior(PipelineMetricService metrics)
    {
        _metrics = metrics;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        if (request is SnmpOidReceived msg)
        {
            _metrics.RecordPipelineDuration(msg.DeviceName ?? "unknown", sw.Elapsed.TotalMilliseconds);
        }

        return response;
    }
}
