using Lextm.SharpSnmpLib;
using MediatR;
using Microsoft.Extensions.Logging;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Pipeline.Handlers;

/// <summary>
/// Terminal MediatR request handler that dispatches SNMP OID values to the correct
/// OpenTelemetry instrument based on the <see cref="SnmpType"/> type code.
///
/// Implements IRequestHandler (not INotificationHandler) so that IPipelineBehavior chain
/// runs: Logging → Exception → Validation → OidResolution → this handler.
/// Counter32 and Counter64 raw values are recorded as gauges (Prometheus applies rate()/increase()).
/// Unrecognized type codes are logged at Warning level and dropped.
/// </summary>
public sealed class OtelMetricHandler : IRequestHandler<SnmpOidReceived, Unit>
{
    private readonly ISnmpMetricFactory _metricFactory;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly ILogger<OtelMetricHandler> _logger;

    public OtelMetricHandler(
        ISnmpMetricFactory metricFactory,
        PipelineMetricService pipelineMetrics,
        ILogger<OtelMetricHandler> logger)
    {
        _metricFactory = metricFactory;
        _pipelineMetrics = pipelineMetrics;
        _logger = logger;
    }

    public Task<Unit> Handle(SnmpOidReceived notification, CancellationToken cancellationToken)
    {
        var metricName = notification.MetricName ?? OidMapService.Unknown;
        var agent = notification.DeviceName ?? notification.AgentIp.ToString();
        var source = notification.Source.ToString().ToLowerInvariant();

        switch (notification.TypeCode)
        {
            case SnmpType.Integer32:
                _metricFactory.RecordGauge(
                    metricName,
                    notification.Oid,
                    agent,
                    source,
                    "integer32",
                    ((Integer32)notification.Value).ToInt32());
                _pipelineMetrics.IncrementHandled();
                break;

            case SnmpType.Gauge32:
                _metricFactory.RecordGauge(
                    metricName,
                    notification.Oid,
                    agent,
                    source,
                    "gauge32",
                    ((Gauge32)notification.Value).ToUInt32());
                _pipelineMetrics.IncrementHandled();
                break;

            case SnmpType.TimeTicks:
                _metricFactory.RecordGauge(
                    metricName,
                    notification.Oid,
                    agent,
                    source,
                    "timeticks",
                    ((TimeTicks)notification.Value).ToUInt32());
                _pipelineMetrics.IncrementHandled();
                break;

            case SnmpType.Counter32:
                _metricFactory.RecordGauge(
                    metricName,
                    notification.Oid,
                    agent,
                    source,
                    "counter32",
                    ((Counter32)notification.Value).ToUInt32());
                _pipelineMetrics.IncrementHandled();
                break;

            case SnmpType.Counter64:
                _metricFactory.RecordGauge(
                    metricName,
                    notification.Oid,
                    agent,
                    source,
                    "counter64",
                    ((Counter64)notification.Value).ToUInt64());
                _pipelineMetrics.IncrementHandled();
                break;

            case SnmpType.OctetString:
                _metricFactory.RecordInfo(
                    metricName,
                    notification.Oid,
                    agent,
                    source,
                    "octetstring",
                    notification.Value.ToString());
                _pipelineMetrics.IncrementHandled();
                break;

            case SnmpType.IPAddress:
                _metricFactory.RecordInfo(
                    metricName,
                    notification.Oid,
                    agent,
                    source,
                    "ipaddress",
                    notification.Value.ToString());
                _pipelineMetrics.IncrementHandled();
                break;

            case SnmpType.ObjectIdentifier:
                _metricFactory.RecordInfo(
                    metricName,
                    notification.Oid,
                    agent,
                    source,
                    "objectidentifier",
                    notification.Value.ToString());
                _pipelineMetrics.IncrementHandled();
                break;

            default:
                _logger.LogWarning(
                    "Unrecognized SnmpType dropped: Oid={Oid} TypeCode={TypeCode} Agent={Agent}",
                    notification.Oid,
                    notification.TypeCode,
                    agent);
                break;
        }

        return Task.FromResult(Unit.Value);
    }
}
