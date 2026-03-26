using k8s;
using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Jobs;

/// <summary>
/// Reads and writes the heartbeat lease from Kubernetes on a fixed interval.
/// <para>
/// Writer path (Phase 86): preferred pod creates or renews the <c>{LeaseOptions.Name}-preferred</c>
/// lease with pod identity and renewTime. Gated by <see cref="_isSchedulerReady"/> (set when
/// <see cref="IHostApplicationLifetime.ApplicationStarted"/> fires) and
/// <see cref="PreferredLeaderService.IsPreferredPod"/>. Non-preferred pods silently skip the
/// write path — no log output.
/// </para>
/// <para>
/// Reader path (Phase 85): all pods poll the heartbeat lease unconditionally and update
/// <see cref="PreferredLeaderService.IsPreferredStampFresh"/> based on stamp age.
/// </para>
/// <para>
/// Gate 2 / voluntary yield (Phase 88, ELEC-02): when a non-preferred pod is leader and
/// the preferred pod's heartbeat stamp becomes fresh, the non-preferred pod deletes the
/// leadership lease and calls <see cref="K8sLeaseElection.CancelInnerElection"/> to allow
/// the preferred pod to acquire through the normal LeaderElector flow.
/// </para>
/// <para>
/// Freshness threshold: <c>LeaseOptions.DurationSeconds + 5s</c> (hardcoded clock-skew tolerance).
/// 404 response = stale (lease not yet created or already expired).
/// Transient K8s API errors keep the last known value to avoid flapping.
/// </para>
/// <para>
/// Execute ordering: write-before-read — preferred pod stamps, then reads its own stamp.
/// TTL expiry handles shutdown; no explicit lease delete is performed on the heartbeat lease.
/// </para>
/// </summary>
[DisallowConcurrentExecution]
public sealed class PreferredHeartbeatJob : IJob
{
    private readonly IKubernetes _kubeClient;
    private readonly PreferredLeaderService _preferredLeaderService;
    private readonly LeaseOptions _leaseOptions;
    private readonly ILivenessVectorService _liveness;
    private readonly K8sLeaseElection? _leaseElection;
    private readonly ILogger<PreferredHeartbeatJob> _logger;

    // Writer path fields
    private readonly string _podIdentity;
    private volatile bool _isSchedulerReady;
    private string? _cachedResourceVersion;

    // Static flag: logs once per process lifetime regardless of job instantiation scope.
    // Quartz resolves IJob as transient so an instance field would reset each tick.
    private static bool _hasLoggedStampingStarted;

