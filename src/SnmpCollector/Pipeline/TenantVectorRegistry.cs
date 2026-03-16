using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using SnmpCollector.Configuration;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Singleton registry that holds all tenant metric slot holders, organised into priority groups
/// and indexed for O(1) fan-out dispatch by (ip, port, metricName).
/// <para>
/// Thread safety: <see cref="Reload"/> builds a complete new data structure and swaps it in
/// via volatile writes. Readers always see a consistent snapshot; no locks needed.
/// </para>
/// </summary>
public sealed class TenantVectorRegistry : ITenantVectorRegistry
{
    private readonly ILogger<TenantVectorRegistry> _logger;

    // Volatile fields: readers always observe a fully-constructed object reference.
    // Volatile.Write/Read via the 'volatile' keyword provides acquire/release semantics.
    private volatile IReadOnlyList<PriorityGroup> _groups = Array.Empty<PriorityGroup>();
    private volatile FrozenDictionary<RoutingKey, IReadOnlyList<MetricSlotHolder>> _routingIndex
        = FrozenDictionary<RoutingKey, IReadOnlyList<MetricSlotHolder>>.Empty;

    public TenantVectorRegistry(ILogger<TenantVectorRegistry> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<PriorityGroup> Groups => _groups;

    /// <inheritdoc />
    public int TenantCount { get; private set; }

    /// <inheritdoc />
    public int SlotCount { get; private set; }

    /// <inheritdoc />
    public bool TryRoute(string ip, int port, string metricName,
        out IReadOnlyList<MetricSlotHolder> holders)
    {
        var result = _routingIndex.TryGetValue(new RoutingKey(ip, port, metricName), out var found);
        holders = found!;
        return result;
    }

    /// <summary>
    /// Rebuilds the registry from pre-validated options. All entries are assumed structurally
    /// valid with resolved IPs. Carries over existing slot values for metrics that appear in
    /// both the old and new configuration. Swaps the new groups and routing index atomically
    /// via volatile writes.
    /// </summary>
    /// <param name="options">Current tenant vector configuration (pre-validated by watcher).</param>
    public void Reload(TenantVectorOptions options)
    {
        // Step 1: Capture current groups snapshot for value carry-over.
        var oldGroups = _groups;

        // Step 2: Build lookup for carry-over: (ip, port, metricName) -> holder.
        var oldSlotLookup = new Dictionary<RoutingKey, MetricSlotHolder>(RoutingKeyComparer.Instance);
        foreach (var group in oldGroups)
            foreach (var tenant in group.Tenants)
                foreach (var holder in tenant.Holders)
                    oldSlotLookup[new RoutingKey(holder.Ip, holder.Port, holder.MetricName)] = holder;

        // Step 3: Build new MetricSlotHolders, carrying over old values where metric matches.
        int carriedOver = 0;
        int totalSlots = 0;
        int survivingTenantCount = 0;

        // SortedDictionary with ascending key order (lowest priority int = highest priority = first group).
        var priorityBuckets = new SortedDictionary<int, List<Tenant>>();

        for (var i = 0; i < options.Tenants.Count; i++)
        {
            var tenantOpts = options.Tenants[i];
            var tenantId = !string.IsNullOrWhiteSpace(tenantOpts.Name)
                ? tenantOpts.Name
                : $"tenant-{i}";

            var holders = new List<MetricSlotHolder>(tenantOpts.Metrics.Count);

            // All entries are pre-validated — build MetricSlotHolders directly.
            for (var j = 0; j < tenantOpts.Metrics.Count; j++)
            {
                var metric = tenantOpts.Metrics[j];
                var newHolder = new MetricSlotHolder(
                    metric.Ip,
                    metric.Port,
                    metric.MetricName,
                    metric.IntervalSeconds,
                    metric.Role,
                    metric.TimeSeriesSize,
                    metric.GraceMultiplier,
                    metric.Threshold);

                // Carry over existing slot value when the same (ip, port, metricName) exists.
                var lookupKey = new RoutingKey(metric.Ip, metric.Port, metric.MetricName);
                if (oldSlotLookup.TryGetValue(lookupKey, out var oldHolder))
                {
                    if (oldHolder.ReadSlot() is not null)
                    {
                        newHolder.CopyFrom(oldHolder);
                        carriedOver++;
                    }
                }

                holders.Add(newHolder);
                totalSlots++;
            }

            var tenant = new Tenant(tenantId, tenantOpts.Priority, holders, tenantOpts.Commands, tenantOpts.SuppressionWindowSeconds);

            if (!priorityBuckets.TryGetValue(tenantOpts.Priority, out var bucket))
            {
                bucket = new List<Tenant>();
                priorityBuckets[tenantOpts.Priority] = bucket;
            }

            bucket.Add(tenant);
            survivingTenantCount++;
        }

        // Step 4: Build IReadOnlyList<PriorityGroup> from sorted buckets.
        var newGroups = priorityBuckets
            .Select(kvp => new PriorityGroup(kvp.Key, kvp.Value))
            .ToList<PriorityGroup>();

        // Step 5: Build routing index: (ip, port, metricName) -> list of holders across all tenants.
        var routingBuilder = new Dictionary<RoutingKey, List<MetricSlotHolder>>(RoutingKeyComparer.Instance);

        foreach (var group in newGroups)
        {
            foreach (var tenant in group.Tenants)
            {
                foreach (var holder in tenant.Holders)
                {
                    var rk = new RoutingKey(holder.Ip, holder.Port, holder.MetricName);
                    if (!routingBuilder.TryGetValue(rk, out var list))
                    {
                        list = new List<MetricSlotHolder>();
                        routingBuilder[rk] = list;
                    }
                    list.Add(holder);
                }
            }
        }

        var newRoutingIndex = routingBuilder
            .ToFrozenDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<MetricSlotHolder>)kvp.Value,
                RoutingKeyComparer.Instance);

        // Step 6: Update counts before volatile swap.
        TenantCount = survivingTenantCount;
        SlotCount = totalSlots;

        // Step 7: Volatile swap — readers see either old or new, never a partial mix.
        _groups = newGroups;
        _routingIndex = newRoutingIndex;

        // Step 8: Count-based reload log.
        _logger.LogInformation(
            "TenantVectorRegistry reloaded: tenants={TenantCount}, slots={SlotCount}, carried_over={CarriedOver}",
            TenantCount,
            SlotCount,
            carriedOver);

        // Step 9: Debug log for time series holders with depth > 1.
        foreach (var group in newGroups)
            foreach (var tenant in group.Tenants)
                foreach (var holder in tenant.Holders)
                    if (holder.TimeSeriesSize > 1)
                        _logger.LogDebug(
                            "TimeSeries holder: tenant={TenantId} metric={MetricName} ip={Ip} size={TimeSeriesSize} samples={SampleCount}",
                            tenant.Id, holder.MetricName, holder.Ip, holder.TimeSeriesSize, holder.ReadSeries().Length);
    }
}
