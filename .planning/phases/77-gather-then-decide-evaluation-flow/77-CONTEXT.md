# Phase 77: Gather-Then-Decide Evaluation Flow - Context

**Gathered:** 2026-03-23
**Status:** Ready for planning

<domain>
## Phase Boundary

Refactor EvaluateTenant to gather all tier results before deciding state, compute percentages, and record all metrics at a single exit point. SnapshotJob callers updated to use the new RecordXxxPercent API from Phase 76. Unit tests rewritten for new flow.

</domain>

<decisions>
## Implementation Decisions

### State determination rules
- Same tier priority order as v2.4 — logic unchanged, just gathered before deciding:
  1. Stale detected → Unresolved (dispatch commands)
  2. All resolved violated → Resolved (no commands)
  3. All evaluate violated → Unresolved (dispatch commands)
  4. Otherwise → Healthy
- The difference: in v2.4 each tier short-circuited with an early return. Now all results are gathered first, then the same priority rules determine state.

### NotReady special handling
- NotReady returns early (only exception to gather-then-decide)
- Only state + duration recorded. NO percentage gauges recorded.
- Same behavior as v2.4 — NotReady means no evaluation happened, percentages are meaningless

### Stale path behavior
- If staleness detected: compute stale% but skip resolved/evaluate gathering
- Record resolved% = 0 and evaluate% = 0 (stale data is unreliable — computing violations on stale data is misleading)
- Still proceed to command dispatch (stale → Unresolved → commands)
- Command percentages (dispatched/failed/suppressed) recorded normally from dispatch results

### Metric recording
- All 6 percentage gauges + state + duration recorded together at a single exit point after state is determined
- No metrics recorded mid-flow (except NotReady early return with state + duration only)

</decisions>

<specifics>
## Specific Ideas

- The gather phase collects: staleCount, resolvedViolatedCount, evaluateViolatedCount, and the totals from config for each denominator
- Percentages computed after gathering, before state determination
- Command dispatch still happens inline (must dispatch before knowing command percentages)
- After dispatch loop: compute command percentages, determine state, record all metrics, return

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 77-gather-then-decide-evaluation-flow*
*Context gathered: 2026-03-23*
