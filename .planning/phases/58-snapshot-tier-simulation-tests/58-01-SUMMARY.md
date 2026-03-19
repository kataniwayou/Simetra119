---
phase: 58-snapshot-tier-simulation-tests
plan: 01
subsystem: testing
tags: [e2e, bash, scenarios, SnapshotJob, staleness, tier2, tier4, quick-076]

# Dependency graph
requires:
  - phase: quick-076
    provides: SnapshotJob staleness skips to tier=4 command dispatch, ConfirmedBad renamed to Violated
provides:
  - Scenario 31 uses correct post-quick-076 tier=2 log pattern (Violated, not ConfirmedBad)
  - Scenario 33 asserts commands ARE dispatched when stale (reversed from old no-commands behavior)
  - report.sh Snapshot Evaluation range extended to cover scenario indices 28-38
affects:
  - 58-02-PLAN.md (new scenarios must fit within extended report range)
  - 58-03-PLAN.md (full phase run)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "poll_until for counter-increment assertions (matching STS-02 30b pattern) — not immediate snapshot"

key-files:
  created: []
  modified:
    - tests/e2e/scenarios/31-sts-03-resolved-gate.sh
    - tests/e2e/scenarios/33-sts-05-staleness.sh
    - tests/e2e/lib/report.sh

key-decisions:
  - "Scenario 33 uses poll_until 45 5 for counter-increment — same pattern as STS-02 sub-scenario 30b"
  - "Old sub-scenarios 33b (delta==0 sent) and 33c (delta==0 suppressed) removed entirely — stale now dispatches"

patterns-established:
  - "Counter-increment assertions use poll_until not immediate snapshot_counter delta check"

# Metrics
duration: 2min
completed: 2026-03-19
---

# Phase 58 Plan 01: Scenario 31/33 Fixes Summary

**Fixed two broken post-quick-076 e2e scenarios: scenario 31 Violated terminology + log pattern, scenario 33 reversed staleness assertion from no-commands to commands-dispatched**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-19T08:29:00Z
- **Completed:** 2026-03-19T08:30:53Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- Scenario 31: replaced all ConfirmedBad/confirmed bad terminology with Violated; fixed critical poll_until_log pattern from "device confirmed bad" to "all resolved violated, no commands" (exact SnapshotJob.cs log text)
- Scenario 33: rewrote staleness assertions to match quick-076 behavior — stale data now skips to tier=4 command dispatch; uses poll_until for counter-increment (same pattern as STS-02); removed old delta==0 sub-scenarios
- report.sh: extended Snapshot Evaluation range from |28|36| to |28|38| to accommodate new scenarios from plan 02

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix scenario 31 log pattern and terminology** - `cec9c6f` (fix)
2. **Task 2: Update scenario 33 to assert commands ARE sent when stale + extend report.sh** - `6710ea9` (fix)

**Plan metadata:** (docs commit follows)

## Files Created/Modified
- `tests/e2e/scenarios/31-sts-03-resolved-gate.sh` - ConfirmedBad -> Violated; poll_until_log pattern fixed
- `tests/e2e/scenarios/33-sts-05-staleness.sh` - Rewritten: stale triggers commands (was: no commands); 2 sub-scenarios instead of 3
- `tests/e2e/lib/report.sh` - Snapshot Evaluation upper bound 36 -> 38

## Decisions Made
- Scenario 33 uses `poll_until 45 5` for counter-increment assertion — mirrors STS-02 sub-scenario 30b (SNMP SET round-trip + OTel export + Prometheus scrape requires polling, not immediate snapshot)
- Old sub-scenario 33c (no suppressions while stale) removed entirely — suppression is a separate concern from the stale-dispatches behavior being tested

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Scenarios 31 and 33 now match SnapshotJob.cs post-quick-076 log text
- report.sh range covers through index 38 (plan 02 scenarios will land in 37-38 range)
- Phase 58 plan 02 can proceed to add new tier simulation scenarios

---
*Phase: 58-snapshot-tier-simulation-tests*
*Completed: 2026-03-19*
