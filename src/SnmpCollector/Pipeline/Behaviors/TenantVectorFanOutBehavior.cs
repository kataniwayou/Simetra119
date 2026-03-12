using MediatR;
using Microsoft.Extensions.Logging;
using SnmpCollector.Configuration;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Pipeline.Behaviors;

/// <summary>
/// Pipeline behavior that routes resolved SNMP samples to matching tenant vector slots.
/// Runs after ValueExtractionBehavior. Catches its own exceptions and always calls next()
/// so OtelMetricHandler fires regardless of fan-out success or failure.
/// </summary>
public sealed class TenantVectorFanOutBehavior<TNotification, TResponse>
    : IPipelineBehavior<TNotification, TResponse>
    where TNotification : notnull
{
    private readonly ITenantVectorRegistry _registry;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly ILogger<TenantVectorFanOutBehavior<TNotification, TResponse>> _logger;

    public TenantVectorFanOutBehavior(
        ITenantVectorRegistry registry,
        IDeviceRegistry deviceRegistry,
        PipelineMetricService pipelineMetrics,
        ILogger<TenantVectorFanOutBehavior<TNotification, TResponse>> logger)
    {
        _registry = registry;
        _deviceRegistry = deviceRegistry;
        _pipelineMetrics = pipelineMetrics;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TNotification notification,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (notification is SnmpOidReceived msg)
        {
            // Filter: skip unresolved OIDs before any routing attempt
            var metricName = msg.MetricName;
            if (metricName is not null && metricName != OidMapService.Unknown)
            {
                try
                {
                    // Heartbeat bypass: "Simetra" device is not in DeviceRegistry.
                    // Route directly using known heartbeat coordinates.
                    if (string.Equals(msg.DeviceName, HeartbeatJobOptions.HeartbeatDeviceName, StringComparison.Ordinal))
                    {
                        if (_registry.TryRoute("127.0.0.1", 0, metricName, out var heartbeatHolders))
                        {
                            foreach (var holder in heartbeatHolders)
                            {
                                holder.WriteValue(msg.ExtractedValue, msg.ExtractedStringValue, msg.TypeCode, msg.Source);
                                _pipelineMetrics.IncrementTenantVectorRouted(msg.DeviceName!);
                            }
                        }
                    }
                    // Resolve port from DeviceRegistry (PIP-02)
                    else if (_deviceRegistry.TryGetDeviceByName(msg.DeviceName!, out var device))
                    {
                        var ip = msg.AgentIp.ToString();
                        if (_registry.TryRoute(ip, device.Port, metricName, out var holders))
                        {
                            foreach (var holder in holders)
                            {
                                holder.WriteValue(msg.ExtractedValue, msg.ExtractedStringValue, msg.TypeCode, msg.Source);
                                _pipelineMetrics.IncrementTenantVectorRouted(msg.DeviceName!);
                                if (holder.TimeSeriesSize > 1)
                                    _logger.LogDebug(
                                        "TimeSeries write: metric={MetricName} ip={Ip} samples={SampleCount}/{TimeSeriesSize}",
                                        holder.MetricName, holder.Ip, holder.ReadSeries().Length, holder.TimeSeriesSize);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "TenantVectorFanOut exception for {DeviceName}", msg.DeviceName);
                }
            }
        }

        // CRITICAL: next() is OUTSIDE the try/catch — always called regardless of fan-out success/failure
        return await next();
    }
}
