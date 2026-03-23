---
phase: 77-gather-then-decide-evaluation-flow
plan: 01
subsystem: telemetry
tags: [snapshotjob, tenant-metrics, percentage-gauges, evaluation-flow, otlp]

# Dependency graph
requires:
  - phase: 76-percentage-gauge-instruments
    provides: RecordXxxPercent API on ITenantMetricService replacing Increment* counters
provides:
  - Gather-then-decide EvaluateTenant in SnapshotJob.cs using RecordXxxPercent at single exit
  - 4 new count helpers: CountStalenessEligibleHolders, CountResolvedViolated, CountResolvedParticipating, CountEvaluateParticipating
  - CommandWorkerService cleaned of ITenantMetricService dependency
  - All 476 unit tests passing; dotnet build succeeds (Phase 76 break fixed)
affects: [78-dashboard-percent-panels, 79-e2e-percent-gauges, testing, ops-dashboard]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Gather-then-decide: collect all tier counts before branching on state"
    - "Single exit point: all 6 percentage gauges recorded together after state determination"
    - "Stale path skips resolved/evaluate gathering — unreliable on stale data, records 0%"
    - "NotReady early return: state+duration only, no percentage gauges"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Jobs/SnapshotJob.cs
    - src/SnmpCollector/Services/CommandWorkerService.cs
    - tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs
    - tests/SnmpCollector.Tests/Services/CommandWorkerServiceTests.cs

key-decisions:
  - "CommandWorkerService IncrementCommandFailed calls removed: tenant command metrics belong to dispatch time (SnapshotJob), not execution time (CommandWorkerService)"
  - "ITenantMetricService dependency removed from CommandWorkerService entirely"
  - "Stale path: resolvedTotal=1 (stub denominator) ensures stale path records 0.0% not div/0"

patterns-established:
  - "EvaluateTenant: 1 early return (NotReady), all other paths flow through gather > decide > compute > single exit"
  - "RecordXxxPercent callers compute ratio before calling; service is passive recorder"

# Metrics
duration: 5min
completed: 2026-03-23
---

# Phase 77 Plan 01: Gather-Then-Decide Evaluation Flow Summary

**EvaluateTenant refactored to gather all tier counts before deciding state; all 6 RecordXxxPercent gauges recorded at single exit point; Phase 76 build break fixed**

## Performance

- **Duration:** 5 min
- **Started:** 2026-03-23T17:25:17Z
- **Completed:** 2026-03-23T17:30:46Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- EvaluateTenant rewritten with gather-then-decide flow: single early return (NotReady), all other paths gather tier counts then decide state at one point
- All 6 RecordXxxPercent calls consolidated at single exit; stale/resolved/evaluate/dispatched/failed/suppressed percentages computed from gathered counts
- Phase 76 build break fixed: zero `Increment*` calls remain on `_tenantMetrics` in SnapshotJob.cs
- CommandWorkerService cleaned: removed `ITenantMetricService` dependency (Increment* no longer exists on the interface)
- 5 metric assertion tests in SnapshotJobTests rewritten for new RecordXxxPercent API with exact percent values
- All 476 unit tests pass

## Task Commits

Each task was committed atomically:

1. **Task 1: Add 4 new count helper methods** - `70c824e` (feat)
2. **Task 2: Rewrite EvaluateTenant to gather-then-decide flow** - `c1a4d5e` (feat)

## Files Created/Modified

- `src/SnmpCollector/Jobs/SnapshotJob.cs` - EvaluateTenant rewritten; 4 new count helpers added
- `src/SnmpCollector/Services/CommandWorkerService.cs` - ITenantMetricService removed; IncrementCommandFailed calls removed
- `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` - 5 metric tests rewritten for RecordXxxPercent API
- `tests/SnmpCollector.Tests/Services/CommandWorkerServiceTests.cs` - _tenantMetrics field/parameter removed

## Decisions Made

- **CommandWorkerService metric removal:** The 4 `_tenantMetrics.IncrementCommandFailed` calls in CommandWorkerService were removed entirely rather than replaced with `RecordCommandFailedPercent`. Rationale: tenant command metrics (dispatched/failed/suppressed percentages) are computed from dispatch counts in SnapshotJob at evaluation time. CommandWorkerService operates at execution time (after dispatch) and doesn't have a denominator to compute meaningful percentages. Recording from both sites would double-count.

- **ITenantMetricService removed from CommandWorkerService:** With no remaining `_tenantMetrics` call sites, the field, constructor parameter, and assignment were all removed to keep the service clean.

- **Stale path denominator stub:** When staleness is detected, `resolvedTotal = 1` and `evaluateTotal = 1` are used as stub denominators so the percent computation `0 * 100.0 / 1 = 0.0` correctly records 0% without special-casing or null guards.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed CommandWorkerService calling removed Increment* methods**

- **Found during:** Task 2 verification (dotnet build)
- **Issue:** `CommandWorkerService.cs` had 4 calls to `_tenantMetrics.IncrementCommandFailed` which no longer exists on `ITenantMetricService` (removed in Phase 76). This prevented the SnmpCollector project from building.
- **Fix:** Removed all 4 `_tenantMetrics.IncrementCommandFailed` calls and the `ITenantMetricService` dependency from `CommandWorkerService` entirely. Also updated `CommandWorkerServiceTests` to remove the now-unused `_tenantMetrics` mock field and constructor argument.
- **Files modified:** `src/SnmpCollector/Services/CommandWorkerService.cs`, `tests/SnmpCollector.Tests/Services/CommandWorkerServiceTests.cs`
- **Verification:** `dotnet build` succeeds; all 12 CommandWorkerService tests pass
- **Committed in:** `c1a4d5e` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** The deviation was a necessary fix for a pre-existing Phase 76 break in a file outside SnapshotJob.cs. No scope creep — the fix was the minimal removal of dead calls.

## Issues Encountered

None — the CommandWorkerService issue was straightforward to identify and fix.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 76 build break fully resolved; `dotnet build` succeeds with zero errors
- EvaluateTenant now records 6 percentage gauges per tenant per cycle to the SnmpCollector.Tenant meter
- Ready for Phase 78 (dashboard panels for the new percentage gauges) and Phase 79 (E2E verification)
- No blockers

---
*Phase: 77-gather-then-decide-evaluation-flow*
*Completed: 2026-03-23*
