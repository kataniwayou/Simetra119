---
phase: 55-advanced-scenarios
plan: 02
subsystem: testing
tags: [e2e, snmp, prometheus, depth3, time-series, all-samples, recovery, bash]

# Dependency graph
requires:
  - phase: 55-advanced-scenarios plan 01
    provides: agg_breach simulator scenario and ADV-01 script proving aggregate tier=4 path
  - phase: 54-mts-scenarios
    provides: MTS-02 advance gate scenario establishing multi-phase scenario pattern
  - phase: 53-sts-scenarios
    provides: STS-02 tier=4 and STS-05 depth-3 staleness patterns
provides:
  - ADV-02 depth-3 all-samples scenario script with breach and recovery phases (4 sub-assertions)
  - Proof that AreAllEvaluateViolated fires tier=4 only after all 3 time-series slots are violated
  - Proof that a single in-range sample recovers to tier=3 with zero commands (partial violation safe)
affects: [run-all.sh, e2e verification pipeline, v2.1 milestone completion]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Recovery delta == 0 assertion: capture baseline after scenario switch, assert after recovery log confirmed
    - Two-phase scenario script: breach phase (tier=4 + counter) -> recovery phase (tier=3 + zero delta)
    - since=30 in tier=3 poll to avoid matching pre-breach logs from same tenant

key-files:
  created:
    - tests/e2e/scenarios/37-adv-02-depth3-allsamples.sh
  modified: []

key-decisions:
  - "Recovery baseline captured after sim_set_scenario healthy (not after breach) so delta measures only the recovery window"
  - "since=30 in poll_until_log for tier=3 to focus on recent logs and avoid pre-breach tier=3 matches"
  - "90s poll timeout for both tier=4 (37a) and tier=3 recovery (37c) accommodates TimeSeriesSize=3 fill time plus SnapshotJob cycle"

patterns-established:
  - "Two-phase scenario structure: breach (set violated scenario -> assert tier=4 + counter delta) -> recovery (switch to healthy -> assert tier=3 + zero counter delta)"
  - "Recovery counter isolation: baseline after scenario switch, assert after event confirmed, delta proves no commands during observation window"

# Metrics
duration: 1min
completed: 2026-03-17
---

# Phase 55 Plan 02: Advanced Scenarios — ADV-02 Depth-3 All-Samples Summary

**ADV-02 depth-3 all-samples scenario proving AreAllEvaluateViolated fires tier=4 only after all 3 slots violated, and a single in-range sample recovers to tier=3 with zero counter delta**

## Performance

- **Duration:** 1 min
- **Started:** 2026-03-17T14:40:07Z
- **Completed:** 2026-03-17T14:41:16Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments

- Created ADV-02 scenario script with 4 sub-assertions covering the full breach-and-recovery cycle for AreAllEvaluateViolated logic
- Breach phase: `agg_breach` fills all 3 time-series slots with sum=100 > Max:80; tier=4 fires (37a) and sent counter increments (37b)
- Recovery phase: `healthy` switches one slot in-range (sum=0); tier=3 log appears (37c) and counter delta == 0 proves no further commands were dispatched (37d)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create ADV-02 depth-3 all-samples scenario script** - `fc8bcac` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `tests/e2e/scenarios/37-adv-02-depth3-allsamples.sh` - ADV-02 two-phase scenario: breach (tier=4 + counter) and recovery (tier=3 + zero delta), 4 sub-assertions

## Decisions Made

- **Recovery baseline captured after switching to healthy (not after breach):** The goal is to prove no NEW commands fire during recovery. Baseline after `sim_set_scenario healthy` and before `poll_until_log` tier=3 ensures the delta measures only the recovery observation window.
- **since=30 in tier=3 poll_until_log:** After the breach phase produces tier=3 logs (from earlier SnapshotJob cycles), a `since=30` scope prevents false positives from pre-breach tier=3 entries for the same tenant ID.
- **90s poll timeout for both tier=4 and tier=3 assertions:** Consistent with STS-02, MTS-01, and ADV-01 patterns for TimeSeriesSize=3 configurations. Accommodates 3 poll cycles (30s) + SnapshotJob (15s) + jitter.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- ADV-02 completes the final v2.1 scenario coverage gap
- Both ADV-01 (index 35) and ADV-02 (index 36) are covered by the report.sh range extended in plan 55-01 to `|28|36|`
- v2.1 milestone is complete: all 37 scenarios (01–37) scripted, committed, and ready for cluster execution via run-all.sh

---
*Phase: 55-advanced-scenarios*
*Completed: 2026-03-17*
