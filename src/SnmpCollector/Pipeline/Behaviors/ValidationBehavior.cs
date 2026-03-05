using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Pipeline.Behaviors;

/// <summary>
/// Pipeline behavior that validates every SnmpOidReceived notification before it proceeds.
/// Rejects notifications with malformed OIDs or from unknown devices.
/// On rejection: logs at Warning, increments the rejected counter, and short-circuits (no next() call).
/// Other notification types pass through to next() unmodified.
/// </summary>
public sealed class ValidationBehavior<TNotification, TResponse>
    : IPipelineBehavior<TNotification, TResponse>
    where TNotification : notnull
{
    /// <summary>
    /// Matches valid OID strings: one or more decimal arcs separated by dots, with at least 2 arcs.
    /// Examples: "1.3", "1.3.6.1.2.1.1.1.0" — rejects empty, single arc, or non-numeric arcs.
    /// </summary>
    private static readonly Regex OidPattern = new(@"^\d+(\.\d+){1,}$", RegexOptions.Compiled);

    private readonly ILogger<ValidationBehavior<TNotification, TResponse>> _logger;
    private readonly PipelineMetricService _metrics;
    private readonly IDeviceRegistry _deviceRegistry;

    public ValidationBehavior(
        ILogger<ValidationBehavior<TNotification, TResponse>> logger,
        PipelineMetricService metrics,
        IDeviceRegistry deviceRegistry)
    {
        _logger = logger;
        _metrics = metrics;
        _deviceRegistry = deviceRegistry;
    }

    public async Task<TResponse> Handle(
        TNotification notification,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (notification is not SnmpOidReceived msg)
        {
            return await next();
        }

        // OID format check: must be at least 2 numeric arcs separated by dots.
        if (!OidPattern.IsMatch(msg.Oid))
        {
            _logger.LogWarning(
                "SnmpOidReceived rejected: Oid={Oid} AgentIp={AgentIp} Reason={Reason}",
                msg.Oid,
                msg.AgentIp,
                "InvalidOidFormat");

            _metrics.IncrementRejected();
            return default!;
        }

        // Unknown device check: if DeviceName is not already set, resolve from registry by IP.
        if (msg.DeviceName is null)
        {
            if (!_deviceRegistry.TryGetDevice(msg.AgentIp, out var device))
            {
                _logger.LogWarning(
                    "SnmpOidReceived rejected: Oid={Oid} AgentIp={AgentIp} Reason={Reason}",
                    msg.Oid,
                    msg.AgentIp,
                    "UnknownDevice");

                _metrics.IncrementRejected();
                return default!;
            }

            // Enrich the notification in-place with the resolved device name.
            msg.DeviceName = device.Name;
        }

        return await next();
    }
}
