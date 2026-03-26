using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Telemetry;

/// <summary>
/// Resolves this pod's preferred-leader identity once at startup by comparing
/// <c>PHYSICAL_HOSTNAME</c> against <see cref="LeaseOptions.PreferredNode"/>.
/// Maintains <see cref="IsPreferredStampFresh"/> as a volatile bool updated by
/// <see cref="UpdateStampFreshness"/> (called by PreferredHeartbeatJob on each poll cycle).
/// </summary>
public sealed class PreferredLeaderService : IPreferredStampReader
{
    private readonly bool _isPreferredPod;
    private readonly ILogger<PreferredLeaderService> _logger;
    private volatile bool _isPreferredStampFresh;

    public PreferredLeaderService(
        IOptions<LeaseOptions> leaseOptions,
        ILogger<PreferredLeaderService> logger)
    {
        _logger = logger;

        var preferredNode = leaseOptions.Value.PreferredNode;

        if (string.IsNullOrEmpty(preferredNode))
        {
            _isPreferredPod = false;
            // Feature disabled -- no log needed, expected default
            return;
        }

        var physicalHostname = Environment.GetEnvironmentVariable("PHYSICAL_HOSTNAME");

        if (string.IsNullOrEmpty(physicalHostname))
        {
            _isPreferredPod = false;
            return;
        }

        // Exact case-sensitive match
        _isPreferredPod = physicalHostname == preferredNode;
    }

    /// <inheritdoc />
    public bool IsPreferredStampFresh => _isPreferredStampFresh;

    /// <summary>
    /// Whether this pod's node name matches the configured PreferredNode.
    /// Resolved once at startup. Consumed by PreferredHeartbeatJob to gate the writer path.
    /// </summary>
    public bool IsPreferredPod => _isPreferredPod;

    /// <summary>
    /// Updates the in-memory freshness flag and logs any state transition at Info level.
    /// Called by PreferredHeartbeatJob on each poll cycle.
    /// Thread-safe: <see cref="_isPreferredStampFresh"/> is volatile.
    /// </summary>
    public void UpdateStampFreshness(bool isFresh)
    {
        var previous = _isPreferredStampFresh;
        _isPreferredStampFresh = isFresh;

        if (previous != isFresh)
        {
            _logger.LogInformation(
                "PreferredStamp freshness changed: {Previous} -> {Current}",
                previous, isFresh);
        }
    }
}
