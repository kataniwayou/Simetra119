---
phase: 55-advanced-scenarios
plan: 01
subsystem: testing
tags: [e2e, snmp, prometheus, aggregate, synthetic, agg_breach, simulator, bash]

# Dependency graph
requires:
  - phase: 54-mts-scenarios
    provides: MTS-02 advance gate scenario and report.sh category range |28|34|
  - phase: 51-e2e-http-control
    provides: aiohttp HTTP simulator with SCENARIOS dict and sim_set_scenario control
provides:
  - agg_breach simulator scenario (.4.2=2, .4.3=2, .4.5=50, .4.6=50) proving aggregate sum breach
  - report.sh Snapshot Evaluation range extended to |28|36| covering ADV-01 and ADV-02 slots
  - ADV-01 aggregate evaluate scenario script (4 sub-assertions: tier=4, sent counter, source=synthetic, recovery)
affects: [phase 55 ADV-02 if it exists, run-all.sh, e2e verification pipeline]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - agg_breach scenario uses .4.2=2 and .4.3=2 to keep Resolved metrics in-range so tier-2 passes and tier-4 fires
    - Explicit if/else with record_pass/record_fail (not assert_delta_gt) for literal-count compliance
    - query_prometheus with source="synthetic" label to verify synthetic pipeline label propagation

key-files:
  created:
    - tests/e2e/scenarios/36-adv-01-aggregate-evaluate.sh
  modified:
    - simulators/e2e-sim/e2e_simulator.py
    - tests/e2e/lib/report.sh

key-decisions:
  - "agg_breach scenario sets .4.2=2 and .4.3=2 (Resolved metrics in-range) so tier-2 passes and tier-4 fires on sum(100) > Max:80"
  - "sleep 30 after tier=4 confirmation for OTel export + Prometheus scrape before source=synthetic assertion"
  - "Recovery assertion uses sim_set_scenario healthy (sum=0 < 80) and polls for tier=3 log"

patterns-established:
  - "ADV scenario pattern: setup fixture -> breach phase -> 4 sub-assertions (log, counter, Prometheus label, recovery) -> cleanup"

# Metrics
duration: 2min
completed: 2026-03-17
---

# Phase 55 Plan 01: Advanced Scenarios — ADV-01 Aggregate Evaluate Summary

**agg_breach simulator scenario + ADV-01 script asserting synthetic pipeline tier=4, sent counter, source=synthetic label, and recovery tier=3 for e2e-tenant-agg**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-17T14:36:16Z
- **Completed:** 2026-03-17T14:38:01Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- Added `agg_breach` scenario to simulator SCENARIOS dict: `.4.5=50 + .4.6=50 = sum 100 > Max:80`, Resolved metrics `.4.2=2, .4.3=2` in-range so tier-2 passes and tier-4 fires
- Extended report.sh Snapshot Evaluation category range from `|28|34|` to `|28|36|` to cover ADV-01 (index 35) and ADV-02 (index 36) slots
- Created ADV-01 scenario script with 4 sub-assertions: tier=4 log for e2e-tenant-agg, sent counter increment, source=synthetic in Prometheus, and recovery tier=3 after healthy switch

## Task Commits

Each task was committed atomically:

1. **Task 1: Add agg_breach simulator scenario and extend report.sh** - `1fd0ba1` (feat)
2. **Task 2: Create ADV-01 aggregate evaluate scenario script** - `c413de4` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `simulators/e2e-sim/e2e_simulator.py` - Added `agg_breach` scenario entry to SCENARIOS dict
- `tests/e2e/lib/report.sh` - Extended Snapshot Evaluation range from |28|34| to |28|36|
- `tests/e2e/scenarios/36-adv-01-aggregate-evaluate.sh` - New ADV-01 scenario: 4 sub-assertions covering tier=4, sent counter, source=synthetic, and recovery

## Decisions Made

- **agg_breach sets .4.2=2 and .4.3=2 explicitly:** If left at 0 (default), Resolved metrics are violated, tier-2 fires ConfirmedBad instead of reaching tier-4. Must be in-range.
- **sleep 30 before source=synthetic assertion:** OTel export and Prometheus scrape require time after tier=4 fires. 30s is sufficient given 15s scrape interval plus export latency.
- **Recovery uses sim_set_scenario healthy:** The healthy scenario zeroes .4.5 and .4.6 (sum=0 < 80, in-range), with .4.2=2 and .4.3=2 already in-range, producing a tier=3 healthy result.
- **Explicit if/else not assert_delta_gt for sub-scenario 36b:** Maintains literal record_pass/record_fail count of 8 as required by plan verification criteria.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- ADV-01 script is complete and follows identical setup/cleanup pattern as MTS-02
- report.sh range already covers ADV-02 index slot (36) if a second advanced scenario is added
- Phase 55 plan 01 complete; no further blockers for run-all.sh execution

---
*Phase: 55-advanced-scenarios*
*Completed: 2026-03-17*
