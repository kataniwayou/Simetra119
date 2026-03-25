using Microsoft.Extensions.Diagnostics.HealthChecks;
using SnmpCollector.Pipeline;

namespace SnmpCollector.HealthChecks;

/// <summary>
/// Startup health check (HLTH-01). Returns Healthy when the OID map is loaded,
/// poll definitions are registered with Quartz, and <see cref="IDeviceRegistry"/>
/// contains at least one device. Kubernetes startup probe gates readiness and
/// liveness probes until this succeeds.
/// </summary>
public sealed class StartupHealthCheck : IHealthCheck
{
    private readonly IJobIntervalRegistry _intervals;
    private readonly IDeviceRegistry _devices;

    public StartupHealthCheck(IJobIntervalRegistry intervals, IDeviceRegistry devices)
    {
        _intervals = intervals;
        _devices = devices;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // HLTH-01: "correlation" job is always registered if AddSnmpScheduling ran.
        // If the registry has this key, scheduling completed and OID map is loaded
        // (OidMapService is resolved before scheduling starts).
        var hasJobs = _intervals.TryGetInterval("correlation", out _);
        var hasDevices = _devices.AllDevices.Count > 0;

        var data = new Dictionary<string, object>
        {
            ["jobsRegistered"] = hasJobs,
            ["devicesLoaded"] = hasDevices
        };

        var healthy = hasJobs && hasDevices;

        if (healthy)
            return Task.FromResult(HealthCheckResult.Healthy(data: data));

        var reason = (!hasJobs, !hasDevices) switch
        {
            (true, true) => "Poll definitions not registered and no devices loaded",
            (true, false) => "Poll definitions not yet registered with Quartz",
            (false, true) => "No devices loaded in DeviceRegistry",
            _ => string.Empty // unreachable
        };

        return Task.FromResult(HealthCheckResult.Unhealthy(reason, data: data));
    }
}
