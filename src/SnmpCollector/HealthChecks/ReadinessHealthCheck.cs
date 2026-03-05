using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;
using SnmpCollector.Pipeline;

namespace SnmpCollector.HealthChecks;

/// <summary>
/// Readiness health check (HLTH-02). Returns Healthy when the trap listener is operational
/// (proxied by device channels existing) and the device registry is populated.
/// Checks: DeviceNames.Count > 0 AND Quartz scheduler IsStarted AND not IsShutdown.
/// Kubernetes readiness probe controls load balancer routing.
/// </summary>
public sealed class ReadinessHealthCheck : IHealthCheck
{
    private readonly IDeviceChannelManager _channels;
    private readonly ISchedulerFactory _schedulerFactory;

    public ReadinessHealthCheck(
        IDeviceChannelManager channels,
        ISchedulerFactory schedulerFactory)
    {
        _channels = channels;
        _schedulerFactory = schedulerFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // HLTH-02: Device channels exist = device registry populated + trap listener can function
        if (_channels.DeviceNames.Count == 0)
        {
            return HealthCheckResult.Unhealthy("No device channels registered");
        }

        // HLTH-02: Quartz scheduler is running = application is past startup phase
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        if (!scheduler.IsStarted || scheduler.IsShutdown)
        {
            return HealthCheckResult.Unhealthy("Quartz scheduler is not running");
        }

        return HealthCheckResult.Healthy();
    }
}
