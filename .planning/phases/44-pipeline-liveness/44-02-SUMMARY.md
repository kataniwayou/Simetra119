---
phase: 44-pipeline-liveness
plan: 02
subsystem: pipeline
tags: [liveness, heartbeat, health-checks, staleness, IOptions, pipeline-arrival]

# Dependency graph
requires:
  - phase: 44-01
    provides: IHeartbeatLivenessService singleton registered in DI, stamped by OtelMetricHandler on heartbeat pipeline arrival
  - phase: 08-graceful-shutdown-and-health-probes
    provides: LivenessHealthCheck pattern (ILivenessVectorService + IJobIntervalRegistry + IOptions<LivenessOptions>)
provides:
  - LivenessHealthCheck extended with pipeline-arrival staleness detection (HB-06, HB-07)
  - pipeline-heartbeat key in health check diagnostic data dictionary
  - Null LastArrival (never stamped since startup) treated as stale
  - Threshold = IOptions<HeartbeatJobOptions>.Value.IntervalSeconds * LivenessOptions.GraceMultiplier (no hardcoded values)
  - 4 new tests: fresh/stale/never-stamped pipeline scenarios, fresh-jobs-stale-pipeline isolation
affects:
  - future-ops: pipeline staleness now surfaced in K8s liveness probe 503 response body

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Additional check after job-stamp foreach loop: pipeline-heartbeat appended to allEntries/staleEntries before evaluation"
    - "Null sentinel for never-stamped: ageSeconds=(double?)null, lastStamp=(string?)null, stale=true"
    - "Optional parameter test helper pattern: CreateFreshHeartbeatLiveness() isolates job-stamp tests from pipeline check"
    - "StaleHeartbeatLivenessService test double: fixed DateTimeOffset? for deterministic staleness testing"

key-files:
  created: []
  modified:
    - src/SnmpCollector/HealthChecks/LivenessHealthCheck.cs
    - tests/SnmpCollector.Tests/HealthChecks/LivenessHealthCheckTests.cs

key-decisions:
  - "Use heartbeatOptions.Value.IntervalSeconds (runtime-configured) not HeartbeatJobOptions.DefaultIntervalSeconds (compile-time const)"
  - "Pipeline check inserted AFTER job-stamp foreach loop, BEFORE staleEntries.Count evaluation — existing job logic unchanged"
  - "Null LastArrival always stale: K8s failureThreshold=3 at periodSeconds=15 provides 45s startup margin"
  - "CreateFreshHeartbeatLiveness() helper stamps immediately to isolate existing tests from pipeline-check interference"

patterns-established:
  - "pipeline-heartbeat: sentinel key in health check data for pipeline arrival staleness alongside per-job keys"
  - "Test isolation via helper that stamps HeartbeatLivenessService: preserves existing test semantics after new constructor param"

# Metrics
duration: 2min
completed: 2026-03-15
---

# Phase 44 Plan 02: Pipeline Liveness - LivenessHealthCheck Extension Summary

**LivenessHealthCheck extended with pipeline-arrival staleness via IHeartbeatLivenessService, threshold = IntervalSeconds * GraceMultiplier, null LastArrival always stale, pipeline-heartbeat key in diagnostic data**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-15T16:15:36Z
- **Completed:** 2026-03-15T16:17:42Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- Extended `LivenessHealthCheck` constructor with `IHeartbeatLivenessService` and `IOptions<HeartbeatJobOptions>` — threshold computed from runtime-configured `IntervalSeconds * GraceMultiplier`
- Pipeline-arrival check runs after job-stamp foreach loop: null LastArrival → stale; age > threshold → stale; age <= threshold → healthy; `pipeline-heartbeat` key always present in diagnostic data
- Updated `CreateCheck` helper with optional params; added `CreateFreshHeartbeatLiveness()` to isolate 7 existing tests from pipeline interference
- Added `StaleHeartbeatLivenessService` test double and 4 new tests; 338 total tests green (up from 334)
- HB-08/09/10 preserved: `HeartbeatJob.cs`, `OidMapService.cs`, `ILivenessVectorService` untouched

## Task Commits

Each task was committed atomically:

1. **Task 1: Extend LivenessHealthCheck with pipeline-arrival staleness check** - `bb6ce96` (feat)
2. **Task 2: Update LivenessHealthCheckTests and add pipeline liveness tests** - `68be13c` (test)

**Plan metadata:** (docs commit follows)

## Files Created/Modified
- `src/SnmpCollector/HealthChecks/LivenessHealthCheck.cs` - Added 2 constructor params, pipeline-arrival block with null/fresh/stale cases
- `tests/SnmpCollector.Tests/HealthChecks/LivenessHealthCheckTests.cs` - CreateCheck updated, CreateFreshHeartbeatLiveness(), StaleHeartbeatLivenessService, 4 new test methods

## Decisions Made
- Used `heartbeatOptions.Value.IntervalSeconds` (runtime-configured via IOptions) rather than `HeartbeatJobOptions.DefaultIntervalSeconds` (compile-time const) — ensures operators can override interval and staleness threshold adapts
- Pipeline check is additive: inserted after the existing job-stamp foreach, before the staleEntries evaluation — zero disruption to existing logic
- Null LastArrival always treated as stale because K8s `failureThreshold=3` at `periodSeconds=15` provides 45s of startup margin; `HeartbeatJob` fires with `StartNow()` so first arrival arrives within 15–30s

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Phase 44 (pipeline-liveness) is complete: IHeartbeatLivenessService stamped in OtelMetricHandler (44-01) and read in LivenessHealthCheck (44-02)
- HB-06 and HB-07 satisfied; HB-08/09/10 verified unchanged
- 338 tests green; no regressions
- v1.10 Heartbeat Refactor & Pipeline Liveness fully delivered

---
*Phase: 44-pipeline-liveness*
*Completed: 2026-03-15*
