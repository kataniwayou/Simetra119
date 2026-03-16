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

    /// <summary>
    /// Result of per-tenant 4-tier evaluation. Drives priority-group advance gate
    /// and cycle summary logging.
    /// </summary>
    internal enum TierResult { Stale, ConfirmedBad, Healthy, Commanded }

    public SnapshotJob(
        ITenantVectorRegistry registry,
        ISuppressionCache suppressionCache,
        ICommandChannel commandChannel,
        ICorrelationService correlation,
        ILivenessVectorService liveness,
        PipelineMetricService pipelineMetrics,
        IOptions<SnapshotJobOptions> options,
        ILogger<SnapshotJob> logger)
    {
        _registry = registry;
        _suppressionCache = suppressionCache;
        _commandChannel = commandChannel;
        _correlation = correlation;
        _liveness = liveness;
        _pipelineMetrics = pipelineMetrics;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _correlation.OperationCorrelationId = _correlation.CurrentCorrelationId;
        var jobKey = context.JobDetail.Key.Name;

        try
        {
            var totalEvaluated = 0;
            var totalCommanded = 0;
            var totalStale = 0;

            foreach (var group in _registry.Groups)
            {
                var results = new TierResult[group.Tenants.Count];
                await Task.WhenAll(group.Tenants.Select((tenant, index) =>
                    Task.Run(() => results[index] = EvaluateTenant(tenant))));

                // Aggregate counters for cycle summary
                for (var i = 0; i < results.Length; i++)
                {
                    totalEvaluated++;
                    if (results[i] == TierResult.Commanded) totalCommanded++;
                    if (results[i] == TierResult.Stale) totalStale++;
                }

                // Advance gate: block if ANY tenant is Stale or Commanded
                var shouldAdvance = true;
                for (var i = 0; i < results.Length; i++)
                {
                    if (results[i] == TierResult.Stale || results[i] == TierResult.Commanded)
                    {
                        shouldAdvance = false;
                        break;
                    }
                }

                if (!shouldAdvance)
                    break;
            }

            _logger.LogDebug(
                "Snapshot cycle complete: {TenantsEvaluated} evaluated, {Commanded} commanded, {Stale} stale",
                totalEvaluated, totalCommanded, totalStale);
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
    /// Runs the 4-tier evaluation for a single tenant. Returns the tier result
    /// for priority-group advance gate decisions.
    /// Tier 1: staleness check. Tier 2: resolved gate. Tier 3: evaluate threshold.
    /// Tier 4: command dispatch with suppression.
    /// </summary>
    internal TierResult EvaluateTenant(Tenant tenant)
    {
        // Tier 1: Staleness check
        if (HasStaleness(tenant.Holders))
        {
            _logger.LogDebug(
                "Tenant {TenantId} priority={Priority} tier=1 stale — skipping threshold checks",
                tenant.Id, tenant.Priority);
            return TierResult.Stale;
        }

        // Tier 2: Resolved gate — are ALL Resolved metrics violated?
        if (AreAllResolvedViolated(tenant.Holders))
        {
            _logger.LogDebug(
                "Tenant {TenantId} priority={Priority} tier=2 — all resolved violated, device confirmed bad, no commands",
                tenant.Id, tenant.Priority);
            return TierResult.ConfirmedBad;
        }

        // Not all Resolved violated — proceed to Tier 3 (evaluate check)
        _logger.LogDebug(
            "Tenant {TenantId} priority={Priority} tier=2 — resolved not all violated, proceeding to evaluate check",
            tenant.Id, tenant.Priority);

        // Tier 3: Evaluate gate — are ALL Evaluate metrics violated?
        if (!AreAllEvaluateViolated(tenant.Holders))
        {
            _logger.LogDebug(
                "Tenant {TenantId} priority={Priority} tier=3 — not all evaluate metrics violated, no action",
                tenant.Id, tenant.Priority);
            return TierResult.Healthy;
        }

        // Tier 4: Command dispatch with suppression
        var enqueueCount = 0;

        foreach (var cmd in tenant.Commands)
        {
            var suppressionKey = $"{tenant.Id}:{cmd.Ip}:{cmd.Port}:{cmd.CommandName}";

            if (_suppressionCache.TrySuppress(suppressionKey, tenant.SuppressionWindowSeconds))
            {
                _logger.LogDebug(
                    "Command {CommandName} suppressed for tenant {TenantId}",
                    cmd.CommandName, tenant.Id);
                _pipelineMetrics.IncrementCommandSuppressed(tenant.Id);
                continue;
            }

            var request = new CommandRequest(
                cmd.Ip, cmd.Port, cmd.CommandName, cmd.Value, cmd.ValueType);

            if (_commandChannel.Writer.TryWrite(request))
            {
                enqueueCount++;
            }
            else
            {
                _logger.LogWarning(
                    "Command channel full, dropping command {CommandName} for tenant {TenantId}",
                    cmd.CommandName, tenant.Id);
                _pipelineMetrics.IncrementCommandFailed(tenant.Id);
            }
        }

        _logger.LogInformation(
            "Tenant {TenantId} priority={Priority} tier=4 — commands enqueued, count={CommandCount}",
            tenant.Id, tenant.Priority, enqueueCount);

        // If no commands were actually enqueued (all suppressed or channel full),
        // treat as ConfirmedBad — no active commands firing, safe to cascade
        return enqueueCount > 0 ? TierResult.Commanded : TierResult.ConfirmedBad;
    }

    /// <summary>
    /// Tier 1: Checks whether any non-excluded holder has stale data.
    /// Excluded: Source=Trap, IntervalSeconds=0.
    /// Holders with null ReadSlot are skipped (cannot judge, not stale).
    /// </summary>
    private static bool HasStaleness(IReadOnlyList<MetricSlotHolder> holders)
    {
        foreach (var holder in holders)
        {
            // Exclude trap-sourced and zero-interval holders
            if (holder.Source == SnmpSource.Trap || holder.IntervalSeconds == 0)
                continue;

            var slot = holder.ReadSlot();
            if (slot is null)
                continue; // No data yet — cannot judge, not stale

            var age = DateTimeOffset.UtcNow - slot.Timestamp;
            var graceWindow = TimeSpan.FromSeconds(holder.IntervalSeconds * holder.GraceMultiplier);

            if (age > graceWindow)
                return true; // This holder is stale
        }

        return false;
    }

    /// <summary>
    /// Tier 2: Checks whether ALL Resolved holders with data are violated.
    /// Returns true if all are violated (ConfirmedBad — evaluation stops).
    /// Returns false if any Resolved holder with data is NOT violated (continue to Tier 3).
    /// Holders with null ReadSlot do not participate.
    /// </summary>
    private static bool AreAllResolvedViolated(IReadOnlyList<MetricSlotHolder> holders)
    {
        var checkedCount = 0;

        foreach (var holder in holders)
        {
            if (holder.Role != "Resolved")
                continue;

            var slot = holder.ReadSlot();
            if (slot is null)
                continue; // Does not participate in "all violated" check

            checkedCount++;

            if (!IsViolated(holder, slot))
                return false; // Not all Resolved are violated — continue to Tier 3
        }

        // If at least one Resolved holder was checked and ALL were violated → ConfirmedBad
        // If none were checked (all null or no Resolved) → vacuous true (defensive)
        return true;
    }

    /// <summary>
    /// Tier 3: Checks whether ALL Evaluate holders with data are violated.
    /// Returns true if all are violated (proceed to Tier 4 command dispatch).
    /// Returns false if any Evaluate holder with data is NOT violated (Healthy).
    /// Holders with null ReadSlot do not participate.
    /// If no Evaluate holders have data, returns false (vacuous fail — no command).
    /// </summary>
    private static bool AreAllEvaluateViolated(IReadOnlyList<MetricSlotHolder> holders)
    {
        var checkedCount = 0;

        foreach (var holder in holders)
        {
            if (holder.Role != "Evaluate")
                continue;

            var slot = holder.ReadSlot();
            if (slot is null)
                continue; // Does not participate in "all violated" check

            checkedCount++;

            if (!IsViolated(holder, slot))
                return false; // Not all Evaluate are violated — tenant is Healthy
        }

        // If at least one Evaluate holder was checked and ALL were violated → proceed to Tier 4
        // If none were checked (all null or no Evaluate) → vacuous false (no data = no command)
        return checkedCount > 0;
    }

    /// <summary>
    /// Checks whether a holder's current value violates its threshold bounds.
    /// Uses strict inequality: boundary values (exactly Min or Max) are in-range (NOT violated).
    /// No threshold (null or both Min/Max null) is treated as violated.
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

        // Strict inequality: value < Min is violated, value == Min is in-range
        if (threshold.Min is not null && value < threshold.Min.Value)
            return true;

        // Strict inequality: value > Max is violated, value == Max is in-range
        if (threshold.Max is not null && value > threshold.Max.Value)
            return true;

        return false; // Value is within threshold range
    }
}
