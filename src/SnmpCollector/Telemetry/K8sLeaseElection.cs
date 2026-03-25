using k8s;
using k8s.LeaderElection;
using k8s.LeaderElection.ResourceLock;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Telemetry;

/// <summary>
/// Kubernetes Lease-based leader election service. Implements both <see cref="BackgroundService"/>
/// (to run the leader election loop) and <see cref="ILeaderElection"/> (to expose leadership
/// status to consumers like <c>MetricRoleGatedExporter</c> and
/// <c>SnmpLogEnrichmentProcessor</c>).
/// <para>
/// Uses the coordination.k8s.io/v1 Lease API via <see cref="LeaseLock"/> and
/// <see cref="LeaderElector"/>. On SIGTERM, the lease is explicitly deleted for near-instant
/// failover (HA-08, SC#3) rather than waiting for TTL expiry.
/// </para>
/// <para>
/// Gate 1 (ELEC-01): Non-preferred pods delay their election attempt by
/// <see cref="LeaseOptions.DurationSeconds"/> when the preferred pod's heartbeat stamp is
/// fresh. Preferred pods (ELEC-04) are never subject to backoff. The outer loop with
/// <see cref="_innerCts"/> enables Phase 88 voluntary yield via
/// <see cref="CancelInnerElection"/>.
/// </para>
/// </summary>
public sealed class K8sLeaseElection : BackgroundService, ILeaderElection
{
    private readonly LeaseOptions _leaseOptions;
    private readonly PodIdentityOptions _podIdentityOptions;
    private readonly IKubernetes _kubeClient;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<K8sLeaseElection> _logger;
    private readonly PreferredLeaderService _preferredLeaderService;
    private readonly IPreferredStampReader _stampReader;

    /// <summary>
    /// Thread-safe leadership flag. Written only by the LeaderElector event handlers
    /// (single writer), read by multiple consumers via <see cref="IsLeader"/>.
    /// </summary>
    private volatile bool _isLeader;

    /// <summary>
    /// The inner cancellation token source for the current election attempt.
    /// Created fresh each outer-loop iteration; disposed via <c>using</c>.
    /// Exposed through <see cref="CancelInnerElection"/> for Phase 88 voluntary yield.
    /// Written from <see cref="ExecuteAsync"/> (single writer); read from
    /// <see cref="CancelInnerElection"/> (potentially separate thread) — volatile
    /// ensures the reader sees the latest value without a full lock.
    /// </summary>
    private volatile CancellationTokenSource? _innerCts;

    /// <summary>
    /// Initializes a new instance of <see cref="K8sLeaseElection"/>.
    /// </summary>
    /// <param name="leaseOptions">Lease configuration (name, namespace, durations).</param>
    /// <param name="podIdentityOptions">Pod identity configuration.</param>
    /// <param name="kubeClient">Kubernetes API client for lease operations.</param>
    /// <param name="lifetime">Application lifetime for shutdown coordination.</param>
    /// <param name="logger">Logger for leadership state changes.</param>
    /// <param name="preferredLeaderService">
    /// Resolves whether this pod is the configured preferred leader. Used to skip Gate 1
    /// backoff for the preferred pod (ELEC-04).
    /// </param>
    /// <param name="stampReader">
    /// Reads the in-memory freshness flag updated by the heartbeat service. Used by Gate 1
    /// to determine whether the preferred pod is currently alive (ELEC-01).
    /// </param>
    public K8sLeaseElection(
        IOptions<LeaseOptions> leaseOptions,
        IOptions<PodIdentityOptions> podIdentityOptions,
        IKubernetes kubeClient,
        IHostApplicationLifetime lifetime,
        ILogger<K8sLeaseElection> logger,
        PreferredLeaderService preferredLeaderService,
        IPreferredStampReader stampReader)
    {
        _leaseOptions = leaseOptions?.Value ?? throw new ArgumentNullException(nameof(leaseOptions));
        _podIdentityOptions = podIdentityOptions?.Value ?? throw new ArgumentNullException(nameof(podIdentityOptions));
        _kubeClient = kubeClient ?? throw new ArgumentNullException(nameof(kubeClient));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _preferredLeaderService = preferredLeaderService ?? throw new ArgumentNullException(nameof(preferredLeaderService));
        _stampReader = stampReader ?? throw new ArgumentNullException(nameof(stampReader));
    }

    /// <inheritdoc />
    public bool IsLeader => _isLeader;

    /// <inheritdoc />
    public string CurrentRole => _isLeader ? "leader" : "follower";

