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
    /// Rebuilds the registry from the provided options, carrying over existing slot values
    /// for metrics that appear in both the old and new configuration.
    /// Swaps the new groups and routing index atomically via volatile writes.
    /// </summary>
    /// <param name="options">Current tenant vector configuration.</param>
    public void Reload(TenantVectorOptions options)
    {
        // Step 1: Capture current groups snapshot for diff logging and value carry-over.
        var oldGroups = _groups;

        // Step 2: Build lookup for carry-over: (tenantId, ip, port, metricName) -> holder.
        var oldSlotLookup = new Dictionary<(string TenantId, string Ip, int Port, string MetricName),
            MetricSlotHolder>(StringTupleComparer.Instance);

        foreach (var group in oldGroups)
        {
            foreach (var tenant in group.Tenants)
            {
                foreach (var holder in tenant.Holders)
                {
                    var key = (tenant.Id, holder.Ip, holder.Port, holder.MetricName);
                    oldSlotLookup[key] = holder;
                }
            }
        }

        // Step 3: Compute diff statistics for logging.
        var oldTenantIds = new HashSet<string>(
            oldGroups.SelectMany(g => g.Tenants).Select(t => t.Id),
            StringComparer.OrdinalIgnoreCase);
        var newTenantIds = new HashSet<string>(
            options.Tenants.Select(t => t.Id),
            StringComparer.OrdinalIgnoreCase);

        var addedTenants = newTenantIds.Except(oldTenantIds, StringComparer.OrdinalIgnoreCase).ToList();
        var removedTenants = oldTenantIds.Except(newTenantIds, StringComparer.OrdinalIgnoreCase).ToList();
        var unchangedTenants = newTenantIds.Intersect(oldTenantIds, StringComparer.OrdinalIgnoreCase).ToList();

        // Step 4: Build new MetricSlotHolders, carrying over old values where metric matches.
        int carriedOver = 0;
        int totalSlots = 0;

        // SortedDictionary with ascending key order (lowest priority int = highest priority = first group).
        var priorityBuckets = new SortedDictionary<int, List<Tenant>>();

        foreach (var tenantOpts in options.Tenants)
        {
            var holders = new List<MetricSlotHolder>(tenantOpts.Metrics.Count);

            foreach (var metric in tenantOpts.Metrics)
            {
                var newHolder = new MetricSlotHolder(
                    metric.Ip,
                    metric.Port,
                    metric.MetricName,
                    metric.IntervalSeconds);

                // Carry over existing slot value when the same (tenantId, ip, port, metricName) exists.
                var lookupKey = (tenantOpts.Id, metric.Ip, metric.Port, metric.MetricName);
                if (oldSlotLookup.TryGetValue(lookupKey, out var oldHolder))
                {
                    var existingSlot = oldHolder.ReadSlot();
                    if (existingSlot is not null)
                    {
                        newHolder.WriteValue(existingSlot.Value, existingSlot.StringValue, existingSlot.TypeCode);
                        carriedOver++;
                    }
                }

                holders.Add(newHolder);
                totalSlots++;
            }

            var tenant = new Tenant(tenantOpts.Id, tenantOpts.Priority, holders);

            if (!priorityBuckets.TryGetValue(tenantOpts.Priority, out var bucket))
            {
                bucket = new List<Tenant>();
                priorityBuckets[tenantOpts.Priority] = bucket;
            }

            bucket.Add(tenant);
        }

        // Step 5: Build IReadOnlyList<PriorityGroup> from sorted buckets.
        var newGroups = priorityBuckets
            .Select(kvp => new PriorityGroup(kvp.Key, kvp.Value))
            .ToList<PriorityGroup>();

        // Step 6: Build routing index: (ip, port, metricName) -> list of holders across all tenants.
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

        // Step 7: Update counts before volatile swap.
        TenantCount = options.Tenants.Count;
        SlotCount = totalSlots;

        // Step 8: Volatile swap — readers see either old or new, never a partial mix.
        _groups = newGroups;
        _routingIndex = newRoutingIndex;

        // Step 9: Structured diff log.
        _logger.LogInformation(
            "TenantVectorRegistry reloaded: tenants={TenantCount}, slots={SlotCount}, " +
            "added=[{Added}], removed=[{Removed}], unchanged=[{Unchanged}], carried_over={CarriedOver}",
            TenantCount,
            SlotCount,
            string.Join(",", addedTenants),
            string.Join(",", removedTenants),
            string.Join(",", unchangedTenants),
            carriedOver);
    }

    /// <summary>
    /// Case-insensitive equality comparer for (tenantId, ip, port, metricName) tuples.
    /// Used for carry-over lookup during <see cref="TenantVectorRegistry.Reload"/>.
    /// </summary>
    private sealed class StringTupleComparer
        : IEqualityComparer<(string TenantId, string Ip, int Port, string MetricName)>
    {
        public static readonly StringTupleComparer Instance = new();

        private StringTupleComparer() { }

        public bool Equals(
            (string TenantId, string Ip, int Port, string MetricName) x,
            (string TenantId, string Ip, int Port, string MetricName) y)
            => x.Port == y.Port
               && string.Equals(x.TenantId, y.TenantId, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Ip, y.Ip, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.MetricName, y.MetricName, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string TenantId, string Ip, int Port, string MetricName) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TenantId),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Ip),
                obj.Port,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.MetricName));
    }
}
