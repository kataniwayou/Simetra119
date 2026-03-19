---
phase: 60-readiness-window-for-holders
plan: 02
subsystem: jobs
tags: [SnapshotJob, readiness, staleness, TierResult, sentinel-removal, unit-tests]

# Dependency graph
requires:
  - phase: 60-readiness-window-for-holders
    plan: 01
    provides: MetricSlotHolder.IsReady, ConstructedAt, ReadinessGrace — consumed by AreAllReady()
provides:
  - Pre-tier readiness check in EvaluateTenant (AreAllReady → TierResult.Unresolved for not-ready tenants)
  - Updated HasStaleness: null ReadSlot post-readiness returns true (stale) instead of continue
  - AreAllReady() static method delegating to MetricSlotHolder.IsReady
  - All sentinel-dependent tests rewritten with new readiness semantics
  - 3 new pre-tier readiness tests added
  - Orphaned TenantVectorRegistry sentinel test fixed (missed in Plan 01)
affects:
  - E2E scenarios: 33-sts-05, 40-mts-03 (comment updates only; behavior unchanged)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pre-tier readiness gate: AreAllReady() before Tier 1 blocks not-ready tenants with Unresolved"
    - "Null-slot-as-stale: post-readiness null ReadSlot is stale (device never responded)"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Jobs/SnapshotJob.cs
    - tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs
    - tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
    - tests/e2e/scenarios/33-sts-05-staleness.sh
    - tests/e2e/scenarios/40-mts-03-starvation-proof.sh

key-decisions:
  - "AreAllReady() is a private static method in SnapshotJob rather than inlining IsReady checks — keeps EvaluateTenant readable and mirrors HasStaleness/AreAllResolvedViolated pattern"
  - "HasStaleness null-slot changed from continue to return true — safe because HasStaleness is only reachable after AreAllReady passes; a null slot post-readiness means the device genuinely never responded"
  - "EvaluateTenant_ResolvedEmptyHolder_SkippedInGate uses IntervalSeconds=0 holder — excluded from staleness check, empty series skipped in gate; cleaner than async Task.Delay approach"
  - "TenantVectorRegistryTests.Reload_NewMetric_StartsWithSentinel fixed as Rule 1 bug — orphaned from Plan 01 sentinel removal, renamed to StartsEmpty with null assertion"

patterns-established:
  - "Pre-tier gate pattern: check readiness before any tier evaluation, return Unresolved immediately"
  - "Null-slot-is-stale pattern: after readiness confirmed, absence of data means device never responded"

# Metrics
duration: 6min
completed: 2026-03-19
---

# Phase 60 Plan 02: Readiness Pre-Tier Check and Sentinel Test Cleanup Summary

**Pre-tier readiness gate in SnapshotJob blocks not-ready tenants, null post-readiness slots are stale, all sentinel tests rewritten for new semantics**

## Performance

- **Duration:** 6 min
- **Started:** 2026-03-19T17:08:50Z
- **Completed:** 2026-03-19T17:15:05Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments

- Added `AreAllReady()` static method to `SnapshotJob` — delegates to `MetricSlotHolder.IsReady`, returns false if any holder is not yet past its readiness grace window
- Inserted pre-tier readiness check at top of `EvaluateTenant()` — not-ready tenants return `TierResult.Unresolved` immediately, blocking the advance gate with the same semantics as an Unresolved device
- Updated `HasStaleness` null-slot handling: `return true` (stale) instead of `continue` — safe only after readiness confirmed; a null slot post-readiness means the device never responded
- Updated `HasStaleness` XML doc to reflect the new post-readiness semantics
- Rewrote 2 sentinel-dependent tests, updated 2 comment-only tests, added 3 new pre-tier readiness tests, fixed 1 orphaned registry test — all 462 tests pass
- Updated E2E scenario comments to reference readiness grace window rather than sentinel behavior

## Task Commits

Each task was committed atomically:

1. **Task 1: Add readiness pre-tier check and update HasStaleness** - `f639d89` (feat)
2. **Task 2: Rewrite sentinel tests, add readiness tests, fix orphaned registry test** - `303e01f` (test)

**Plan metadata:** (committed with this summary)

## Files Created/Modified

- `src/SnmpCollector/Jobs/SnapshotJob.cs` — AreAllReady() added, pre-tier check inserted, HasStaleness null-slot changed to return true
- `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` — 2 tests rewritten, 2 comments updated, 3 new readiness tests added (60 tests total, up from 57)
- `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` — Orphaned sentinel test renamed and assertion updated
- `tests/e2e/scenarios/33-sts-05-staleness.sh` — Priming comments updated to reference readiness grace window
- `tests/e2e/scenarios/40-mts-03-starvation-proof.sh` — Phase 60 note added about 90s timeout absorbing grace window

## Decisions Made

- `AreAllReady()` is a private static method following the same pattern as `HasStaleness`, `AreAllResolvedViolated`, `AreAllEvaluateViolated` — readable, testable, consistent.
- `HasStaleness` null-slot changed to `return true` rather than adding a secondary check — the pre-tier gate guarantees readiness before `HasStaleness` is ever called, so a null slot at this point unambiguously means the device never sent data within the grace window.
- `EvaluateTenant_ResolvedEmptyHolder_SkippedInGate` uses `IntervalSeconds=0` for h2 (excluded from staleness) with no write (empty series skipped in gate) — avoids `async/await` complexity for a synchronous concept.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed orphaned TenantVectorRegistryTests.Reload_NewMetric_StartsWithSentinel**

- **Found during:** Task 2 (full test suite run)
- **Issue:** Test asserted `ReadSlot() != null` and `slot.Value == 0` (sentinel value) — but Plan 01 removed the sentinel, making `ReadSlot()` return null on fresh holders. Test was failing with `Assert.NotNull() Failure: Value is null`.
- **Fix:** Renamed to `Reload_NewMetric_StartsEmpty`, changed assertion to `Assert.Null(slot)` with explanatory comment
- **Files modified:** `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs`
- **Commit:** `303e01f`

## Issues Encountered

None beyond the orphaned test above.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- Phase 60 is complete (both plans done)
- The MTS-03 priming fix todo noted in earlier docs is now unnecessary — the readiness grace window eliminates the startup race that required priming
- No blockers for any subsequent work

---
*Phase: 60-readiness-window-for-holders*
*Completed: 2026-03-19*
