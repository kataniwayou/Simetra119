---
phase: 64-advance-gate-logic
plan: 01
subsystem: testing
tags: [e2e, pss, bash, kubernetes, configmap, snmp, stage-gating]

# Dependency graph
requires:
  - phase: 63-two-tenant-independence
    provides: run-stage2.sh pattern and PSS Stage 1/2 scenarios (53-61)
  - phase: 62-pss-single-tenant
    provides: PSS single-tenant fixture and OID naming conventions
provides:
  - 4-tenant PSS fixture (tenant-cfg08-pss-four-tenant.yaml) with G1/G2 group structure
  - run-stage3.sh runner with 3-stage FAIL_COUNT gating
  - Extended PSS report category covering all 16 PSS scenarios (indices 52-67)
  - Cross-stage PSS summary in both run-stage3.sh and run-all.sh
affects:
  - 64-02: Stage 3 scenario scripts (62-68) use this fixture and runner as their execution context

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Stage 3 runner manages fixture lifecycle (apply/prime/restore) rather than individual scenarios
    - FAIL_COUNT gating: each stage checks accumulated count before proceeding to next
    - Cross-stage summary uses SCENARIO_RESULTS array with known 0-based index ranges per stage

key-files:
  created:
    - tests/e2e/fixtures/tenant-cfg08-pss-four-tenant.yaml
    - tests/e2e/run-stage3.sh
  modified:
    - tests/e2e/lib/report.sh
    - tests/e2e/run-all.sh

key-decisions:
  - "run-stage3.sh manages Stage 3 fixture lifecycle (not individual scenarios) -- scenarios only manipulate OIDs"
  - "3-stage gating: Stage 1 (53-58) gated before Stage 2 (59-61); Stage 2 gated before Stage 3 (62-68)"
  - "Cleanup trap flags _STAGE3_CONFIGMAP_SAVED to avoid restoring configmap if Stage 3 setup never ran"
  - "Cross-stage summary in run-all.sh uses PSS scenario indices 52-67 (offset from 0-based SCENARIO_RESULTS)"

patterns-established:
  - "Pattern: runner-managed fixture lifecycle for multi-stage scenarios (save before, restore after all stages)"
  - "Pattern: boolean flag (_STAGE3_CONFIGMAP_SAVED) guards conditional restore in cleanup trap"

# Metrics
duration: 2min
completed: 2026-03-20
---

# Phase 64 Plan 01: Advance Gate Logic Infrastructure Summary

**PSS 4-tenant fixture (G1 Priority=1 / G2 Priority=2), run-stage3.sh with 3-stage FAIL_COUNT gating over scenarios 53-68, report category extended to 52-67, and cross-stage PSS summary in both runners**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-20T12:25:13Z
- **Completed:** 2026-03-20T12:27:00Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Created tenant-cfg08-pss-four-tenant.yaml: 4 tenants, e2e-pss-g1-t1 + e2e-pss-g1-t2 (Priority=1, OIDs .999.4.x and .999.5.x) gating e2e-pss-g2-t3 + e2e-pss-g2-t4 (Priority=2, OIDs .999.6.x and .999.7.x)
- Created run-stage3.sh: 3-stage runner sourcing Stage 1 (53-58), Stage 2 (59-61), Stage 3 (62-68) with FAIL_COUNT gates between each; runner manages Stage 3 fixture apply/prime/restore
- Extended report.sh PSS category from 52|60 to 52|67, covering all 16 PSS scenarios (indices 52-67, scenarios 53-68)
- Added Cross-Stage PSS Summary to run-all.sh printing Stage 1/2/3 pass/fail counts after print_summary

## Task Commits

Each task was committed atomically:

1. **Task 1: Create 4-tenant PSS fixture and update report category** - `0f7f897` (feat)
2. **Task 2: Create run-stage3.sh with 3-stage gating and update run-all.sh** - `9ad3dce` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `tests/e2e/fixtures/tenant-cfg08-pss-four-tenant.yaml` - 4-tenant PSS ConfigMap: G1 (T1+T2, Priority=1) gating G2 (T3+T4, Priority=2), each tenant has 1 Evaluate + 2 Resolved metrics + e2e_set_bypass command
- `tests/e2e/run-stage3.sh` - Stage 3 runner: sources 53-61 (Stage 1+2), checks FAIL_COUNT gates, applies 4-tenant fixture, primes 12 OIDs, sources 62-68, restores configmap, cross-stage summary
- `tests/e2e/lib/report.sh` - PSS category extended from `52|60` to `52|67`
- `tests/e2e/run-all.sh` - Added Cross-Stage PSS Summary section after print_summary (PSS indices 52-67)

## Decisions Made

- **Runner-managed fixture lifecycle for Stage 3:** Individual Stage 3 scenarios (62-68) do not apply or restore the 4-tenant fixture. The runner handles setup before the scenario loop and teardown after. This differs from SNS scenarios (46-52) which each apply/restore their own fixture. Rationale: 7 Stage 3 scenarios share identical setup state (all 12 OIDs primed to healthy); applying per-scenario would add 7 duplicate wait-for-reload cycles.

- **Cleanup trap with boolean flag:** `_STAGE3_CONFIGMAP_SAVED=false` flag controls whether cleanup trap attempts to restore configmap. If execution exits before Stage 3 setup (Stage 1 or Stage 2 gate triggered), the flag remains false and no configmap restore runs. Rationale: prevents restoring a saved configmap that was never created.

- **3-stage gating identical to run-stage2.sh pattern:** Stage gate log message follows exact format "Stage N had $FAIL_COUNT failure(s) -- skipping Stage N+1 scenarios". Stage 1 exit generates a partial report (Stage 1 results only). Stage 2 exit generates a partial report (Stage 1+2 results). Only if all gates pass does Stage 3 execute and a full report generate.

- **Cross-stage PSS indices in run-all.sh:** run-all.sh runs all scenarios 01-68 via glob. PSS scenarios occupy SCENARIO_RESULTS indices 52-67 (0-based). Cross-stage summary computes: Stage 1 = indices 52-57, Stage 2 = indices 58-60, Stage 3 = indices 61-67. Stages with no results (ran < total in range) are skipped in output.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Infrastructure is complete: fixture, runner, report category, and summary all in place
- Plan 64-02 can now create scenario scripts 62-68 (PSS-14 through PSS-20) -- they will be sourced by run-stage3.sh without any runner modifications
- Scenarios 62-68 must NOT apply or restore the 4-tenant fixture (the runner does this)
- Scenarios should re-prime OIDs to healthy at the start of each scenario to ensure isolation (the runner only primes once at Stage 3 setup; cross-scenario state contamination is possible if scenarios don't reset)

---
*Phase: 64-advance-gate-logic*
*Completed: 2026-03-20*
