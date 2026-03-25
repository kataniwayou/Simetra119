using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Services;

/// <summary>
/// Hosted service that logs a startup summary of registered poll jobs when the host starts.
/// <para>
/// Reads all devices from <see cref="IDeviceRegistry"/> and sums their poll group counts to
/// produce the same job/thread-pool figures calculated in <see cref="Extensions.ServiceCollectionExtensions.AddSnmpScheduling"/>.
/// Log format (locked from CONTEXT.md): "Registered {N} poll jobs across {M} devices, thread pool size: {T}"
/// </para>
/// </summary>
public sealed class PollSchedulerStartupService : IHostedService
{
    private readonly IDeviceRegistry _registry;
    private readonly ILogger<PollSchedulerStartupService> _logger;

    public PollSchedulerStartupService(
        IDeviceRegistry registry,
        ILogger<PollSchedulerStartupService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var devices = _registry.AllDevices;
        var pollJobCount = devices.Sum(d => d.PollGroups.Count);
        var threadPoolSize = pollJobCount + 2; // +1 CorrelationJob, +1 HeartbeatJob

        _logger.LogInformation(
            "Registered {N} poll jobs across {M} devices, thread pool size: {T}",
            pollJobCount,
            devices.Count,
            threadPoolSize);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
