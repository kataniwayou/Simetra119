---
phase: 66-pipeline-event-counters
plan: 03
subsystem: testing
tags: [bash, e2e, assertions, pipeline-counters, rejected, errors, snmp_event]

# Dependency graph
requires:
  - phase: 66-pipeline-event-counters
    plan: 01
    provides: assert_delta_eq helper in common.sh for exact equality counter assertions
affects:
  - Phase 67+ (any future phase adding E2E scenarios)
  - Report generation via report.sh Pipeline Counter Verification category (68-75 range)

provides:
  - MCV-05 (scenario 73): documents and verifies rejected stays 0 during normal operation
  - MCV-06 (scenario 74): verifies rejected stays 0 specifically while mapped OIDs are handled
  - MCV-07 (scenario 75): verifies errors stays 0 during normal E2E run

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Activity-gated zero assertion: if PUB_DELTA > 0 then assert_delta_eq REJ/ERR 0 else record_fail pipeline-inactive"
    - "Two-metric corroboration: snapshot both the safety-net counter and an activity counter; prove pipeline is live before asserting zero delta"

key-files:
  created:
    - tests/e2e/scenarios/73-mcv05-rejected-unmapped-behavior.sh
    - tests/e2e/scenarios/74-mcv06-rejected-stays-zero-mapped.sh
    - tests/e2e/scenarios/75-mcv07-errors-stays-zero.sh
  modified: []

key-decisions:
  - "MCV-05 uses published (not handled) as the activity proof because published is the first counter to move in the pipeline"
  - "MCV-06 uses handled as the activity proof to specifically demonstrate mapped-OID flow; complements MCV-05 which uses published"
  - "All three scenarios use an activity-gate (if delta > 0) rather than unconditional assert to produce clear failure messages when pipeline is idle"

patterns-established:
  - "Activity-gated zero assertion pattern: snapshot two counters, wait for activity counter to move, then assert safety-net counter delta == 0"

# Metrics
duration: 2min
completed: 2026-03-22
---

# Phase 66 Plan 03: Pipeline Event Counters — Safety-Net Counter Verification (MCV-05 through MCV-07) Summary

**Three E2E scenarios verifying that snmp.event.rejected fires only on ValidationBehavior failures (not unmapped OIDs) and that both rejected and errors stay at zero throughout a normal E2E run**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-22T15:02:15Z
- **Completed:** 2026-03-22T15:03:34Z
- **Tasks:** 2
- **Files created:** 3

## Accomplishments

- Created scenario 73 (MCV-05) documenting the key research finding: `snmp.event.rejected` fires ONLY on ValidationBehavior failures (malformed OID regex or null DeviceName), NOT for unmapped OIDs. Unmapped OIDs resolve to MetricName="Unknown" and flow through normally. Verifies rejected stays 0 while published moves.
- Created scenario 74 (MCV-06) as the complementary proof: specifically uses the `handled` counter (not published) as the activity gate, proving rejected stays 0 while mapped OIDs are actively being processed by OtelMetricHandler.
- Created scenario 75 (MCV-07) verifying `snmp.event.errors` stays 0 during normal operation, confirming no unhandled exceptions are caught by ExceptionBehavior.

## Task Commits

Each task was committed atomically:

1. **Task 1: Create scenario 73 (MCV-05 rejected behavior for unmapped OIDs)** - `5e59e75` (feat)
2. **Task 2: Create scenarios 74-75 (MCV-06 rejected stays 0 mapped, MCV-07 errors stays 0)** - `99b9d29` (feat)

**Plan metadata:** (see final docs commit)

## Files Created/Modified

- `tests/e2e/scenarios/73-mcv05-rejected-unmapped-behavior.sh` - MCV-05: documents ValidationBehavior-only rejection finding, asserts rejected==0 while published is active
- `tests/e2e/scenarios/74-mcv06-rejected-stays-zero-mapped.sh` - MCV-06: asserts rejected==0 while handled is active (mapped OID flow proof)
- `tests/e2e/scenarios/75-mcv07-errors-stays-zero.sh` - MCV-07: asserts errors==0 while published is active (no pipeline exceptions)

## Decisions Made

- MCV-05 uses `published` as the activity gate (not `handled`) because published is the first counter to move after a poll cycle enters the pipeline. This makes the scenario sensitive to any pipeline activity.
- MCV-06 uses `handled` as the activity gate specifically to demonstrate that mapped OIDs are flowing all the way through to OtelMetricHandler, which is the direct complement to the rejected check.
- All three scenarios use an explicit activity-gate pattern (`if PUB/HDL_DELTA > 0 ... else record_fail pipeline-inactive`) rather than an unconditional `assert_delta_eq`, providing clearer failure messages when the pipeline is idle vs. when the counter unexpectedly moves.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Scenarios 73-75 complete the Phase 66 pipeline event counter verification suite (MCV-01 through MCV-07, with MCV-01 through MCV-04 provided by plan 66-02 when executed)
- All scenarios follow the activity-gated zero assertion pattern and are ready for the E2E runner
- No blockers for subsequent phases

---
*Phase: 66-pipeline-event-counters*
*Completed: 2026-03-22*
