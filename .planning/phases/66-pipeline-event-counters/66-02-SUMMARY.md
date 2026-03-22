---
phase: 66-pipeline-event-counters
plan: 02
subsystem: testing
tags: [bash, e2e, scenarios, pipeline-counters, snmp-event-published, snmp-event-handled, snmp-event-rejected, assert_delta_ge, assert_delta_eq]

# Dependency graph
requires:
  - phase: 66-01
    provides: assert_delta_eq and assert_delta_ge helpers in common.sh, Pipeline Counter Verification report category
provides:
  - Scenario 69 (MCV-01): assert_delta_ge verifying snmp_event_published_total increments by at least 9 per E2E-SIM poll cycle
  - Scenario 70 (MCV-02): correlates snmp_trap_received_total delta with snmp_event_published_total increment for trap path
  - Scenario 71 (MCV-03): assert_delta_eq verifying handled delta equals published delta for all-mapped E2E-SIM OIDs
  - Scenario 72 (MCV-04): asserts handled never exceeds published, proving rejected OIDs excluded from handled counter
affects:
  - 66-03 (remaining pipeline counter scenarios 73-75)
  - Any future phase adding E2E scenarios in MCV category

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "MCV scenario pattern: snapshot_before, poll_until for activity, sleep for OTel export flush, snapshot_after, compute delta, assert"
    - "Multi-counter correlation: snapshot multiple counters at same before/after timestamps, compute independent deltas, compare"

key-files:
  created:
    - tests/e2e/scenarios/69-mcv01-poll-published-exact.sh
    - tests/e2e/scenarios/70-mcv02-trap-published.sh
    - tests/e2e/scenarios/71-mcv03-handled-equals-published.sh
    - tests/e2e/scenarios/72-mcv04-handled-not-for-rejected.sh
  modified: []

key-decisions:
  - "Scenario 69 minimum threshold is 9 (E2E-SIM group 2 OID count) not total OID count, to be achievable even if only one replica completes one cycle before OTel export"
  - "Scenario 70 falls back to record_fail if no trap arrives in 60s window, rather than marking as flaky pass"
  - "Scenario 72 uses inline if/elif/fi comparison rather than assert_delta_ge to produce distinct fail messages for zero-published vs handled-exceeds-published cases"

patterns-established:
  - "OTel export flush: poll_until for counter to move, then sleep 20 for second counter to catch up before snapshot"
  - "Guard pattern for timer-dependent scenarios: check trap_delta > 0 before asserting correlation"

# Metrics
duration: 2min
completed: 2026-03-22
---

# Phase 66 Plan 02: Pipeline Event Counters — MCV-01 through MCV-04 E2E Scenarios Summary

**Four MCV E2E scenarios replacing weak assert_delta_gt 0 tests with precise delta assertions: poll published count (>=9), trap-to-published correlation, handled/published exact parity, and handled-never-exceeds-published proof**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-22T15:01:41Z
- **Completed:** 2026-03-22T15:03:05Z
- **Tasks:** 2
- **Files modified:** 4 (all created)

## Accomplishments

- Created scenario 69 (MCV-01): uses `assert_delta_ge` with minimum 9 to verify `snmp_event_published_total` increments by at least one E2E-SIM group 2 poll cycle worth of OIDs
- Created scenario 70 (MCV-02): snapshots both `snmp_trap_received_total` and `snmp_event_published_total`, guards on trap_delta > 0 before asserting published increment >= trap_delta
- Created scenario 71 (MCV-03): uses `assert_delta_eq` to verify published and handled deltas are exactly equal for E2E-SIM (all OIDs mapped, no rejects expected)
- Created scenario 72 (MCV-04): tracks `snmp_event_rejected_total` alongside published/handled, proves handled never exceeds published with distinct fail messages

## Task Commits

Each task was committed atomically:

1. **Task 1: Create scenarios 69-70 (MCV-01 poll published, MCV-02 trap published)** - `a0402c6` (feat)
2. **Task 2: Create scenarios 71-72 (MCV-03 handled parity, MCV-04 handled-not-for-rejected)** - `76815af` (feat)

**Plan metadata:** (see final docs commit)

## Files Created/Modified

- `tests/e2e/scenarios/69-mcv01-poll-published-exact.sh` - MCV-01: assert_delta_ge snmp_event_published_total >= 9 after E2E-SIM poll cycle
- `tests/e2e/scenarios/70-mcv02-trap-published.sh` - MCV-02: correlate trap_received delta with published increment, guard on trap arrival
- `tests/e2e/scenarios/71-mcv03-handled-equals-published.sh` - MCV-03: assert_delta_eq published == handled for all-mapped E2E-SIM OIDs
- `tests/e2e/scenarios/72-mcv04-handled-not-for-rejected.sh` - MCV-04: assert handled <= published with rejected tracking

## Decisions Made

- Scenario 69 uses minimum threshold 9 (E2E-SIM group 2 OID count) rather than total OID count across all groups, so the assertion passes even when only one replica completes a single poll cycle before the OTel export window closes.
- Scenario 70 uses `record_fail` when no trap arrives in the 60s window rather than skipping, because a missed trap indicates an infrastructure problem worth surfacing.
- Scenario 72 uses inline conditional logic rather than `assert_delta_ge`/`assert_delta_eq` to produce two distinct failure messages: "No published events observed" vs "handled > published -- unexpected", making triage easier.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Scenarios 69-72 are ready for E2E runner execution; they inherit all lib functions via `run-all.sh` sourcing
- Report category "Pipeline Counter Verification" (range 68-75) set up in plan 66-01 will automatically include these scenario results
- No blockers for Phase 66 plan 03 (remaining MCV scenarios 73-75)

---
*Phase: 66-pipeline-event-counters*
*Completed: 2026-03-22*