    /// <summary>
    /// Cancels the current inner election attempt, causing the outer loop
    /// to restart and re-evaluate backoff. Used by voluntary yield (Phase 88).
    /// No-op if no inner election is in progress.
    /// </summary>
    public void CancelInnerElection()
    {
        try { _innerCts?.Cancel(); }
        catch (ObjectDisposedException) { /* already disposed — cancel was redundant */ }
    }

    /// <summary>
    /// Runs the leader election loop using <see cref="LeaderElector.RunAndTryToHoldLeadershipForeverAsync"/>.
    /// <para>
    /// An outer <c>while</c> loop wraps each election attempt to support Gate 1 backoff and
    /// Phase 88 voluntary yield. On each iteration the Gate 1 condition is checked first:
    /// if this pod is not the preferred pod and the preferred pod's stamp is fresh, the
    /// iteration sleeps for <see cref="LeaseOptions.DurationSeconds"/> before trying again.
    /// This gives the preferred pod a head start on leadership acquisition.
    /// </para>
    /// <para>
    /// When <c>_innerCts</c> is cancelled (Phase 88 voluntary yield), the
    /// <see cref="OperationCanceledException"/> is caught and the loop continues —
    /// re-evaluating Gate 1 before the next election attempt. When <c>stoppingToken</c> is
    /// cancelled (shutdown), the loop exits cleanly.
    /// </para>
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var identity = _podIdentityOptions.PodIdentity ?? Environment.MachineName;

        var leaseLock = new LeaseLock(
            _kubeClient,
            _leaseOptions.Namespace,
            _leaseOptions.Name,
            identity);

        var config = new LeaderElectionConfig(leaseLock)
        {
            LeaseDuration = TimeSpan.FromSeconds(_leaseOptions.DurationSeconds),
            RetryPeriod = TimeSpan.FromSeconds(_leaseOptions.RenewIntervalSeconds),
            RenewDeadline = TimeSpan.FromSeconds(_leaseOptions.DurationSeconds - 2)
        };

        var elector = new LeaderElector(config);

        elector.OnStartedLeading += () =>
        {
            _isLeader = true;
            _logger.LogInformation("Acquired leadership for lease {LeaseName}", _leaseOptions.Name);
        };

        elector.OnStoppedLeading += () =>
        {
            _isLeader = false;
            _logger.LogInformation("Lost leadership for lease {LeaseName}", _leaseOptions.Name);
        };

        elector.OnNewLeader += leader =>
        {
            _logger.LogInformation("New leader observed: {Leader}", leader);
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            // Gate 1 (ELEC-01): non-preferred pod delays when the preferred pod is alive.
            // Preferred pod (ELEC-04) skips this block entirely.
            // Feature-off (NullPreferredStampReader): IsPreferredStampFresh is always false
            // so this block never triggers — zero overhead (ELEC-03).
            if (!_preferredLeaderService.IsPreferredPod && _stampReader.IsPreferredStampFresh)
            {
                _logger.LogDebug(
                    "Preferred pod is alive — delaying election attempt for {DurationSeconds}s",
                    _leaseOptions.DurationSeconds);
                await Task.Delay(TimeSpan.FromSeconds(_leaseOptions.DurationSeconds), stoppingToken);
                continue; // re-evaluate at top of loop
            }

            using var innerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _innerCts = innerCts;

            try
            {
                await elector.RunAndTryToHoldLeadershipForeverAsync(innerCts.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // Outer shutdown — exit loop cleanly
            }
            catch (OperationCanceledException)
            {
                // Inner cancel (Phase 88 voluntary yield) — loop continues, re-evaluate backoff
            }
            finally
            {
                _innerCts = null;
            }
        }
    }

    /// <summary>
    /// Gracefully releases the lease on shutdown. If this instance is the leader,
    /// the lease is explicitly deleted via the Kubernetes API for near-instant failover
    /// (HA-08, SC#3). Followers waiting on <see cref="LeaseOptions.DurationSeconds"/> TTL can
    /// immediately acquire leadership instead of waiting for expiry.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Cancel the ExecuteAsync stoppingToken first.
        // _innerCts is linked to stoppingToken and will be cancelled automatically.
        // The using block in ExecuteAsync handles disposal.
        await base.StopAsync(cancellationToken);

        if (_isLeader)
        {
            try
            {
                await _kubeClient.CoordinationV1.DeleteNamespacedLeaseAsync(
                    _leaseOptions.Name,
                    _leaseOptions.Namespace,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Lease {LeaseName} explicitly released for near-instant failover",
                    _leaseOptions.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to explicitly release lease {LeaseName} -- followers will acquire after TTL expiry",
                    _leaseOptions.Name);
            }
        }

        _isLeader = false;
    }
}
