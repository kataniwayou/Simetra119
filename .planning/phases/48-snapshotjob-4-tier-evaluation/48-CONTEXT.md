# Phase 48: SnapshotJob 4-Tier Evaluation - Context

**Gathered:** 2026-03-16
**Status:** Ready for planning

<domain>
## Phase Boundary

SnapshotJob Quartz job with full 4-tier tenant evaluation loop, priority group traversal, liveness stamp, and structured evaluation logs. All prerequisite components exist (ISuppressionCache, ICommandChannel, CommandWorkerService, SnapshotJobOptions). This phase wires them together into the orchestration layer.

</domain>

<decisions>
## Implementation Decisions

### Tier edge cases

**ReadSlot() null (never written):**
- Consistent across ALL tiers: null = cannot judge = **skip this holder**
- Tier 1: null → not stale (holder excluded from staleness check)
- Tier 2/3: null → excluded from "all violated" check (holder doesn't participate)

**Threshold violation (strict inequality, no equal):**
- `Min=5, Max=null` → violated if `value < 5`
- `Min=null, Max=10` → violated if `value > 10`
- `Min=5, Max=10` → violated if `value < 5 OR value > 10` (outside range)
- Value of exactly Min or Max is NOT violated (boundary is in-range)
- `Min=null, Max=null` (no threshold) → **treated as violated** (metric with no threshold acts as violated)

**Empty role groups:**
- Not possible at runtime — tenant config validation (TEN-13) ensures every tenant has required structure before reaching the registry. No vacuous pass/fail logic needed in SnapshotJob.

### Priority group advance gate

**"Tenant violated" definition for advance gate:**
- Violated = ALL Resolved metrics are violated (device confirmed bad state)
- Stale tenant = NOT violated (missing data ≠ confirmed bad)
- Tenant actively commanding (all Evaluate violated but Resolved not all violated) = NOT violated for advance gate

**Group advances when ALL tenants in the group are either:**
1. Confirmed-bad: all Resolved metrics violated → move on
2. Confirmed-healthy: not all Evaluate metrics violated → no action needed → move on

**Group blocks when ANY tenant is:**
- Actively commanding (all Evaluate violated, Resolved not all violated) — commands firing, don't cascade
- Stale (data missing) — uncertain state, don't cascade

### Evaluation logging

**Per-tenant logging:**
- One log entry per tenant per cycle — summary of tier reached
- **Debug level** for non-command outcomes (stale, resolved-gate stop, evaluate-healthy stop)
- **Information level** for command-dispatched outcomes (tenant reached Tier 4, commands enqueued)
- Include: tenant ID, priority, tier reached, holder counts

**Cycle summary:**
- One summary line per snapshot cycle at **Debug level**
- Format: "Snapshot cycle complete: {tenants} evaluated, {commanded} commanded, {stale} stale"

### Claude's Discretion
- SnapshotJob method decomposition (EvaluateTenant, CheckStaleness, CheckThresholds, etc.)
- Whether to return an enum (TierResult) from tenant evaluation or use booleans
- Test fixture organization and helper method design
- Exact structured log field names beyond tenant ID, priority, tier

</decisions>

<specifics>
## Specific Ideas

- SnapshotJob should follow the MetricPollJob pattern: [DisallowConcurrentExecution], correlation ID in finally, liveness stamp in finally
- Priority groups accessed via ITenantVectorRegistry.Groups (already sorted ascending by priority)
- Same-priority tenants visited in parallel (Task.WhenAll), sequential across groups
- TryWrite for command enqueue — false → increment snmp.command.failed, log Warning (don't block SnapshotJob)
- Suppression check via ISuppressionCache.TrySuppress before each command enqueue

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 48-snapshotjob-4-tier-evaluation*
*Context gathered: 2026-03-16*
