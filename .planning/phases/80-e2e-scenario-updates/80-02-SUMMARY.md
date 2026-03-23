---
phase: 80-e2e-scenario-updates
plan: "02"
subsystem: testing
tags: [e2e, bash, prometheus, gauges, tenant-metrics, v2.5]

# Dependency graph
requires:
  - phase: 77-gather-then-decide-evaluation-flow
    provides: v2.5 EvaluateTenant with single exit point recording all 6 percentage gauges
  - phase: 80-e2e-scenario-updates
    plan: "01"
    provides: Updated TVM-01/02/06 scenarios with v2.5 gauge assertions baseline
provides:
  - 109-tvm03-resolved.sh asserts tenant_evaluation_state=2, dispatched_percent=0, resolved_percent>0, stale_percent present
  - 110-tvm04-healthy.sh asserts tenant_evaluation_state=1, all percentages=0, P99>0
  - 111-tvm05-unresolved.sh asserts tenant_evaluation_state=3, dispatched_percent>0, evaluate_percent>0
affects:
  - future e2e scenario additions referencing TVM patterns
  - run-all.sh scenario execution

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Direct gauge query via query_prometheus + jq for v2.5 percentage assertions"
    - "awk numeric comparison for float gauge values (not integer -eq/-gt)"
    - "Poll loop with DEADLINE for gauges that may take a scrape cycle to appear"
    - "Duration histogram count delta retained as valid (monotonic counter, not gauge)"

key-files:
  created: []
  modified:
    - tests/e2e/scenarios/109-tvm03-resolved.sh
    - tests/e2e/scenarios/110-tvm04-healthy.sh
    - tests/e2e/scenarios/111-tvm05-unresolved.sh

key-decisions:
  - "Keep duration histogram _count delta assertions -- histogram counter is still monotonic, delta approach valid"
  - "Use awk numeric comparison for float gauge values since bash integer ops not safe on doubles"
  - "Poll loop (30s) for dispatched_percent in Unresolved scenario -- gauge may lag a scrape cycle after tier=4 fires"
  - "Remove all BEFORE_DISPATCHED/AFTER_DISPATCHED snapshot_counter patterns -- replaced by direct gauge query"

patterns-established:
  - "v2.5 gauge assertions: query_prometheus '<gauge>{tenant_id=...}' | jq -r '.data.result[0].value[1] // <default>'"
  - "Float zero-check: awk '{exit ($1 == 0) ? 0 : 1}'"
  - "Float gt-check: awk '{exit ($1 > 0) ? 0 : 1}'"

# Metrics
duration: 15min
completed: 2026-03-23
---

# Phase 80 Plan 02: E2E TVM Scenario Rewrite (Resolved/Healthy/Unresolved) Summary

**TVM-03/04/05 e2e scenarios rewritten from counter-delta assertions to direct v2.5 percentage gauge queries, asserting tenant_evaluation_state and all relevant tenant_metric_*_percent / tenant_command_*_percent gauges per evaluation path**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-23T19:38:00Z
- **Completed:** 2026-03-23T19:54:39Z
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments

- Rewrote 109-tvm03-resolved.sh: renamed tenant_state to tenant_evaluation_state, replaced dispatched_total delta with dispatched_percent==0 gauge query, added resolved_percent>0 and stale_percent presence assertions
- Rewrote 110-tvm04-healthy.sh: renamed tenant_state to tenant_evaluation_state, replaced dispatched_total delta with dispatched_percent==0 gauge query, added stale_percent==0 and evaluate_percent==0 assertions for zero-violation Healthy path
- Rewrote 111-tvm05-unresolved.sh: renamed tenant_state to tenant_evaluation_state, replaced counter poll+delta for dispatched with gauge polling loop (dispatched_percent>0), added evaluate_percent>0 assertion for violated evaluate OID

## Task Commits

Each task was committed atomically:

1. **Task 1: Rewrite 109-tvm03-resolved.sh** - `d618730` (feat)
2. **Task 2: Rewrite 110-tvm04-healthy.sh** - `1e05c8f` (feat)
3. **Task 3: Rewrite 111-tvm05-unresolved.sh** - `2fe2a89` (feat)

## Files Created/Modified

- `tests/e2e/scenarios/109-tvm03-resolved.sh` - 5 assertions: state=2, duration delta, dispatched_percent=0, resolved_percent>0, stale_percent present
- `tests/e2e/scenarios/110-tvm04-healthy.sh` - 6 assertions: state=1, duration delta, dispatched_percent=0, P99>0, stale_percent=0, evaluate_percent=0
- `tests/e2e/scenarios/111-tvm05-unresolved.sh` - 4 assertions: state=3, dispatched_percent>0 (polled), duration delta, evaluate_percent>0

## Decisions Made

- Retained duration histogram `_count` delta assertion in all three scenarios: the histogram counter is still monotonic (it increments per evaluation cycle), so the delta approach is valid and correctly proves evaluation ran.
- Used `awk` numeric comparison for all gauge float assertions rather than bash integer arithmetic, since percentage gauges are doubles (e.g., 100.0, not 100).
- Added a 30s polling loop for `dispatched_percent` in the Unresolved scenario: the gauge records at the single exit point in EvaluateTenant, which may lag one Prometheus scrape cycle after the tier=4 log appears.
- Removed all `BEFORE_DISPATCHED` / `AFTER_DISPATCHED` `snapshot_counter` baselines for command counters in all three files; replaced with point-in-time gauge queries.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All three stateful evaluation path scenarios (Resolved, Healthy, Unresolved) are updated for v2.5 percentage gauges
- Phase 80 plan 02 is complete; remaining TVM scenarios (107-tvm01, 108-tvm02, 112-tvm06) were handled in plan 01
- E2E test suite is ready for v2.5 deployment validation

---
*Phase: 80-e2e-scenario-updates*
*Completed: 2026-03-23*
