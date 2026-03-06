using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Hosted lifecycle service that runs during <see cref="StartingAsync"/> (before Quartz starts any jobs)
/// to compute and log the estimated OTel metric series cardinality.
/// <para>
/// Cardinality formula: devices x max(OID map entries, unique poll OIDs) x instruments (2) x sources (2).
/// </para>
/// <para>
/// A warning is logged if the estimate exceeds <see cref="WarningThreshold"/> (10,000 series),
/// but startup is never blocked -- this is warn-but-allow by design.
/// </para>
/// <para>
/// Label taxonomy (bounded by design):
///   site_name  -- 1 per deployment (from SiteOptions.Name)
///   metric_name -- bounded by OID map size + 1 for Unknown
///   oid         -- bounded by OID map size
///   agent       -- bounded by device count (IDeviceRegistry.AllDevices)
///   source      -- 2 values: poll, trap
///   snmp_type   -- 8 fixed values: integer32, gauge32, timeticks, counter32, counter64, octetstring, ipaddress, objectidentifier
/// </para>
/// </summary>
public sealed class CardinalityAuditService : IHostedLifecycleService
{
    private readonly IDeviceRegistry _registry;
    private readonly IOidMapService _oidMap;
    private readonly ILogger<CardinalityAuditService> _logger;

    // Constants for cardinality calculation
    private const int InstrumentCount = 2;     // snmp_gauge, snmp_info
    private const int SourceCount = 2;         // poll, trap
    private const int WarningThreshold = 10_000;

    public CardinalityAuditService(
        IDeviceRegistry registry,
        IOidMapService oidMap,
        ILogger<CardinalityAuditService> logger)
    {
        _registry = registry;
        _oidMap = oidMap;
        _logger = logger;
    }

    /// <summary>
    /// Runs before <see cref="StartAsync"/>, which ensures the audit completes
    /// before the Quartz hosted service begins scheduling jobs.
    /// </summary>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        AuditCardinality();
        return Task.CompletedTask;
    }

    // Required IHostedLifecycleService members -- no-op for this audit-only service.
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void AuditCardinality()
    {
        var devices = _registry.AllDevices;
        var deviceCount = devices.Count;
        var oidMapEntries = _oidMap.EntryCount;

        // Count unique OIDs across all device poll groups.
        // Traps may reference OIDs that polls don't cover, so OID map size is the upper bound
        // for the OID dimension -- hence we take the max of both sources.
        var uniquePollOids = devices
            .SelectMany(d => d.PollGroups.SelectMany(p => p.Oids))
            .Distinct(StringComparer.Ordinal)
            .Count();

        // OID dimension: max of configured map size vs unique OIDs seen in poll groups.
        var oidDimension = Math.Max(oidMapEntries, uniquePollOids);

        // Cardinality formula: devices x OIDs x instruments x sources
        var estimate = deviceCount * oidDimension * InstrumentCount * SourceCount;

        _logger.LogInformation(
            "Cardinality audit: {Devices} devices, {OidMapEntries} OID map entries, " +
            "{UniquePollOids} unique poll OIDs, {Instruments} instruments, {Sources} sources. " +
            "Estimated series: ~{Estimate}",
            deviceCount, oidMapEntries, uniquePollOids, InstrumentCount, SourceCount, estimate);

        // Log label taxonomy summary -- provides documentation-in-logs for operational teams.
        _logger.LogInformation(
            "Label taxonomy: site_name (1 per deployment), " +
            "metric_name (bounded by OID map: {OidMapSize} entries + Unknown), " +
            "oid (bounded by OID map), " +
            "agent (bounded by device count: {DeviceCount}), " +
            "source (2: poll/trap), " +
            "snmp_type (bounded: 8 fixed enum values)",
            oidMapEntries, deviceCount);

        if (estimate > WarningThreshold)
        {
            _logger.LogWarning(
                "Cardinality estimate {Estimate} exceeds warning threshold {Threshold}. " +
                "Consider reducing OID count or device count to avoid Prometheus performance degradation.",
                estimate, WarningThreshold);
        }
    }
}
