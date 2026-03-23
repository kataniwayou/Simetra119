---
phase: 81-e2e-partial-percentage-scenario
plan: 01
subsystem: testing
tags: [e2e, bash, prometheus, percentage-gauges, tenant-metrics, partial-violation]

# Dependency graph
requires:
  - phase: 80-e2e-v25-percentage-gauge-migration
    provides: TVM scenarios 107-112 updated for v2.5 percentage gauges
  - phase: 77-tenant-metric-service
    provides: EvaluateTenant single-exit-point percentage gauge recording
provides:
  - E2E scenario 113 asserting exactly 50% intermediate percentage values
  - Multi-holder tenant fixture enabling partial violation testing
  - Report category covering all TVM scenarios 107-113
affects: [run-all.sh, e2e report generation]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - awk int($1+0.5) == N for exact float-to-integer percentage comparison in bash
    - 4-holder fixture pattern for partial violation coverage

key-files:
  created:
    - tests/e2e/fixtures/tenant-cfg11-pss-partial.yaml
    - tests/e2e/scenarios/113-tvm07-partial-percent.sh
  modified:
    - tests/e2e/lib/report.sh

key-decisions:
  - "awk '{exit (int($1+0.5) == 50) ? 0 : 1}' for integer round-to-nearest comparison handles 50.0 float representation"
  - "TVM-07E asserts gauge series presence (not value > 0) for dispatched_percent, matching TVM-05B pattern for fast-cycling tenants"
  - "Fixture uses e2e_eval_T3 (6.1) and e2e_res1_T3 (6.2) as second holders — both already in OID map"

patterns-established:
  - "Multi-holder partial violation: violate 1 of N holders to produce N% intermediate percentage values"
  - "awk round-compare: int($1+0.5) == N for exact percentage assertion on Prometheus float values"

# Metrics
duration: 2min
completed: 2026-03-23
---

# Phase 81 Plan 01: E2E Partial Percentage Scenario Summary

**Scenario 113 (TVM-07) added: asserts evaluate_percent==50 and resolved_percent==50 via 4-holder fixture with partial violation (1 of 2 holders each)**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-23T20:38:22Z
- **Completed:** 2026-03-23T20:40:10Z
- **Tasks:** 2/2
- **Files modified:** 3

## Accomplishments

- Created `tenant-cfg11-pss-partial.yaml` fixture with e2e-pss-partial tenant: 2 Evaluate + 2 Resolved metric holders using T2/T3 OID families (all entries confirmed in OID map)
- Created scenario 113 (`113-tvm07-partial-percent.sh`) with 5 TVM-07A-E sub-assertions; TVM-07B and TVM-07C assert exactly 50% using awk integer-round comparison
- Updated report.sh Tenant Metric Validation category end index from 111 to 112, covering scenarios 107-113

## Task Commits

Each task was committed atomically:

1. **Task 1: Create multi-holder tenant fixture and scenario 113** - `5d384e4` (feat)
2. **Task 2: Update report.sh category to include scenario 113** - `6443ecf` (feat)

## Files Created/Modified

- `tests/e2e/fixtures/tenant-cfg11-pss-partial.yaml` - e2e-pss-partial tenant with 4 metric holders (e2e_eval_T2, e2e_eval_T3 as Evaluate; e2e_res1_T2, e2e_res1_T3 as Resolved)
- `tests/e2e/scenarios/113-tvm07-partial-percent.sh` - TVM-07 scenario: prime 4 OIDs, violate 1 eval + 1 resolved, assert state=3, evaluate_percent=50, resolved_percent=50, duration delta>0, dispatched_percent present
- `tests/e2e/lib/report.sh` - Tenant Metric Validation category end index: 111 -> 112

## Decisions Made

- Used `awk '{exit (int($1+0.5) == 50) ? 0 : 1}'` for exact 50% comparison — handles Prometheus returning "50.0" float string while comparing as integer
- TVM-07E asserts `dispatched_percent` gauge series presence (not `> 0`) matching TVM-05B pattern — fast-cycling tenants may overwrite gauge before Prometheus scrapes; state=3 (TVM-07A) already proves dispatch ran
- Used OID suffixes 5.1/6.1 (eval1/eval2) and 5.2/6.2 (res1/res2) from T2/T3 families — confirmed present in OID map as e2e_eval_T2, e2e_eval_T3, e2e_res1_T2, e2e_res1_T3

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 81 complete: all 7 TVM scenarios (107-113) now cover full percentage gauge coverage: smoke (107), NotReady (108), Resolved (109), Healthy/0% (110), Unresolved/100% (111), unresolved-with-resolved (112), and partial 50% (113)
- v2.5 E2E migration fully complete across all phases (80-81)
- No blockers.

---
*Phase: 81-e2e-partial-percentage-scenario*
*Completed: 2026-03-23*
