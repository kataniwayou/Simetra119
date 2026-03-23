---
phase: 80-e2e-scenario-updates
plan: 01
subsystem: testing
tags: [e2e, bash, prometheus, tenant-metrics, v2.5, gauges]

# Dependency graph
requires:
  - phase: 77-gather-then-decide-evaluation-flow
    provides: v2.5 EvaluateTenant with percentage gauges replacing counters
  - phase: 76-tenant-metric-service-v2
    provides: RecordXxxPercent API and gauge instrument names
provides:
  - E2E scenarios 107/108/112 updated for v2.5 percentage gauge metric surface
  - Smoke test (107) explicitly asserts 6 old v2.4 counter names absent
  - NotReady test (108) simplified to state + duration only assertions
  - All-instances test (112) queries tenant_evaluation_state on all pods
affects: [future-e2e-runs, v2.5-release-validation]

# Tech tracking
tech-stack:
  added: []
  patterns: [gauge-presence-check, absence-check-via-label-matcher, awk-float-comparison]

key-files:
  created: []
  modified:
    - tests/e2e/scenarios/107-tvm01-smoke.sh
    - tests/e2e/scenarios/108-tvm02-notready.sh
    - tests/e2e/scenarios/112-tvm06-all-instances.sh

key-decisions:
  - "TVM-01J stale path: query gauge value directly and compare > 0 via awk (no snapshot_counter/delta needed for gauges)"
  - "TVM-01E no longer needs poll_until_exists fallback: percentage gauges are always present after first recording"
  - "TVM-02C/D/E old tier counter assertions removed; replaced with single informational stale_percent observation"

patterns-established:
  - "_tvm01_assert_absent helper: uses {__name__=metric,tenant_id=...} label matcher for absence checks"
  - "awk float comparison pattern: echo $VALUE | awk '{exit ($1 > 0) ? 0 : 1}'"

# Metrics
duration: 2min
completed: 2026-03-23
---

# Phase 80 Plan 01: E2E Scenario Updates (107/108/112) Summary

**Three E2E scenarios rewritten for v2.5 percentage gauges: smoke asserts 6 new gauge names present + 6 old counter names absent, NotReady simplified to state+duration-only, all-instances renamed to tenant_evaluation_state throughout.**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-23T18:31:09Z
- **Completed:** 2026-03-23T18:33:36Z
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments

- Smoke test (107) now asserts all 6 v2.5 percentage gauges present (TVM-01A through TVM-01F), tenant_evaluation_state (G), duration histogram (H), priority label (I), stale_percent > 0 after stale OID (J), and 6 old v2.4 counter names absent (K-P)
- NotReady test (108) removes all snapshot_counter/tier-counter code; asserts only tenant_evaluation_state valid (0-3), duration > 0, and informational stale_percent observation
- All-instances test (112) is a pure find-and-replace of tenant_state -> tenant_evaluation_state across all PromQL queries (18 occurrences)

## Task Commits

Each task was committed atomically:

1. **Task 1: Rewrite 107-tvm01-smoke.sh for v2.5 gauge presence and counter absence** - `643105d` (feat)
2. **Task 2: Rewrite 108-tvm02-notready.sh for v2.5 (state + duration only)** - `b250fd1` (feat)
3. **Task 3: Rewrite 112-tvm06-all-instances.sh for tenant_evaluation_state rename** - `53f606f` (feat)

## Files Created/Modified

- `tests/e2e/scenarios/107-tvm01-smoke.sh` - Presence checks for 6 percentage gauges; stale_percent > 0 gauge assertion; 6 absence checks for v2.4 counters; removed snapshot_counter/delta arithmetic
- `tests/e2e/scenarios/108-tvm02-notready.sh` - Removed tier counter snapshots and sleep; renamed to tenant_evaluation_state; added informational TVM-02C stale_percent observation
- `tests/e2e/scenarios/112-tvm06-all-instances.sh` - All PromQL queries and comments updated from tenant_state to tenant_evaluation_state

## Decisions Made

- TVM-01E no longer needs poll_until_exists fallback: percentage gauges are always present after first recording (unlike OTel counters which only appear after the first Add call). Simplified to a direct _tvm01_assert_metric call.
- TVM-01J stale path verification changed from snapshot_counter/poll_until/delta arithmetic to a direct gauge query + awk float comparison > 0. Cleaner and correct for gauges.
- TVM-02C/D/E old informational tier counter delta passes removed entirely. Replaced with a single informational TVM-02C observing whether stale_percent series are absent (correct for NotReady) or carried over from prior runs.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- 80-01 complete. Three simpler scenarios (107/108/112) updated for v2.5.
- 80-02 already committed (scenarios 109/110/111 for Resolved, Healthy, Stale paths).
- Phase 80 can be marked complete after 80-02 SUMMARY is created.

---
*Phase: 80-e2e-scenario-updates*
*Completed: 2026-03-23*
