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
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly IOidMapService _oidMapService;
    private readonly ILogger<TenantVectorRegistry> _logger;

    // Volatile fields: readers always observe a fully-constructed object reference.
    // Volatile.Write/Read via the 'volatile' keyword provides acquire/release semantics.
    private volatile IReadOnlyList<PriorityGroup> _groups = Array.Empty<PriorityGroup>();
    private volatile FrozenDictionary<RoutingKey, IReadOnlyList<MetricSlotHolder>> _routingIndex
        = FrozenDictionary<RoutingKey, IReadOnlyList<MetricSlotHolder>>.Empty;

    public TenantVectorRegistry(
        IDeviceRegistry deviceRegistry,
        IOidMapService oidMapService,
        ILogger<TenantVectorRegistry> logger)
    {
        _deviceRegistry = deviceRegistry;
        _oidMapService = oidMapService;
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

        // Heartbeat tenant: hardcoded at int.MinValue priority (reserved, always present).
        var heartbeatHolder = new MetricSlotHolder("127.0.0.1", 0, "Heartbeat", HeartbeatJobOptions.DefaultIntervalSeconds);
        var heartbeatKey = new RoutingKey("127.0.0.1", 0, "Heartbeat");
        if (oldSlotLookup.TryGetValue(heartbeatKey, out var oldHeartbeatHolder))
        {
            if (oldHeartbeatHolder.ReadSlot() is not null)
            {
                heartbeatHolder.CopyFrom(oldHeartbeatHolder);
                carriedOver++;
            }
        }

        var heartbeatTenant = new Tenant("heartbeat", int.MinValue, new[] { heartbeatHolder });
        priorityBuckets[int.MinValue] = new List<Tenant> { heartbeatTenant };
        totalSlots++;

        for (var i = 0; i < options.Tenants.Count; i++)
        {
            var tenantOpts = options.Tenants[i];
            var tenantId = !string.IsNullOrWhiteSpace(tenantOpts.Name)
                ? tenantOpts.Name
                : $"tenant-{i}";

            var holders = new List<MetricSlotHolder>(tenantOpts.Metrics.Count);
            int evaluateCount = 0;
            int resolvedCount = 0;

            // Metric entry validation loop (per-entry skip semantics: invalid entry does not affect siblings).
            for (var j = 0; j < tenantOpts.Metrics.Count; j++)
            {
                var metric = tenantOpts.Metrics[j];

                // 1. Structural: empty Ip
                if (string.IsNullOrWhiteSpace(metric.Ip))
                {
                    _logger.LogError(
                        "Tenant '{TenantId}' Metrics[{Index}] skipped: Ip is empty",
                        tenantId, j);
                    continue;
                }

                // 2. Structural: port out of range
                if (metric.Port < 1 || metric.Port > 65535)
                {
                    _logger.LogError(
                        "Tenant '{TenantId}' Metrics[{Index}] skipped: Port {Port} is out of range [1, 65535]",
                        tenantId, j, metric.Port);
                    continue;
                }

                // 3. Structural: empty MetricName
                if (string.IsNullOrWhiteSpace(metric.MetricName))
                {
                    _logger.LogError(
                        "Tenant '{TenantId}' Metrics[{Index}] skipped: MetricName is empty",
                        tenantId, j);
                    continue;
                }

                // 4. Role validation
                if (metric.Role != "Evaluate" && metric.Role != "Resolved")
                {
                    _logger.LogError(
                        "Tenant '{TenantId}' Metrics[{Index}] skipped: Role '{Role}' is invalid (must be 'Evaluate' or 'Resolved')",
                        tenantId, j, metric.Role);
                    continue;
                }

                // 5. MetricName resolution (TEN-05): must exist in OID map
                if (!_oidMapService.ContainsMetricName(metric.MetricName))
                {
                    _logger.LogError(
                        "Tenant '{TenantId}' Metrics[{Index}] skipped: MetricName '{MetricName}' not found in OID map (TEN-05)",
                        tenantId, j, metric.MetricName);
                    continue;
                }

                // 6. IP+Port existence (TEN-07): device must be registered
                if (!_deviceRegistry.TryGetByIpPort(metric.Ip, metric.Port, out _))
                {
                    _logger.LogError(
                        "Tenant '{TenantId}' Metrics[{Index}] skipped: IP+Port '{Ip}:{Port}' not found in DeviceRegistry (TEN-07)",
                        tenantId, j, metric.Ip, metric.Port);
                    continue;
                }

                // Passed all validation — build holder.
                var resolvedIp = ResolveIp(metric.Ip);
                var newHolder = new MetricSlotHolder(
                    resolvedIp,
                    metric.Port,
                    metric.MetricName,
                    metric.IntervalSeconds,
                    metric.TimeSeriesSize);

                // Carry over existing slot value when the same (ip, port, metricName) exists.
                var lookupKey = new RoutingKey(resolvedIp, metric.Port, metric.MetricName);
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

                // Track role counts for TEN-13 gate.
                if (metric.Role == "Evaluate") evaluateCount++;
                else resolvedCount++;
            }

            // Command entry validation loop.
            int commandCount = 0;
            for (var k = 0; k < tenantOpts.Commands.Count; k++)
            {
                var cmd = tenantOpts.Commands[k];

                // 1. Structural: empty Ip
                if (string.IsNullOrWhiteSpace(cmd.Ip))
                {
                    _logger.LogError(
                        "Tenant '{TenantId}' Commands[{Index}] skipped: Ip is empty",
                        tenantId, k);
                    continue;
                }

                // 2. Structural: port out of range
                if (cmd.Port < 1 || cmd.Port > 65535)
                {
                    _logger.LogError(
                        "Tenant '{TenantId}' Commands[{Index}] skipped: Port {Port} is out of range [1, 65535]",
                        tenantId, k, cmd.Port);
                    continue;
                }

                // 3. Structural: empty CommandName
                if (string.IsNullOrWhiteSpace(cmd.CommandName))
                {
                    _logger.LogError(
                        "Tenant '{TenantId}' Commands[{Index}] skipped: CommandName is empty",
                        tenantId, k);
                    continue;
                }

                // 4. ValueType validation (TEN-03)
                if (cmd.ValueType != "Integer32" && cmd.ValueType != "IpAddress" && cmd.ValueType != "OctetString")
                {
                    _logger.LogError(
                        "Tenant '{TenantId}' Commands[{Index}] skipped: ValueType '{ValueType}' is invalid (must be 'Integer32', 'IpAddress', or 'OctetString') (TEN-03)",
                        tenantId, k, cmd.ValueType);
                    continue;
                }

                // 5. Empty Value
                if (string.IsNullOrWhiteSpace(cmd.Value))
                {
                    _logger.LogError(
                        "Tenant '{TenantId}' Commands[{Index}] skipped: Value is empty",
                        tenantId, k);
                    continue;
                }

                // 6. IP+Port existence (TEN-07)
                if (!_deviceRegistry.TryGetByIpPort(cmd.Ip, cmd.Port, out _))
                {
                    _logger.LogError(
                        "Tenant '{TenantId}' Commands[{Index}] skipped: IP+Port '{Ip}:{Port}' not found in DeviceRegistry (TEN-07)",
                        tenantId, k, cmd.Ip, cmd.Port);
                    continue;
                }

                commandCount++;
            }

            // TEN-13 post-validation completeness gate.
            var missing = new List<string>(3);
            if (resolvedCount == 0) missing.Add("no Resolved metrics remaining after validation");
            if (evaluateCount == 0) missing.Add("no Evaluate metrics remaining after validation");
            if (commandCount == 0) missing.Add("no commands remaining after validation");

            if (missing.Count > 0)
            {
                _logger.LogError(
                    "Tenant '{TenantId}' skipped: {Reason}",
                    tenantId, string.Join("; ", missing));
                continue; // Skip to next tenant — do not add to priorityBuckets.
            }

            var tenant = new Tenant(tenantId, tenantOpts.Priority, holders);

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
        // TenantCount = surviving tenants + 1 for heartbeat.
        TenantCount = survivingTenantCount + 1;
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

    private string ResolveIp(string configIp)
    {
        foreach (var device in _deviceRegistry.AllDevices)
        {
            if (string.Equals(device.ConfigAddress, configIp, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Resolved tenant metric IP {ConfigIp} -> {ResolvedIp}", configIp, device.ResolvedIp);
                return device.ResolvedIp;
            }
        }
        return configIp;
    }

}
