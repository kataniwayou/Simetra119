---
phase: 54-multi-tenant-scenarios
plan: "01"
subsystem: testing
tags: [bash, e2e, multi-tenant, SnapshotJob, prometheus, kubernetes]

requires:
  - phase: 53-snapshot-evaluation-scenarios
    provides: Phase 53 script patterns (save/apply/reload/scenario/baseline/assert/cleanup), poll_until_log, assert_delta_gt, all lib functions

provides:
  - tenant-cfg03-two-diff-prio-mts.yaml fixture with P1 SuppressionWindowSeconds=30 for MTS-02B gate-pass timing
  - MTS-01 scenario script validating same-priority independence via per-tenant tier=4 logs and delta>=2 counter
  - report.sh Snapshot Evaluation range extended to 28|34 covering MTS-01 (index 33) and MTS-02 (index 34)

affects:
  - 54-multi-tenant-scenarios plan 02 (MTS-02 advance gate — uses tenant-cfg03-two-diff-prio-mts.yaml)
  - 55-e2e-final-pass (report.sh range now covers all MTS scenarios)

tech-stack:
  added: []
  patterns:
    - "Per-tenant log polling: two separate poll_until_log calls (one per tenant) for multi-tenant independence assertions"
    - "Suppression-window fixture variant: cfg03-mts with P1 at 30s to enable gate-pass at T=15s cycle"

key-files:
  created:
    - tests/e2e/fixtures/tenant-cfg03-two-diff-prio-mts.yaml
    - tests/e2e/scenarios/34-mts-01-same-priority.sh
  modified:
    - tests/e2e/lib/report.sh

key-decisions:
  - "tenant-cfg03-two-diff-prio-mts.yaml uses P1 SuppressionWindowSeconds=30 so suppression fires at T=15s cycle allowing gate to pass for P2 evaluation in MTS-02B"
  - "report.sh Snapshot Evaluation upper bound changed from 32 to 34 to cover MTS-01 (33) and MTS-02 (34)"

patterns-established:
  - "Multi-tenant tier log: poll_until_log for each tenant separately with distinct patterns (e2e-tenant-A.*tier=4, e2e-tenant-B.*tier=4)"
  - "Same-priority independence proof: both tenant IDs appear in LogInformation tier=4 lines + aggregate sent delta >= 2"

duration: 2min
completed: 2026-03-17
---

# Phase 54 Plan 01: MTS Fixture, Report Range, and MTS-01 Same-Priority Scenario Summary

**MTS fixture with P1 SuppressionWindowSeconds=30, report.sh extended to 28|34, and MTS-01 proving same-priority tenant independence via per-tenant tier=4 log polling and aggregate sent counter delta>=2**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-17T14:02:01Z
- **Completed:** 2026-03-17T14:03:52Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- Created `tenant-cfg03-two-diff-prio-mts.yaml`: identical to cfg03 except P1 has SuppressionWindowSeconds=30, enabling MTS-02B gate-pass timing (P1 suppressed at T=15s → ConfirmedBad → gate passes → P2 commanded)
- Extended `report.sh` Snapshot Evaluation category upper bound from 32 to 34, adding coverage for MTS-01 (index 33) and MTS-02 (index 34)
- Wrote `34-mts-01-same-priority.sh` following Phase 53 structure: applies tenant-cfg02 (two P1 tenants), sets command_trigger scenario, polls for e2e-tenant-A and e2e-tenant-B tier=4 logs independently, asserts aggregate sent delta > 1 (>=2)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create MTS fixture and extend report.sh** - `0c5f48f` (feat)
2. **Task 2: Write MTS-01 same-priority independence scenario** - `9b71e07` (feat)

**Plan metadata:** see docs commit below

## Files Created/Modified

- `tests/e2e/fixtures/tenant-cfg03-two-diff-prio-mts.yaml` - P1 (Priority 1, SuppressionWindowSeconds=30) + P2 (Priority 2, SuppressionWindowSeconds=10); enables MTS-02B gate-pass scenario
- `tests/e2e/lib/report.sh` - Snapshot Evaluation category range extended from 28|32 to 28|34
- `tests/e2e/scenarios/34-mts-01-same-priority.sh` - MTS-01 scenario: two P1 tenants independently reach tier=4 with per-tenant log assertions and aggregate counter delta>=2

## Decisions Made

- **tenant-cfg03-two-diff-prio-mts.yaml P1 SuppressionWindowSeconds=30:** Stock cfg03 has P1 at 10s, which is less than the 15s SnapshotJob interval. This means P1's window expires before the next cycle, so P1 always sends a fresh command (Commanded) and always blocks the advance gate — P2 is never evaluated. Setting P1 to 30s ensures P1 is suppressed at the T=15s cycle (30s > 15s), returns ConfirmedBad (enqueueCount=0), gate passes, P2 gets evaluated and commanded.
- **report.sh range 28|34:** MTS-01 is index 33 and MTS-02 is index 34 (0-based). The old upper bound of 32 excluded both. Extended to 34 to cover the full Phase 54 scenario range.

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- `tenant-cfg03-two-diff-prio-mts.yaml` is ready for MTS-02 advance gate scenario (plan 54-02)
- `report.sh` now covers all Phase 54 scenario indices (33-34)
- MTS-01 scenario script is complete and syntax-valid; ready for cluster execution
- No blockers for plan 54-02

---
*Phase: 54-multi-tenant-scenarios*
*Completed: 2026-03-17*
