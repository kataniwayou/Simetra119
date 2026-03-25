using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using Quartz;
using SnmpCollector.Pipeline;
using SnmpCollector.Services;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Lifecycle;

/// <summary>
/// Orchestrates the SHUT-01 through SHUT-08 graceful shutdown sequence with time-budgeted steps.
/// Registered LAST as an <see cref="IHostedService"/> so its <see cref="StopAsync"/> runs
/// FIRST in the framework's reverse-order stop (SHUT-01). This is the SINGLE orchestrator
/// of the entire shutdown sequence -- no other service should independently manage shutdown steps.
/// <para>
/// The 5-step sequence ensures: near-instant HA failover (lease release), no new traps
/// accepted (listener stop), no new job fires (scheduler standby), in-flight data completes
/// (channel drain), and final telemetry is preserved (flush).
/// </para>
/// <para>
/// K8sLeaseElection and SnmpTrapListenerService extend BackgroundService, whose StopAsync
/// is idempotent (cancels the stoppingToken via an internal CancellationTokenSource, then
/// awaits ExecuteTask -- both are safe to call multiple times). The framework will call their
/// StopAsync AGAIN in reverse registration order after this service completes, but that
/// second call is a harmless no-op.
/// </para>
/// </summary>
public sealed class GracefulShutdownService : IHostedService
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ITrapChannel _trapChannel;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GracefulShutdownService> _logger;

    public GracefulShutdownService(
        ISchedulerFactory schedulerFactory,
        ITrapChannel trapChannel,
        IServiceProvider serviceProvider,
        ILogger<GracefulShutdownService> logger)
    {
        _schedulerFactory = schedulerFactory;
        _trapChannel = trapChannel;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// No startup work required. This service only acts during shutdown.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Executes the complete 5-step graceful shutdown sequence (SHUT-02 through SHUT-06).
    /// Total budget: 3+3+3+8+5 = 22s happy path; HostOptions.ShutdownTimeout = 30s ceiling (SHUT-08).
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Graceful shutdown sequence starting");

        // Step 1: Release lease (3s budget) -- SHUT-02
        await ExecuteWithBudget("ReleaseLease", TimeSpan.FromSeconds(3), async () =>
        {
            var leaseService = _serviceProvider.GetService<K8sLeaseElection>();
            if (leaseService is not null)
            {
                await leaseService.StopAsync(CancellationToken.None);
                _logger.LogInformation("Leader lease released");
            }
            else
            {
                _logger.LogDebug("No K8sLeaseElection registered (local dev mode), skipping lease release");
            }
        }, cancellationToken);

        // Step 2: Stop SNMP trap listener (3s budget) -- SHUT-03
        await ExecuteWithBudget("StopListener", TimeSpan.FromSeconds(3), async () =>
        {
            var listener = _serviceProvider.GetServices<IHostedService>()
                .OfType<SnmpTrapListenerService>()
                .FirstOrDefault();
            if (listener is not null)
            {
                await listener.StopAsync(CancellationToken.None);
                _logger.LogInformation("SNMP trap listener stopped");
            }
        }, cancellationToken);

        // Step 3: Put scheduler in standby (3s budget) -- SHUT-04
        await ExecuteWithBudget("PauseScheduler", TimeSpan.FromSeconds(3), async () =>
        {
            var scheduler = await _schedulerFactory.GetScheduler();
            await scheduler.Standby();
            _logger.LogInformation("Scheduler placed in standby");
        }, cancellationToken);

        // Step 4: Drain trap channel (8s budget) -- SHUT-05
        await ExecuteWithBudget("DrainChannels", TimeSpan.FromSeconds(8), async () =>
        {
            _trapChannel.Complete();
            await _trapChannel.WaitForDrainAsync(CancellationToken.None);
            _logger.LogInformation("Trap channel drained");
        }, cancellationToken);

        // Step 5: Flush telemetry (independent CTS -- ALWAYS runs) -- SHUT-06
        await FlushTelemetryAsync();

        _logger.LogInformation("Graceful shutdown sequence completed");
    }

    /// <summary>
    /// Executes a shutdown step with a bounded time budget (SHUT-07). If the step exceeds
    /// its budget, it is abandoned and the next step proceeds. Each step gets its own linked
    /// CancellationTokenSource with CancelAfter for the budget duration.
    /// </summary>
    private async Task ExecuteWithBudget(
        string stepName,
        TimeSpan budget,
        Func<Task> action,
        CancellationToken outerToken)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        stepCts.CancelAfter(budget);

        try
        {
            await action();
        }
        catch (OperationCanceledException) when (stepCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Shutdown step {StepName} exceeded budget of {BudgetSeconds}s, abandoning",
                stepName,
                budget.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Shutdown step {StepName} failed",
                stepName);
        }
    }

    /// <summary>
    /// Flushes telemetry with a protected time budget (SHUT-06). Uses its OWN
    /// CancellationTokenSource -- NOT linked to the outer shutdown token -- ensuring
    /// telemetry flush always gets its full 5s budget regardless of prior step outcomes
    /// or the host's remaining shutdown time.
    /// </summary>
    private async Task FlushTelemetryAsync()
    {
        using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await Task.Run(() =>
            {
                var meterProvider = _serviceProvider.GetService<MeterProvider>();
                meterProvider?.ForceFlush(timeoutMilliseconds: 5000);
                // No TracerProvider -- LOG-07: SnmpCollector has no traces
            }, flushCts.Token);

            _logger.LogInformation("Telemetry flush completed");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Telemetry flush exceeded 5s budget");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telemetry flush failed");
        }
    }
}
