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
/// </summary>
public sealed class K8sLeaseElection : BackgroundService, ILeaderElection
{
    private readonly LeaseOptions _leaseOptions;
    private readonly PodIdentityOptions _podIdentityOptions;
    private readonly IKubernetes _kubeClient;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<K8sLeaseElection> _logger;

    /// <summary>
    /// Thread-safe leadership flag. Written only by the LeaderElector event handlers
    /// (single writer), read by multiple consumers via <see cref="IsLeader"/>.
    /// </summary>
    private volatile bool _isLeader;

    /// <summary>
    /// Initializes a new instance of <see cref="K8sLeaseElection"/>.
    /// </summary>
    /// <param name="leaseOptions">Lease configuration (name, namespace, durations).</param>
    /// <param name="podIdentityOptions">Pod identity configuration.</param>
    /// <param name="kubeClient">Kubernetes API client for lease operations.</param>
    /// <param name="lifetime">Application lifetime for shutdown coordination.</param>
    /// <param name="logger">Logger for leadership state changes.</param>
    public K8sLeaseElection(
        IOptions<LeaseOptions> leaseOptions,
        IOptions<PodIdentityOptions> podIdentityOptions,
        IKubernetes kubeClient,
        IHostApplicationLifetime lifetime,
        ILogger<K8sLeaseElection> logger)
    {
        _leaseOptions = leaseOptions?.Value ?? throw new ArgumentNullException(nameof(leaseOptions));
        _podIdentityOptions = podIdentityOptions?.Value ?? throw new ArgumentNullException(nameof(podIdentityOptions));
        _kubeClient = kubeClient ?? throw new ArgumentNullException(nameof(kubeClient));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsLeader => _isLeader;

    /// <inheritdoc />
    public string CurrentRole => _isLeader ? "leader" : "follower";

    /// <summary>
    /// Runs the leader election loop using <see cref="LeaderElector.RunAndTryToHoldLeadershipForeverAsync"/>.
    /// This method retries indefinitely on leadership loss rather than exiting, ensuring
    /// the pod remains a candidate for re-election.
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

        await elector.RunAndTryToHoldLeadershipForeverAsync(stoppingToken);
    }

    /// <summary>
    /// Gracefully releases the lease on shutdown. If this instance is the leader,
    /// the lease is explicitly deleted via the Kubernetes API for near-instant failover
    /// (HA-08, SC#3). Followers waiting on <see cref="LeaseOptions.DurationSeconds"/> TTL can
    /// immediately acquire leadership instead of waiting for expiry.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Cancel the ExecuteAsync stoppingToken first
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
