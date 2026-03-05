using Microsoft.Extensions.Diagnostics.HealthChecks;
using SnmpCollector.Pipeline;

namespace SnmpCollector.HealthChecks;

/// <summary>
/// Startup health check (HLTH-01). Returns Healthy when the OID map is loaded
/// and poll definitions are registered with Quartz. Verified by checking that
/// <see cref="IJobIntervalRegistry"/> has at least one registered entry (populated
/// during AddSnmpScheduling). Kubernetes startup probe gates readiness and liveness
/// probes until this succeeds.
/// </summary>
public sealed class StartupHealthCheck : IHealthCheck
{
    private readonly IJobIntervalRegistry _intervals;

    public StartupHealthCheck(IJobIntervalRegistry intervals)
    {
        _intervals = intervals;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // HLTH-01: "correlation" job is always registered if AddSnmpScheduling ran.
        // If the registry has this key, scheduling completed and OID map is loaded
        // (OidMapService is resolved before scheduling starts).
        var hasJobs = _intervals.TryGetInterval("correlation", out _);

        return Task.FromResult(hasJobs
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Poll definitions not yet registered with Quartz"));
    }
}
