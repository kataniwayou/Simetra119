using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Jobs;

/// <summary>
/// Evaluates tenant priority groups on a fixed interval, running a 4-tier evaluation
/// loop (staleness, resolved-gate, evaluate-threshold, command dispatch) per tenant.
/// Stamps liveness vector on completion for <see cref="HealthChecks.LivenessHealthCheck"/>
/// staleness detection.
/// </summary>
[DisallowConcurrentExecution]
public sealed class SnapshotJob : IJob
{
    private readonly ITenantVectorRegistry _registry;
    private readonly ISuppressionCache _suppressionCache;
    private readonly ICommandChannel _commandChannel;
    private readonly ICorrelationService _correlation;
    private readonly ILivenessVectorService _liveness;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly SnapshotJobOptions _options;
    private readonly ILogger<SnapshotJob> _logger;

    private readonly ITenantMetricService _tenantMetrics;

    public SnapshotJob(
        ITenantVectorRegistry registry,
        ISuppressionCache suppressionCache,
        ICommandChannel commandChannel,
        ICorrelationService correlation,
        ILivenessVectorService liveness,
        PipelineMetricService pipelineMetrics,
        ITenantMetricService tenantMetrics,
        IOptions<SnapshotJobOptions> options,
        ILogger<SnapshotJob> logger)
    {
        _registry = registry;
        _suppressionCache = suppressionCache;
        _commandChannel = commandChannel;
        _correlation = correlation;
        _liveness = liveness;
        _pipelineMetrics = pipelineMetrics;
        _tenantMetrics = tenantMetrics;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _correlation.OperationCorrelationId = _correlation.CurrentCorrelationId;
        var jobKey = context.JobDetail.Key.Name;
        var sw = Stopwatch.StartNew();

        try
        {
            var totalEvaluated = 0;
            var totalUnresolved = 0;

            foreach (var group in _registry.Groups)
            {
                var results = new TenantState[group.Tenants.Count];
                if (group.Tenants.Count == 1)
                {
                    // Single tenant: evaluate directly on Quartz thread — no ThreadPool hop
                    results[0] = EvaluateTenant(group.Tenants[0]);
                }
                else
                {
                    // Multiple tenants: parallel evaluation via ThreadPool
                    await Task.WhenAll(group.Tenants.Select((tenant, index) =>
                        Task.Run(() => results[index] = EvaluateTenant(tenant))));
                }

                // Aggregate counters for cycle summary
                for (var i = 0; i < results.Length; i++)
                {
                    totalEvaluated++;
                    if (results[i] == TenantState.Unresolved || results[i] == TenantState.NotReady) totalUnresolved++;
                }

                // Advance gate: block if ANY tenant is Unresolved or NotReady
                var shouldAdvance = true;
                for (var i = 0; i < results.Length; i++)
                {
                    if (results[i] == TenantState.Unresolved || results[i] == TenantState.NotReady)
                    {
                        shouldAdvance = false;
                        break;
                    }
                }

                if (!shouldAdvance)
                    break;
            }

            sw.Stop();
            _pipelineMetrics.RecordSnapshotCycleDuration(sw.Elapsed.TotalMilliseconds);

            _logger.LogDebug(
                "Snapshot cycle complete: {TenantsEvaluated} evaluated, {Unresolved} unresolved, {DurationMs:F1}ms",
                totalEvaluated, totalUnresolved, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot job {JobKey} failed", jobKey);
        }
        finally
        {
            _liveness.Stamp(jobKey);
            _correlation.OperationCorrelationId = null;
        }
    }

    /// <summary>
    /// Runs the gather-then-decide evaluation for a single tenant. Returns the tier result
    /// for priority-group advance gate decisions.
    /// Pre-tier: readiness check (only early return) — skip tenants still in grace window.
    /// GATHER: collects stale count, resolved/evaluate violation counts, dispatches commands.
    /// DECIDE: determines state using same priority order as v2.4 (stale &gt; resolved &gt; evaluate &gt; healthy).
    /// SINGLE EXIT: records all 6 percentage gauges + state + duration.
    /// </summary>
    internal TenantState EvaluateTenant(Tenant tenant)
    {
        var sw = Stopwatch.StartNew();

        // Pre-tier: Readiness check — only early return in this method
        if (!AreAllReady(tenant.Holders))
        {
            _logger.LogDebug(
                "Tenant {TenantId} priority={Priority} not ready (in grace window) — skipping",
                tenant.Id, tenant.Priority);
            return RecordAndReturn(tenant, TenantState.NotReady, sw);
        }

        // --- GATHER ---

        // Tier 1: Count stale holders
        var staleCount = CountStaleHolders(tenant.Holders);
        var staleTotal = CountStalenessEligibleHolders(tenant.Holders);
        var isStale = staleCount > 0;

        // Tier 2 & 3: Count resolved/evaluate violations (skipped on stale — unreliable data)
        int resolvedViolatedCount, resolvedTotal, evaluateViolatedCount, evaluateTotal;

        if (isStale)
        {
            _logger.LogDebug(
                "Tenant {TenantId} priority={Priority} tier=1 stale — dispatching commands",
                tenant.Id, tenant.Priority);
            resolvedViolatedCount = 0;
            resolvedTotal = 1; // avoid div/0; percent will be 0.0
            evaluateViolatedCount = 0;
            evaluateTotal = 1;
        }
        else
        {
            resolvedViolatedCount = CountResolvedViolated(tenant.Holders);
            resolvedTotal = CountResolvedParticipating(tenant.Holders);
            evaluateViolatedCount = CountEvaluateViolated(tenant.Holders);
            evaluateTotal = CountEvaluateParticipating(tenant.Holders);
        }

        // --- DECIDE (same priority order as v2.4) ---
        TenantState state;
        if (isStale)
        {
            state = TenantState.Unresolved;
        }
        else if (AreAllResolvedViolated(tenant.Holders))
        {
            _logger.LogDebug(
                "Tenant {TenantId} priority={Priority} tier=2 — all resolved violated — no commands",
                tenant.Id, tenant.Priority);
            state = TenantState.Resolved;
        }
        else if (AreAllEvaluateViolated(tenant.Holders))
        {
            state = TenantState.Unresolved;
        }
        else
        {
            _logger.LogDebug(
                "Tenant {TenantId} priority={Priority} tier=3 — healthy — no action",
                tenant.Id, tenant.Priority);
            state = TenantState.Healthy;
        }

        // --- DISPATCH (only when state = Unresolved) ---
        var enqueueCount = 0;
        var dispatchedCount = 0;
        var suppressedCount = 0;
        var failedCount = 0;

        if (state == TenantState.Unresolved)
        {
            foreach (var cmd in tenant.Commands)
            {
                var suppressionKey = $"{tenant.Id}:{cmd.Ip}:{cmd.Port}:{cmd.CommandName}";

                if (_suppressionCache.TrySuppress(suppressionKey, tenant.SuppressionWindowSeconds))
                {
                    _logger.LogDebug(
                        "Command {CommandName} suppressed for tenant {TenantId}",
                        cmd.CommandName, tenant.Id);
                    _pipelineMetrics.IncrementCommandSuppressed(tenant.Id);
                    suppressedCount++;
                    continue;
                }

                var request = new CommandRequest(
                    cmd.Ip, cmd.Port, cmd.CommandName, cmd.Value, cmd.ValueType,
                    tenant.Id, tenant.Priority);

                if (_commandChannel.Writer.TryWrite(request))
                {
                    enqueueCount++;
                    _pipelineMetrics.IncrementCommandDispatched(tenant.Id);
                    dispatchedCount++;
                }
                else
                {
                    _logger.LogWarning(
                        "Command channel full, dropping command {CommandName} for tenant {TenantId}",
                        cmd.CommandName, tenant.Id);
                    _pipelineMetrics.IncrementCommandFailed(tenant.Id);
                    failedCount++;
                }
            }

            _logger.LogInformation(
                "Tenant {TenantId} priority={Priority} tier=4 — commands enqueued, count={CommandCount}",
                tenant.Id, tenant.Priority, enqueueCount);
        }

        // --- COMPUTE PERCENTAGES ---
        var stalePercent = staleTotal == 0 ? 0.0 : staleCount * 100.0 / staleTotal;
        var cmdTotal     = tenant.Commands.Count;
        var dispatchedPct  = cmdTotal == 0 ? 0.0 : dispatchedCount  * 100.0 / cmdTotal;
        var failedPct      = cmdTotal == 0 ? 0.0 : failedCount      * 100.0 / cmdTotal;
        var suppressedPct  = cmdTotal == 0 ? 0.0 : suppressedCount  * 100.0 / cmdTotal;

        // --- SINGLE EXIT ---
        // stale% + command percentages: always recorded (command outcomes are real)
        _tenantMetrics.RecordMetricStalePercent(tenant.Id, tenant.Priority, stalePercent);
        _tenantMetrics.RecordCommandDispatchedPercent(tenant.Id, tenant.Priority, dispatchedPct);
        _tenantMetrics.RecordCommandFailedPercent(tenant.Id, tenant.Priority, failedPct);
        _tenantMetrics.RecordCommandSuppressedPercent(tenant.Id, tenant.Priority, suppressedPct);

        // resolved% + evaluate%: only when not stale (stale data unreliable)
        if (!isStale)
        {
            var resolvedPercent = resolvedTotal == 0 ? 0.0 : resolvedViolatedCount * 100.0 / resolvedTotal;
            var evaluatePercent = evaluateTotal == 0 ? 0.0 : evaluateViolatedCount * 100.0 / evaluateTotal;

            _tenantMetrics.RecordMetricResolvedPercent(tenant.Id, tenant.Priority, resolvedPercent);
            _tenantMetrics.RecordMetricEvaluatePercent(tenant.Id, tenant.Priority, evaluatePercent);
        }

        return RecordAndReturn(tenant, state, sw);
    }

    private TenantState RecordAndReturn(Tenant tenant, TenantState state, Stopwatch sw)
    {
        _tenantMetrics.RecordTenantState(tenant.Id, tenant.Priority, state);
        _tenantMetrics.RecordEvaluationDuration(tenant.Id, tenant.Priority, sw.Elapsed.TotalMilliseconds);
        return state;
    }

    /// <summary>
    /// Pre-tier: Checks whether all holders in the tenant are past their readiness grace window.
    /// A holder with real data (from WriteValue or CopyFrom) is always considered ready,
    /// even if its ConstructedAt is recent (handles config reload).
    /// </summary>
    private static bool AreAllReady(IReadOnlyList<MetricSlotHolder> holders)
    {
        foreach (var holder in holders)
        {
            if (!holder.IsReady)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Tier 1: Checks whether any non-excluded holder has stale data.
    /// Excluded: Source=Trap, Source=Command, IntervalSeconds=0.
    /// Holders with null ReadSlot after readiness check are stale (device never responded).
    /// </summary>
    private static bool HasStaleness(IReadOnlyList<MetricSlotHolder> holders)
    {
        foreach (var holder in holders)
        {
            // Exclude trap-sourced, command-response, and zero-interval holders
            if (holder.Source == SnmpSource.Trap || holder.Source == SnmpSource.Command || holder.IntervalSeconds == 0)
                continue;

            var slot = holder.ReadSlot();
            if (slot is null)
                return true; // Grace ended, no data — device never responded — stale

            var age = DateTimeOffset.UtcNow - slot.Timestamp;
            var graceWindow = TimeSpan.FromSeconds(holder.IntervalSeconds * holder.GraceMultiplier);

            if (age > graceWindow)
                return true; // This holder is stale
        }

        return false;
    }

    /// <summary>
    /// Tier 2: Checks whether ALL Resolved holders with data are violated.
    /// For Poll/Synthetic sources: every sample in the time series must be violated.
    /// For Trap/Command sources: only the newest sample is checked (no periodic interval).
    /// Returns true if all are violated (Violated — evaluation stops).
    /// Returns false if any Resolved holder with data is NOT violated (continue to Tier 3).
    /// Holders with empty series (Length == 0) do not participate.
    /// </summary>
    private static bool AreAllResolvedViolated(IReadOnlyList<MetricSlotHolder> holders)
    {
        var checkedCount = 0;

        foreach (var holder in holders)
        {
            if (holder.Role != "Resolved")
                continue;

            // Trap/Command sources: check newest sample only (one-shot, no periodic interval)
            if (holder.Source == SnmpSource.Trap || holder.Source == SnmpSource.Command)
            {
                var slot = holder.ReadSlot();
                if (slot is null)
                    continue;

                checkedCount++;

                if (!IsViolated(holder, slot))
                    return false;

                continue;
            }

            // Poll/Synthetic sources: check all time series samples
            var series = holder.ReadSeries();
            if (series.Length == 0)
                continue;

            foreach (var sample in series)
            {
                if (!IsViolated(holder, sample))
                    return false;
            }

            checkedCount++;
        }

        // If at least one Resolved holder was checked and ALL were violated → Violated
        // If none were checked (all empty or no Resolved) → vacuous true (defensive)
        return true;
    }

    /// <summary>
    /// Tier 3: Checks whether ALL Evaluate holders with data are violated.
    /// For Poll/Synthetic sources: every sample in the time series must be violated.
    /// For Trap/Command sources: only the newest sample is checked (no periodic interval).
    /// Returns true if all are violated (proceed to Tier 4 command dispatch).
    /// Returns false if any Evaluate holder with data is NOT violated (Healthy).
    /// Holders with empty series (Length == 0) do not participate.
    /// If no Evaluate holders have data, returns false (vacuous fail — no command).
    /// </summary>
    private static bool AreAllEvaluateViolated(IReadOnlyList<MetricSlotHolder> holders)
    {
        var checkedCount = 0;

        foreach (var holder in holders)
        {
            if (holder.Role != "Evaluate")
                continue;

            // Trap/Command sources: check newest sample only (one-shot, no periodic interval)
            if (holder.Source == SnmpSource.Trap || holder.Source == SnmpSource.Command)
            {
                var slot = holder.ReadSlot();
                if (slot is null)
                    continue;

                checkedCount++;

                if (!IsViolated(holder, slot))
                    return false;

                continue;
            }

            // Poll/Synthetic sources: check all time series samples
            var series = holder.ReadSeries();
            if (series.Length == 0)
                continue;

            foreach (var sample in series)
            {
                if (!IsViolated(holder, sample))
                    return false;
            }

            checkedCount++;
        }

        // If at least one Evaluate holder was checked and ALL samples were violated → proceed to Tier 4
        // If none were checked (all empty or no Evaluate) → vacuous false (no data = no command)
        return checkedCount > 0;
    }

    /// <summary>
    /// Counts non-excluded holders that have stale data (null slot or age exceeds grace window).
    /// Excluded: Source=Trap, Source=Command, IntervalSeconds=0.
    /// Unlike HasStaleness, does not short-circuit — counts ALL stale holders.
    /// </summary>
    private static int CountStaleHolders(IReadOnlyList<MetricSlotHolder> holders)
    {
        var count = 0;
        foreach (var holder in holders)
        {
            if (holder.Source == SnmpSource.Trap || holder.Source == SnmpSource.Command || holder.IntervalSeconds == 0)
                continue;

            var slot = holder.ReadSlot();
            if (slot is null)
            {
                count++;
                continue;
            }

            var age = DateTimeOffset.UtcNow - slot.Timestamp;
            var graceWindow = TimeSpan.FromSeconds(holder.IntervalSeconds * holder.GraceMultiplier);
            if (age > graceWindow)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Counts Evaluate-role holders where ALL checked samples are violated.
    /// For Trap/Command sources: checks newest sample only. Null slot = skip.
    /// For Poll/Synthetic sources: ALL samples must be violated for the holder to count.
    /// Holders with empty series (Length == 0) do not participate.
    /// </summary>
    private static int CountEvaluateViolated(IReadOnlyList<MetricSlotHolder> holders)
    {
        var count = 0;
        foreach (var holder in holders)
        {
            if (holder.Role != "Evaluate")
                continue;

            if (holder.Source == SnmpSource.Trap || holder.Source == SnmpSource.Command)
            {
                var slot = holder.ReadSlot();
                if (slot is null)
                    continue;
                if (IsViolated(holder, slot))
                    count++;
                continue;
            }

            var series = holder.ReadSeries();
            if (series.Length == 0)
                continue;

            var allViolated = true;
            foreach (var sample in series)
            {
                if (!IsViolated(holder, sample))
                {
                    allViolated = false;
                    break;
                }
            }
            if (allViolated)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Counts holders eligible for the staleness check.
    /// Excluded: Source=Trap, Source=Command, IntervalSeconds=0.
    /// This is the denominator for stale%.
    /// </summary>
    private static int CountStalenessEligibleHolders(IReadOnlyList<MetricSlotHolder> holders)
    {
        var count = 0;
        foreach (var holder in holders)
        {
            if (holder.Source == SnmpSource.Trap || holder.Source == SnmpSource.Command || holder.IntervalSeconds == 0)
                continue;
            count++;
        }
        return count;
    }

    /// <summary>
    /// Counts Resolved-role holders that ARE violated (numerator for resolved% — higher = worse).
    /// For Trap/Command sources: checks newest sample only. Null slot = skip.
    /// For Poll/Synthetic sources: ALL samples must be violated for the holder to count.
    /// Holders with empty series (Length == 0) do not participate.
    /// </summary>
    private static int CountResolvedViolated(IReadOnlyList<MetricSlotHolder> holders)
    {
        var count = 0;
        foreach (var holder in holders)
        {
            if (holder.Role != "Resolved")
                continue;

            if (holder.Source == SnmpSource.Trap || holder.Source == SnmpSource.Command)
            {
                var slot = holder.ReadSlot();
                if (slot is null)
                    continue;
                if (IsViolated(holder, slot))
                    count++;
                continue;
            }

            var series = holder.ReadSeries();
            if (series.Length == 0)
                continue;

            var allViolated = true;
            foreach (var sample in series)
            {
                if (!IsViolated(holder, sample))
                {
                    allViolated = false;
                    break;
                }
            }
            if (allViolated)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Counts Resolved-role holders with at least one participating sample.
    /// For Trap/Command sources: non-null slot = participating.
    /// For Poll/Synthetic sources: series.Length &gt; 0 = participating.
    /// This is the denominator for resolved%.
    /// </summary>
    private static int CountResolvedParticipating(IReadOnlyList<MetricSlotHolder> holders)
    {
        var count = 0;
        foreach (var holder in holders)
        {
            if (holder.Role != "Resolved")
                continue;

            if (holder.Source == SnmpSource.Trap || holder.Source == SnmpSource.Command)
            {
                if (holder.ReadSlot() is not null)
                    count++;
                continue;
            }

            if (holder.ReadSeries().Length > 0)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Counts Evaluate-role holders with at least one participating sample.
    /// For Trap/Command sources: non-null slot = participating.
    /// For Poll/Synthetic sources: series.Length &gt; 0 = participating.
    /// This is the denominator for evaluate%.
    /// </summary>
    private static int CountEvaluateParticipating(IReadOnlyList<MetricSlotHolder> holders)
    {
        var count = 0;
        foreach (var holder in holders)
        {
            if (holder.Role != "Evaluate")
                continue;

            if (holder.Source == SnmpSource.Trap || holder.Source == SnmpSource.Command)
            {
                if (holder.ReadSlot() is not null)
                    count++;
                continue;
            }

            if (holder.ReadSeries().Length > 0)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Checks whether a holder's current value violates its threshold bounds.
    /// <para>
    /// Threshold conditions:
    /// - null threshold or both Min/Max null → always violated
    /// - Min == Max (both non-null, same value) → violated if value equals that value (equality check)
    /// - Min != Max → strict inequality: value &lt; Min or value &gt; Max is violated; boundary values are in-range
    /// - Only Min set → violated if value &lt; Min
    /// - Only Max set → violated if value &gt; Max
    /// </para>
    /// </summary>
    internal static bool IsViolated(MetricSlotHolder holder, MetricSlot slot)
    {
        var threshold = holder.Threshold;

        // No threshold at all → violated
        if (threshold is null)
            return true;

        // Both bounds null → no meaningful threshold → violated
        if (threshold.Min is null && threshold.Max is null)
            return true;

        var value = slot.Value;

        // Equal bounds (Min == Max, both non-null) → equality condition
        // Violated if value equals the threshold point (exact match triggers action)
        if (threshold.Min is not null && threshold.Max is not null
            && threshold.Min.Value == threshold.Max.Value)
        {
            return value == threshold.Min.Value;
        }

        // Strict inequality: value < Min is violated, value == Min is in-range
        if (threshold.Min is not null && value < threshold.Min.Value)
            return true;

        // Strict inequality: value > Max is violated, value == Max is in-range
        if (threshold.Max is not null && value > threshold.Max.Value)
            return true;

        return false; // Value is within threshold range
    }
}
