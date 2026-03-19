---
phase: 59-advance-gate-fix-and-simulation
plan: 01
subsystem: snapshot-evaluation
tags: [TierResult, advance-gate, suppression, priority-starvation, SnapshotJob]

# Dependency graph
requires:
  - phase: 58-sts-06-07-staleness-commands
    provides: SnapshotJob with staleness-to-commands, tier fixes, single-tenant direct eval
provides:
  - TierResult enum renamed: Resolved/Healthy/Unresolved (was Violated/Healthy/Commanded)
  - Advance gate bug fix: tier=4 always returns Unresolved regardless of enqueueCount
  - Unit tests corrected for new enum semantics
affects:
  - 59-02-priority-starvation-simulation

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Tier=4 = command intent = Unresolved: reaching tier=4 means the device state is unresolved regardless of whether commands dispatched (suppression/channel full). Advance gate must block."

key-files:
  created: []
  modified:
    - src/SnmpCollector/Jobs/SnapshotJob.cs
    - tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs

key-decisions:
  - "Tier=4 always returns TierResult.Unresolved: command intent (reaching tier=4) means device state is unresolved, even if all commands suppressed or channel full. Ternary removed."
  - "Advance gate blocks on TierResult.Unresolved: P2 group cannot evaluate while P1 is in tier=4 (suppression window), defeating priority starvation bug."

patterns-established:
  - "TierResult.Resolved: tier-2 return — all Resolved metrics violated, no commands needed"
  - "TierResult.Healthy: tier-3 return — not all Evaluate metrics violated, no action"
  - "TierResult.Unresolved: tier-4 return — command intent reached, device state unresolved"

# Metrics
duration: 6min
completed: 2026-03-19
---

# Phase 59 Plan 01: Advance Gate Fix & TierResult Rename Summary

**TierResult enum renamed (Violated->Resolved, Commanded->Unresolved) and tier=4 bug fixed so suppressed commands no longer allow lower-priority groups to cascade through the advance gate.**

## Performance

- **Duration:** 6 min
- **Started:** 2026-03-19T15:11:51Z
- **Completed:** 2026-03-19T15:17:00Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Renamed TierResult enum values for semantic clarity: Violated->Resolved, Commanded->Unresolved
- Fixed advance gate bug: tier=4 now always returns `TierResult.Unresolved` regardless of `enqueueCount`; the old ternary `enqueueCount > 0 ? Commanded : Violated` was the root cause of P2 evaluating during P1 suppression windows
- Updated all 453 unit tests (mechanical rename + 2 semantic corrections for suppressed/channel-full paths)

## Task Commits

Each task was committed atomically:

1. **Task 1: Rename TierResult enum and fix advance gate bug in SnapshotJob.cs** - `7eb92cb` (fix)
2. **Task 2: Update all unit tests for renamed enum values and fix semantic assertions** - `2e9dd12` (test)

## Files Created/Modified

- `src/SnmpCollector/Jobs/SnapshotJob.cs` - TierResult renamed, totalCommanded->totalUnresolved, advance gate condition, tier-2 return, tier-4 ternary removed
- `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` - Mechanical rename of all enum values + semantic fix for two suppressed/channel-full tests

## Decisions Made

- **Tier=4 always returns Unresolved:** Command intent (reaching tier=4 at all) means the device state is unresolved, regardless of whether commands were actually dispatched. Suppression and channel-full are operational states, not correctness states. The old ternary was semantically wrong.
- **Two tests required semantic fix (not just rename):** `Execute_CommandSuppressed_NoTryWrite_IncrementSuppressed` and `Execute_ChannelFull_IncrementFailed_NoException` both asserted `Violated` (now `Resolved` after rename), but the bug fix changes their expected behavior to `Unresolved`.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Advance gate bug fixed; P2 will no longer evaluate while P1 is in a suppression window
- Ready for 59-02: priority starvation simulation scenario (MTS-03)

---
*Phase: 59-advance-gate-fix-and-simulation*
*Completed: 2026-03-19*
