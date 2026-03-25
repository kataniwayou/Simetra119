using k8s;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Jobs;

/// <summary>
/// Reads the heartbeat lease from Kubernetes on a fixed interval and updates
/// <see cref="PreferredLeaderService.IsPreferredStampFresh"/> based on stamp age.
/// <para>
/// Reader path only (Phase 85): all pods poll the heartbeat lease unconditionally.
/// Writer path (preferred pod stamps the lease) is added in Phase 86.
/// </para>
/// <para>
/// Freshness threshold: <c>LeaseOptions.DurationSeconds + 5s</c> (hardcoded clock-skew tolerance).
/// 404 response = stale (lease not yet created or already expired).
/// Transient K8s API errors keep the last known value to avoid flapping.
/// </para>
/// </summary>
[DisallowConcurrentExecution]
public sealed class PreferredHeartbeatJob : IJob
{
    private readonly IKubernetes _kubeClient;
    private readonly PreferredLeaderService _preferredLeaderService;
    private readonly LeaseOptions _leaseOptions;
    private readonly ILivenessVectorService _liveness;
    private readonly ILogger<PreferredHeartbeatJob> _logger;

    public PreferredHeartbeatJob(
        IKubernetes kubeClient,
        PreferredLeaderService preferredLeaderService,
        IOptions<LeaseOptions> leaseOptions,
        ILivenessVectorService liveness,
        ILogger<PreferredHeartbeatJob> logger)
    {
        _kubeClient = kubeClient;
        _preferredLeaderService = preferredLeaderService;
        _leaseOptions = leaseOptions.Value;
        _liveness = liveness;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var jobKey = context.JobDetail.Key.Name;

        try
        {
            await ReadAndUpdateStampFreshnessAsync(context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "PreferredHeartbeat job {JobKey} failed",
                jobKey);
        }
        finally
        {
            _liveness.Stamp(jobKey);
        }
    }

    private async Task ReadAndUpdateStampFreshnessAsync(CancellationToken ct)
    {
        var heartbeatLeaseName = $"{_leaseOptions.Name}-preferred";

        try
        {
            var lease = await _kubeClient.CoordinationV1.ReadNamespacedLeaseAsync(
                heartbeatLeaseName,
                _leaseOptions.Namespace,
                cancellationToken: ct);

            var stampTime = lease.Spec?.RenewTime ?? lease.Spec?.AcquireTime;

            if (stampTime is null)
            {
                _preferredLeaderService.UpdateStampFreshness(false);
                return;
            }

            var normalizedStampTime = DateTime.SpecifyKind(stampTime.Value, DateTimeKind.Utc);
            var threshold = TimeSpan.FromSeconds(_leaseOptions.DurationSeconds + 5);
            var age = DateTime.UtcNow - normalizedStampTime;

            _preferredLeaderService.UpdateStampFreshness(age <= threshold);
        }
        catch (k8s.Autorest.HttpOperationException ex)
            when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // 404 = lease not yet created or already expired — treat as stale
            _preferredLeaderService.UpdateStampFreshness(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Transient K8s API error — keep last known value to avoid flapping
            _logger.LogWarning(ex,
                "Transient K8s API error reading lease {LeaseName} — keeping last value",
                heartbeatLeaseName);
        }
    }
}
