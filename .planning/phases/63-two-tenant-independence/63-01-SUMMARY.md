---
phase: 63-two-tenant-independence
plan: 01
subsystem: testing
tags: [e2e, bash, k8s, configmap, snmp, pss, stage-gating, two-tenant]

# Dependency graph
requires:
  - phase: 62-single-tenant-evaluation-states
    provides: PSS Stage 1 scenarios (53-58), sim.sh OID control, report category infrastructure
provides:
  - Two-tenant PSS fixture with independent OID namespaces (T2 for t1, T3 for t2)
  - Extended Progressive Snapshot Suite report category covering scenarios 53-61 (indices 52-60)
  - Stage 2 runner (run-stage2.sh) with PSS-INF-01 FAIL_COUNT gate between Stage 1 and Stage 2
affects: [63-02-scenarios, run-stage2-usage]

# Tech tracking
tech-stack:
  added: []
  patterns: [stage-gating via FAIL_COUNT global, two-tenant independent OID namespace fixture, explicit scenario sourcing in gated runner]

key-files:
  created:
    - tests/e2e/fixtures/tenant-cfg07-pss-two-tenant.yaml
    - tests/e2e/run-stage2.sh
  modified:
    - tests/e2e/lib/report.sh

key-decisions:
  - "run-stage2.sh is PSS-only (scenarios 53-61) -- not a full suite runner; run-all.sh handles 1-N unconditionally"
  - "Stage gate checks FAIL_COUNT directly (not a delta) since Stage 2 runner only sources PSS scenarios before the gate"
  - "Explicit scenario list in runner (not glob) for clarity and to avoid picking up non-PSS files"

patterns-established:
  - "Stage-gated runner: source Stage 1 scenarios, check FAIL_COUNT, conditionally source Stage 2"
  - "Two-tenant fixture: independent OID suffixes per tenant (T2 .999.5.x for t1, T3 .999.6.x for t2)"
  - "Report extension: add scenarios by widening end_index in _REPORT_CATEGORIES"

# Metrics
duration: 2min
completed: 2026-03-20
---

# Phase 63 Plan 01: Two-Tenant Infrastructure Summary

**Two-tenant K8s fixture with independent T2/T3 OID namespaces, extended PSS report category to 52-60, and stage-gated run-stage2.sh that checks FAIL_COUNT after scenarios 53-58 before running 59-61**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-20T11:14:20Z
- **Completed:** 2026-03-20T11:16:38Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- Created `tenant-cfg07-pss-two-tenant.yaml` with e2e-pss-t1 (T2 OIDs: e2e_eval_T2, e2e_res1_T2, e2e_res2_T2) and e2e-pss-t2 (T3 OIDs: e2e_eval_T3, e2e_res1_T3, e2e_res2_T3), both at Priority=1 with 6s grace window
- Extended `report.sh` Progressive Snapshot Suite category from `52|57` to `52|60` to cover the 9 PSS scenarios (53-61)
- Created `run-stage2.sh` implementing PSS-INF-01: sources Stage 1 (53-58), gates on FAIL_COUNT, conditionally sources Stage 2 (59-61), generates PSS-specific report

## Task Commits

Each task was committed atomically:

1. **Task 1: Create 2-tenant PSS fixture and update report category** - `1592630` (feat)
2. **Task 2: Create Stage 2 runner script with FAIL_COUNT gating** - `8ad7b52` (feat)

## Files Created/Modified
- `tests/e2e/fixtures/tenant-cfg07-pss-two-tenant.yaml` - Two-tenant ConfigMap with independent OID namespaces per tenant
- `tests/e2e/lib/report.sh` - Extended PSS category end_index from 57 to 60 (one line change)
- `tests/e2e/run-stage2.sh` - Gated PSS runner: Stage 1 (53-58) -> FAIL_COUNT gate -> Stage 2 (59-61)

## Decisions Made
- run-stage2.sh runs PSS scenarios only (53-61), not the full suite -- avoids entangling non-PSS failures with PSS stage gating; run-all.sh remains the unconditional full suite runner
- Stage gate checks raw FAIL_COUNT (not a delta from Stage 1 start) since the runner only sources PSS scenarios before the gate -- no prior-scenario contamination possible
- Explicit scenario list used in runner instead of glob -- clearer intent, no risk of picking up unexpected files

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Infrastructure complete: fixture, report category, and stage runner all ready
- Phase 63 Plan 02 can now create scenarios 59-61 (PSS-11, PSS-12, PSS-13) using tenant-cfg07-pss-two-tenant.yaml
- run-stage2.sh will source the scenario files as soon as they exist in scenarios/

---
*Phase: 63-two-tenant-independence*
*Completed: 2026-03-20*
