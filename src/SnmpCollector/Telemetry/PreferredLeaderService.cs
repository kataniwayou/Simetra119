using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Telemetry;

/// <summary>
/// Resolves this pod's preferred-leader identity once at startup by comparing
/// <c>PHYSICAL_HOSTNAME</c> against <see cref="LeaseOptions.PreferredNode"/>.
/// Implements <see cref="IPreferredStampReader"/> as a stub (always false) pending
/// Phase 85 where the real heartbeat lease logic is added.
/// </summary>
public sealed class PreferredLeaderService : IPreferredStampReader
{
    private readonly bool _isPreferredPod;

    public PreferredLeaderService(
        IOptions<LeaseOptions> leaseOptions,
        ILogger<PreferredLeaderService> logger)
    {
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
            logger.LogWarning(
                "PreferredNode is configured ({PreferredNode}) but PHYSICAL_HOSTNAME env var is empty. " +
                "Preferred-leader feature disabled for this pod.",
                preferredNode);
            _isPreferredPod = false;
            return;
        }

        // Exact case-sensitive match
        _isPreferredPod = physicalHostname == preferredNode;

        logger.LogInformation(
            "Preferred-leader identity: PHYSICAL_HOSTNAME={HostName}, PreferredNode={PreferredNode}, IsPreferredPod={IsPreferredPod}",
            physicalHostname, preferredNode, _isPreferredPod);
    }

    // Phase 84: always false -- no heartbeat lease written yet (Phase 85)
    /// <inheritdoc />
    public bool IsPreferredStampFresh => false;

    /// <summary>
    /// Whether this pod's node name matches the configured PreferredNode.
    /// Resolved once at startup. Consumed by downstream heartbeat service (Phase 85).
    /// </summary>
    public bool IsPreferredPod => _isPreferredPod;
}
