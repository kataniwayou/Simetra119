using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Quartz;
using SnmpCollector.Services;

namespace SnmpCollector.HealthChecks;

/// <summary>
/// Readiness health check (HLTH-02). Returns Healthy when the trap listener is bound
/// and the Quartz scheduler is running. Does not require devices to be configured --
/// a pod with no poll targets is still ready if it can receive traps.
/// Kubernetes readiness probe controls load balancer routing.
/// </summary>
public sealed class ReadinessHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISchedulerFactory _schedulerFactory;

    public ReadinessHealthCheck(
        IServiceProvider serviceProvider,
        ISchedulerFactory schedulerFactory)
    {
        _serviceProvider = serviceProvider;
        _schedulerFactory = schedulerFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // HLTH-02: Trap listener is bound = UDP socket is active and receiving traps
        var listener = _serviceProvider.GetServices<IHostedService>()
            .OfType<SnmpTrapListenerService>()
            .FirstOrDefault();

        if (listener?.IsBound != true)
        {
            return HealthCheckResult.Unhealthy("Trap listener is not bound");
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
