using Lextm.SharpSnmpLib;
using MediatR;
using Microsoft.Extensions.Logging;
using SnmpCollector.Configuration;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Pipeline.Handlers;

/// <summary>
/// Terminal MediatR request handler that dispatches SNMP OID values to the correct
/// OpenTelemetry instrument based on the <see cref="SnmpType"/> type code.
///
/// Implements IRequestHandler (not INotificationHandler) so that IPipelineBehavior chain
/// runs: Logging → Exception → Validation → OidResolution → ValueExtraction → this handler.
/// Counter32 and Counter64 raw values are recorded as gauges (Prometheus applies rate()/increase()).
/// Reads pre-extracted values from <see cref="SnmpOidReceived.ExtractedValue"/> and
/// <see cref="SnmpOidReceived.ExtractedStringValue"/> set by ValueExtractionBehavior.
/// Unrecognized type codes are logged at Warning level and dropped.
/// </summary>
public sealed class OtelMetricHandler : IRequestHandler<SnmpOidReceived, Unit>
{
    private readonly ISnmpMetricFactory _metricFactory;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly IHeartbeatLivenessService _heartbeatLiveness;
    private readonly ILogger<OtelMetricHandler> _logger;

    public OtelMetricHandler(
        ISnmpMetricFactory metricFactory,
        PipelineMetricService pipelineMetrics,
        IHeartbeatLivenessService heartbeatLiveness,
        ILogger<OtelMetricHandler> logger)
    {
        _metricFactory = metricFactory;
        _pipelineMetrics = pipelineMetrics;
        _heartbeatLiveness = heartbeatLiveness;
        _logger = logger;
    }

    public Task<Unit> Handle(SnmpOidReceived notification, CancellationToken cancellationToken)
    {
        var deviceName = notification.DeviceName ?? "unknown";

        var metricName = notification.MetricName ?? OidMapService.Unknown;
        var ip = notification.AgentIp.ToString();
        var source = notification.Source.ToString().ToLowerInvariant();

        switch (notification.TypeCode)
        {
            case SnmpType.Integer32:
            case SnmpType.Gauge32:
            case SnmpType.TimeTicks:
            case SnmpType.Counter32:
            case SnmpType.Counter64:
                _metricFactory.RecordGauge(
                    metricName,
                    notification.Oid,
                    deviceName,
                    ip,
                    source,
                    notification.TypeCode.ToString().ToLowerInvariant(),
                    notification.ExtractedValue);
                if (notification.PollDurationMs.HasValue)
                    _metricFactory.RecordGaugeDuration(metricName, notification.Oid, deviceName, ip, source,
                        notification.TypeCode.ToString().ToLowerInvariant(), notification.PollDurationMs.Value);
                _pipelineMetrics.IncrementHandled(deviceName);
                if (deviceName == HeartbeatJobOptions.HeartbeatDeviceName)
                    _heartbeatLiveness.Stamp();
                break;

            case SnmpType.OctetString:
            case SnmpType.IPAddress:
            case SnmpType.ObjectIdentifier:
                var stringVal = notification.ExtractedStringValue ?? string.Empty;
                _metricFactory.RecordInfo(
                    metricName,
                    notification.Oid,
                    deviceName,
                    ip,
                    source,
                    notification.TypeCode.ToString().ToLowerInvariant(),
                    stringVal.Length > 128 ? stringVal[..128] : stringVal);
                if (notification.PollDurationMs.HasValue)
                    _metricFactory.RecordInfoDuration(metricName, notification.Oid, deviceName, ip, source,
                        notification.TypeCode.ToString().ToLowerInvariant(),
                        stringVal.Length > 128 ? stringVal[..128] : stringVal,
                        notification.PollDurationMs.Value);
                _pipelineMetrics.IncrementHandled(deviceName);
                break;

            default:
                _logger.LogWarning(
                    "Unrecognized SnmpType dropped: Oid={Oid} TypeCode={TypeCode} DeviceName={DeviceName} Ip={Ip}",
                    notification.Oid,
                    notification.TypeCode,
                    deviceName,
                    ip);
                break;
        }

        return Task.FromResult(Unit.Value);
    }
}
