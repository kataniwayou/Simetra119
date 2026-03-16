---
phase: 48-snapshotjob-4-tier-evaluation
plan: 02
subsystem: evaluation
tags: [staleness, threshold, resolved-gate, tier-evaluation]

dependency_graph:
  requires: [48-01]
  provides: [tier1-staleness, tier2-resolved-gate, is-violated-helper, tier-result-enum]
  affects: [48-03, 48-04]

tech_stack:
  added: []
  patterns: [4-tier-evaluation, strict-inequality-thresholds, staleness-grace-window]

file_tracking:
  key_files:
    created:
      - tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs
    modified:
      - src/SnmpCollector/Jobs/SnapshotJob.cs

decisions:
  - id: TIER-RESULT-ENUM
    decision: "TierResult enum (Stale, ConfirmedBad, Healthy, Commanded) returned from EvaluateTenant"
    rationale: "Clean return value for priority-group advance gate in 48-04"
  - id: VACUOUS-TRUE
    decision: "AreAllResolvedViolated returns true when no Resolved holders have data"
    rationale: "Defensive handling per CONTEXT.md — empty role groups not possible at runtime"

metrics:
  duration: ~5 min
  completed: 2026-03-16
---

# Phase 48 Plan 02: Tier 1 Staleness and Tier 2 Resolved Gate Summary

**HasStaleness excludes Trap/IntervalSeconds=0/null-slot; AreAllResolvedViolated gates ConfirmedBad stop; IsViolated uses strict inequality with null-threshold-as-violated; 17 tests covering all edge cases**

## What Was Done

### Task 1: Implement Tier 1 staleness and Tier 2 Resolved gate helper methods
Modified `src/SnmpCollector/Jobs/SnapshotJob.cs`:
- Added `TierResult` enum: `Stale`, `ConfirmedBad`, `Healthy`, `Commanded`
- Added `HasStaleness`: filters out `Source==Trap` and `IntervalSeconds==0`, skips null `ReadSlot`, computes age vs grace window (`IntervalSeconds * GraceMultiplier`)
- Added `AreAllResolvedViolated`: filters to `Role=="Resolved"`, skips null `ReadSlot`, returns true only when all checked holders are violated
- Added `IsViolated`: strict inequality (`value < Min` or `value > Max`), null threshold or both bounds null treated as violated
- Added `EvaluateTenant`: applies Tier 1 then Tier 2 with correct gate direction, logs debug per outcome
- Wired `EvaluateTenant` into Execute loop replacing placeholder comment

### Task 2: Unit tests for Tier 1 staleness and Tier 2 Resolved gate
Created `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` with 17 tests:

**Tier 1 (5 tests):**
1. Fresh holder (large interval) → not stale
2. Null ReadSlot (never written) → not stale
3. Source=Trap → excluded from staleness check
4. IntervalSeconds=0 → excluded from staleness check
5. Stale holder (small interval + delay) → Stale

**Tier 2 (6 tests):**
6. All Resolved violated → ConfirmedBad (no commands enqueued)
7. One Resolved in range → continues to Tier 3
8. Resolved null ReadSlot → excluded from gate
9. Resolved no threshold → treated as violated
10. Resolved both bounds null → treated as violated
11. Resolved at exact Min boundary → NOT violated (in-range)
12. Resolved at exact Max boundary → NOT violated (in-range)

**IsViolated direct (4 tests):**
13-16. Below min, above max, in range, only min set

**Execute integration (1 test):**
17. Stamps liveness and clears correlation ID

Test doubles: StubTenantVectorRegistry, StubSuppressionCache, StubCommandChannel, StubCorrelationService, StubLivenessVectorService, StubJobContext

## Verification

- `dotnet build src/SnmpCollector/` compiles with 0 errors
- `dotnet test tests/SnmpCollector.Tests/ --filter "SnapshotJobTests"` — 17 tests pass
- `dotnet test tests/SnmpCollector.Tests/` — all 393 tests pass (376 existing + 17 new)
- Gate direction verified: ALL Resolved violated = ConfirmedBad (STOP), NOT all violated = continue

## Deviations from Plan

None — plan executed exactly as written.

## Commits

| # | Hash | Message |
|---|------|---------|
| 1 | bd4a695 | feat(48-02): implement Tier 1 staleness and Tier 2 Resolved gate in SnapshotJob |
| 2 | 2b74f19 | test(48-02): add 17 unit tests for Tier 1 staleness and Tier 2 Resolved gate |

## Next Phase Readiness

Plans 48-03 and 48-04 will complete the evaluation:
- 48-03: Tier 3 (evaluate-threshold) and Tier 4 (command dispatch) — will use IsViolated and EvaluateTenant infrastructure
- 48-04: Priority group advance gate and cycle summary logging — will use TierResult enum

All infrastructure for Tier 3/4 is in place. EvaluateTenant currently returns `Healthy` as placeholder after passing Tier 2 gate. No blockers.