    public PreferredHeartbeatJob(
        IKubernetes kubeClient,
        PreferredLeaderService preferredLeaderService,
        IOptions<LeaseOptions> leaseOptions,
        IOptions<PodIdentityOptions> podIdentityOptions,
        IHostApplicationLifetime lifetime,
        ILivenessVectorService liveness,
        ILogger<PreferredHeartbeatJob> logger,
        K8sLeaseElection? leaseElection = null)
    {
        _kubeClient = kubeClient;
        _preferredLeaderService = preferredLeaderService;
        _leaseOptions = leaseOptions.Value;
        _podIdentity = podIdentityOptions.Value.PodIdentity ?? Environment.MachineName;
        _liveness = liveness;
        _logger = logger;
        _leaseElection = leaseElection;

        // Set _isSchedulerReady once the host has fully started (all hosted services running).
        // ApplicationStarted is a CancellationToken — use Register, not += event subscription.
        lifetime.ApplicationStarted.Register(() => _isSchedulerReady = true);
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var jobKey = context.JobDetail.Key.Name;

        try
        {
            // Writer path: only on preferred pod, only when scheduler is ready.
            // Non-preferred pods silently skip — no log.
            if (_preferredLeaderService.IsPreferredPod && _isSchedulerReady)
            {
                if (!_hasLoggedStampingStarted)
                {
                    _logger.LogInformation(
                        "Heartbeat stamping started for lease {LeaseName} — preferred pod is ready",
                        $"{_leaseOptions.Name}-preferred");
                    _hasLoggedStampingStarted = true;
                }

                await WriteHeartbeatLeaseAsync(context.CancellationToken);
            }

            // Reader path: all pods, every tick (Phase 85, unchanged).
            await ReadAndUpdateStampFreshnessAsync(context.CancellationToken);

            // Gate 2 (ELEC-02): non-preferred leader yields when preferred pod recovers.
            if (_preferredLeaderService.IsPreferredStampFresh
                && _leaseElection is not null
                && _leaseElection.IsLeader
                && !_preferredLeaderService.IsPreferredPod)
            {
                await YieldLeadershipAsync(context.CancellationToken);
            }
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

    /// <summary>
    /// Creates or renews the heartbeat lease. Uses a create-on-first-tick, replace-with-cached-resourceVersion
    /// pattern to avoid reading before every write. On 409 Conflict during create, reads the
    /// existing lease to get resourceVersion, then falls through to replace.
    /// <para>
    /// AcquireTime is set only on the create path; renewals only update RenewTime.
    /// </para>
    /// </summary>
    private async Task WriteHeartbeatLeaseAsync(CancellationToken ct)
    {
        var leaseName = $"{_leaseOptions.Name}-preferred";
        var now = DateTime.UtcNow;

        // Build the spec without AcquireTime (set only on create branch below).
        var spec = new V1LeaseSpec
        {
            HolderIdentity = _podIdentity,
            LeaseDurationSeconds = _leaseOptions.DurationSeconds,
            RenewTime = now
        };

        try
        {
            if (_cachedResourceVersion is null)
            {
                // First tick or after cache invalidation: attempt to create the lease.
                spec.AcquireTime = now;

                var leaseToCreate = new V1Lease
                {
                    ApiVersion = "coordination.k8s.io/v1",
                    Kind = "Lease",
                    Metadata = new V1ObjectMeta
                    {
                        Name = leaseName,
                        NamespaceProperty = _leaseOptions.Namespace
                    },
                    Spec = spec
                };

                try
                {
                    // CreateNamespacedLeaseAsync: second param is NAMESPACE, not name.
                    // Name comes from body.Metadata.Name.
                    var created = await _kubeClient.CoordinationV1.CreateNamespacedLeaseAsync(
                        leaseToCreate,
                        _leaseOptions.Namespace,
                        cancellationToken: ct);

                    _cachedResourceVersion = created.Metadata.ResourceVersion;
                    return;
                }
                catch (k8s.Autorest.HttpOperationException ex)
                    when (ex.Response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    // Lease already exists (previous pod run or restart) — read to get resourceVersion,
                    // then fall through to the replace block below.
                    var existing = await _kubeClient.CoordinationV1.ReadNamespacedLeaseAsync(
                        leaseName,
                        _leaseOptions.Namespace,
                        cancellationToken: ct);

                    _cachedResourceVersion = existing.Metadata.ResourceVersion;

                    // AcquireTime is already set from the create attempt; clear it so the
                    // replace branch (renewal) does not overwrite the original acquisition time.
                    spec.AcquireTime = null;
                }
            }

            // Replace using cached resourceVersion. ReplaceNamespacedLeaseAsync: second param IS name.
            var leaseToReplace = new V1Lease
            {
                ApiVersion = "coordination.k8s.io/v1",
                Kind = "Lease",
                Metadata = new V1ObjectMeta
                {
                    Name = leaseName,
                    NamespaceProperty = _leaseOptions.Namespace,
                    ResourceVersion = _cachedResourceVersion
                },
                Spec = spec  // AcquireTime is null on the renewal path
            };

            var replaced = await _kubeClient.CoordinationV1.ReplaceNamespacedLeaseAsync(
                leaseToReplace,
                leaseName,
                _leaseOptions.Namespace,
                cancellationToken: ct);

            _cachedResourceVersion = replaced.Metadata.ResourceVersion;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (k8s.Autorest.HttpOperationException ex)
            when (ex.Response.StatusCode == System.Net.HttpStatusCode.Conflict
               || ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // resourceVersion stale (concurrent write) or lease disappeared — invalidate cache.
            // Next tick will attempt create again (or read on 409).
            _cachedResourceVersion = null;
            _logger.LogWarning(
                "Heartbeat lease {LeaseName} write conflict or missing — cache invalidated, will retry next tick",
                leaseName);
        }
        catch (Exception ex)
        {
            // Transient K8s API error — log warning; do not crash the job.
            // Liveness stamp still fires in Execute's finally block.
            _logger.LogWarning(ex,
                "Transient K8s API error writing heartbeat lease {LeaseName}",
                leaseName);
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

    /// <summary>
    /// Voluntary yield: deletes the leadership lease then cancels the inner election,
    /// allowing the preferred pod to acquire through the normal LeaderElector flow.
    /// Mirrors the delete-then-cancel pattern from <see cref="K8sLeaseElection.StopAsync"/>.
    /// </summary>
    private async Task YieldLeadershipAsync(CancellationToken ct)
    {
        try
        {
            await _kubeClient.CoordinationV1.DeleteNamespacedLeaseAsync(
                _leaseOptions.Name,
                _leaseOptions.Namespace,
                cancellationToken: ct);

            _logger.LogInformation(
                "Voluntary yield: deleted leadership lease {LeaseName} — preferred pod recovered",
                _leaseOptions.Name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Voluntary yield: failed to delete leadership lease {LeaseName} — cancelling inner election anyway",
                _leaseOptions.Name);
        }

        _leaseElection!.CancelInnerElection();
    }
}
