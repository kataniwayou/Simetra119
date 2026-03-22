# Phase 73: SnapshotJob Instrumentation - Context

**Gathered:** 2026-03-23
**Status:** Ready for planning

<domain>
## Phase Boundary

Wire all 8 TenantMetricService methods into SnapshotJob's EvaluateTenant and CommandWorkerService so every evaluation cycle records live per-tenant counters, gauge state, and histogram duration to Prometheus.

</domain>

<decisions>
## Implementation Decisions

### Counter increment points
- All tier counters (stale, resolved, evaluate) always record the count regardless of which tier the tenant exits at — except NotReady
- **NotReady path: no tier counters or command counters fire.** Only state gauge + duration recorded. Reason: adding 0 to a counter that was never evaluated is misleading — implies "checked and found nothing" when nothing was checked
- tier1_stale: always count stale holders even when tenant passes tier-1 (some stale but not all)
- tier2_resolved: always record the count of resolved-role metrics not violated, even when 0 (gate passes)
- tier3_evaluate: always record the count of violated evaluate metrics, even when 0 (tenant healthy)

### Command counter placement
- tenant_command_dispatched: incremented in EvaluateTenant when command queued to channel (decision point, all pods)
- tenant_command_suppressed: incremented in EvaluateTenant when suppression cache blocks (all pods)
- tenant_command_failed: incremented in TWO places:
  - EvaluateTenant: channel-full drops (all pods)
  - CommandWorkerService: SET failures — SNMP error, timeout, device not found (leader only)
- **CommandRequest record must be extended with TenantId and Priority** so CommandWorkerService has tenant context for failed increments
- Followers show failed only from drops; leader shows failed from both drops and SET failures — this is per-pod reality, not asymmetry

### State gauge timing
- Record tenant_state on every cycle (not only on change) — Prometheus always has a fresh data point
- Record after tier counters, before return — state reflects what the counters just measured
- NotReady path: state gauge (NotReady=0) + duration both record, tier counters do not

### Duration measurement scope
- Stopwatch wraps entire EvaluateTenant method — start at method entry, stop before return
- Includes: pre-tier readiness check, all tier evaluations, command dispatch loop
- Excludes: async SET execution in CommandWorkerService (that's after the channel write)
- Stopwatch placed inside EvaluateTenant (method measures itself), not in the caller

### Claude's Discretion
- Exact Stopwatch pattern (single start with record before each return vs try/finally)
- Whether to use a local helper method for the counter recording block
- CommandWorkerService injection approach for ITenantMetricService

</decisions>

<specifics>
## Specific Ideas

- EvaluateTenant already has `_tenantMetrics` field wired from Phase 72 (stored, not called)
- CommandWorkerService needs ITenantMetricService injected and CommandRequest extended with TenantId + Priority
- The existing `_pipelineMetrics.IncrementCommand*` calls remain — tenant counters are additive, not replacements

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 73-snapshotjob-instrumentation*
*Context gathered: 2026-03-23*
