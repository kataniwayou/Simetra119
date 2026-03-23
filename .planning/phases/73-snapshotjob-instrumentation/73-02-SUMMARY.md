---
phase: 73-snapshotjob-instrumentation
plan: 02
subsystem: telemetry
tags: [snmp, snapshotjob, tenantmetricservice, otel, prometheus, nsubstitute, metrics, stopwatch]

# Dependency graph
requires:
  - phase: 72-tenantmetricservice-meter-registration
    provides: ITenantMetricService interface with all 8 methods, TenantMetricService implementation registered in DI
  - phase: 73-snapshotjob-instrumentation
    plan: 01
    provides: CommandRequest with TenantId/Priority, ITenantMetricService field wired in SnapshotJob constructor
provides:
  - EvaluateTenant fully instrumented: Stopwatch at entry, RecordAndReturn at all 4 return paths
  - CountStaleHolders / CountResolvedNonViolated / CountEvaluateViolated private counting helpers
  - Tier counter loops incrementing once per holder (not by 1 per cycle)
  - Command counters (dispatched/suppressed/failed) in Tier 4 dispatch loop, additive with pipeline counters
  - 5 new NSubstitute-based tests covering all 4 evaluation paths and stale count accuracy
affects:
  - 73-03 (phase complete — this is the final plan in Phase 73)
  - Any future changes to EvaluateTenant evaluation logic
  - Prometheus/Grafana dashboards querying tenant_tier1_stale, tenant_tier2_resolved, tenant_tier3_evaluate, tenant_state, tenant_evaluation_duration_ms

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "RecordAndReturn pattern: single helper records state gauge + duration before every return, eliminating repetition"
    - "CountX helpers mirror boolean check methods (HasStaleness/AreAllResolvedViolated/AreAllEvaluateViolated) but count all matches without short-circuit"
    - "Tier counter loops: for(i=0;i<count;i++) IncrementTierX() — increments once per holder to match Prometheus counter semantics"
    - "Additive command counters: tenant metric calls alongside existing pipeline metric calls at each dispatch path"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Jobs/SnapshotJob.cs
    - tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs

key-decisions:
  - "RecordAndReturn is an instance helper (not static) because it calls _tenantMetrics"
  - "Tier4 path uses separate local variables (staleCountT4/resolvedCountT4/evaluateCountT4) to avoid name shadowing the Tier2/Tier3 locals in the else branch"
  - "CountResolvedNonViolated counts holders where ANY sample is in-range (inverse of AreAllResolvedViolated which requires ALL samples violated)"
  - "CountEvaluateViolated counts holders where ALL samples are violated (mirrors AreAllEvaluateViolated per-holder logic)"
  - "_tenantMetrics extracted as class-level field in SnapshotJobTests to enable Received() assertions across all 5 new tests"

patterns-established:
  - "Per-return instrumentation: RecordAndReturn called at every exit point; no try/finally needed"
  - "Count-then-loop pattern: compute count once, loop that many times calling Increment — avoids needing overload accepting count parameter"

# Metrics
duration: 3min
completed: 2026-03-23
---

# Phase 73 Plan 02: SnapshotJob Instrumentation - EvaluateTenant Summary

**EvaluateTenant fully instrumented: Stopwatch + RecordAndReturn at all 4 paths, tier counters by holder count, command counters per dispatch, 5 NSubstitute path tests**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-23T12:36:54Z
- **Completed:** 2026-03-23T12:40:03Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Added `Stopwatch.StartNew()` at EvaluateTenant entry and `RecordAndReturn` helper that records state gauge + duration before every return
- Added three private counting helpers (`CountStaleHolders`, `CountResolvedNonViolated`, `CountEvaluateViolated`) mirroring existing boolean check methods but counting all matches without short-circuit
- Wired tier counter loops at all applicable paths: Resolved path (tier1+tier2), Healthy path (all 3 tiers), Unresolved path (all 3 tiers); NotReady path intentionally records only state+duration per design decision
- Added additive `_tenantMetrics.IncrementCommand*` calls in Tier 4 dispatch loop alongside existing `_pipelineMetrics.IncrementCommand*` calls
- Extracted `_tenantMetrics` as class-level NSubstitute field in `SnapshotJobTests`; added 5 new tests covering all 4 evaluation paths plus stale count accuracy

## Task Commits

Each task was committed atomically:

1. **Task 1: Add counting helpers and RecordAndReturn to SnapshotJob, wire all metric calls into EvaluateTenant** - `0bb5ce4` (feat)
2. **Task 2: Add unit tests verifying metric calls for all 4 evaluation paths** - `dd2ee51` (test)

**Plan metadata:** (pending docs commit)

## Files Created/Modified

- `src/SnmpCollector/Jobs/SnapshotJob.cs` - RecordAndReturn helper, CountStaleHolders/CountResolvedNonViolated/CountEvaluateViolated counting methods, Stopwatch at EvaluateTenant entry, tier counter loops at Resolved/Healthy/Unresolved paths, command counter calls in Tier 4 dispatch loop
- `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` - Class-level _tenantMetrics field, 5 new tests for NotReady/Resolved/Healthy/Unresolved paths and stale count accuracy

## Decisions Made

- `RecordAndReturn` is an instance method (not static) because it calls `_tenantMetrics`; takes Tenant, TenantState, Stopwatch — minimal API
- Tier 4 path uses distinct local variable names (`staleCountT4`, `resolvedCountT4`, `evaluateCountT4`) to avoid C# scoping conflict with the `staleCount`/`resolvedCount`/`evaluateCount` locals in the nested else block
- `_tenantMetrics` extracted as class-level field in tests (was previously `Substitute.For<ITenantMetricService>()` inline in constructor) to enable `Received()` assertions in new tests; `ClearReceivedCalls()` called at top of each new test to reset state from prior tests

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None. Build succeeded on first attempt (no transient cache issues this time).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 73 complete: all 8 ITenantMetricService methods wired into SnapshotJob and CommandWorkerService
- Every EvaluateTenant cycle now records per-tenant state gauge, duration histogram, tier counters, and command counters to Prometheus
- 475 tests pass (0 regressions)
- Ready for Phase 74: Prometheus/Grafana dashboard authoring using the tenant_* metrics
- No blockers

---
*Phase: 73-snapshotjob-instrumentation*
*Completed: 2026-03-23*
