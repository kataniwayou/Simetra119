---
phase: 63-two-tenant-independence
plan: 02
subsystem: testing
tags: [e2e, pss, two-tenant, independence, tier4, tier3, tier2, snmp, prometheus, bash]

# Dependency graph
requires:
  - phase: 63-01
    provides: tenant-cfg07-pss-two-tenant.yaml fixture, run-stage2.sh runner, report extension
  - phase: 62-01
    provides: single-tenant PSS scenarios 53-58 as structural templates
provides:
  - Three two-tenant independence scenario scripts (scenarios 59-61, PSS-11/12/13)
  - PSS-11: T1=Healthy (tier=3) while T2=Unresolved (tier=4) -- T2 eval violated, T1 untouched
  - PSS-12: T1=Resolved (tier=2) while T2=Healthy (tier=3) -- T1 both resolved violated, T2 untouched
  - PSS-13: Both T1 and T2 reach tier=4 simultaneously, counter delta >= 2 proves independent dispatch
affects: [64-phase-completion, run-stage2.sh coverage of scenarios 59-61]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Two-tenant OID priming: prime ALL 6 OIDs (5.1-5.3 T1, 6.1-6.3 T2) before any stimulus"
    - "Independence proof via log grep: e2e-pss-t1.*tier=X and e2e-pss-t2.*tier=Y are independent assertions"
    - "Counter delta >= 2 pattern: shared device_name label with minimum delta proves multi-tenant dispatch"
    - "No negative counter assertion for T1 in PSS-11: T1 tier=3 log IS the proof (shared counter unsuitable)"

key-files:
  created:
    - tests/e2e/scenarios/59-pss-11-t1-healthy-t2-unresolved.sh
    - tests/e2e/scenarios/60-pss-12-t1-resolved-t2-healthy.sh
    - tests/e2e/scenarios/61-pss-13-both-unresolved.sh
  modified: []

key-decisions:
  - "PSS-11 does not assert T1 counter delta=0 -- snmp_command_dispatched_total is shared by device_name; T1 tier=3 log is the independence proof"
  - "PSS-13 uses delta >= 2 (not delta > 0) to distinguish both-tenant dispatch from single-tenant dispatch"
  - "All two-tenant scenarios prime all 6 OIDs before stimulus to ensure readiness grace passes for both tenants"

patterns-established:
  - "Two-tenant scenario structure: prime 6 OIDs, sleep 8s, stimulus only target OIDs, assert both tenant tier logs"
  - "em-dash/double-dash alternation in tier=4 grep covers both log format variants"

# Metrics
duration: 2min
completed: 2026-03-20
---

# Phase 63 Plan 02: Two-Tenant Independence Scenarios Summary

**Three bash E2E scenarios proving per-tenant tier evaluation independence: PSS-11 (T1 healthy/T2 unresolved), PSS-12 (T1 resolved/T2 healthy), PSS-13 (both unresolved, counter delta >= 2)**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-20T11:19:11Z
- **Completed:** 2026-03-20T11:21:42Z
- **Tasks:** 2
- **Files created:** 3

## Accomplishments

- Created PSS-11 scenario: T2 eval violated while T1 untouched proves T1 remains healthy (tier=3) and T2 fires unresolved (tier=4) independently
- Created PSS-12 scenario: T1 both resolved OIDs violated while T2 untouched proves T1 reaches resolved (tier=2) and T2 remains healthy (tier=3) independently
- Created PSS-13 scenario: Both tenants eval violated simultaneously, logs confirm both at tier=4, counter delta >= 2 proves both dispatched commands
- All 3 scripts prime all 6 OIDs (T1: 5.1-5.3, T2: 6.1-6.3) before any stimulus, ensuring readiness grace passes for both tenants
- Total of 15 record_pass/record_fail assertions across 3 scenarios

## Task Commits

Each task was committed atomically:

1. **Task 1: Create PSS scenarios 59-60** - `ab82dd5` (feat)
2. **Task 2: Create PSS scenario 61** - `5b9fe9b` (feat)

**Plan metadata:** `[pending]` (docs: complete plan)

## Files Created/Modified

- `tests/e2e/scenarios/59-pss-11-t1-healthy-t2-unresolved.sh` - PSS-11: T1 healthy tier=3 while T2 unresolved tier=4
- `tests/e2e/scenarios/60-pss-12-t1-resolved-t2-healthy.sh` - PSS-12: T1 resolved tier=2 while T2 healthy tier=3
- `tests/e2e/scenarios/61-pss-13-both-unresolved.sh` - PSS-13: Both tenants unresolved tier=4 with command counter delta >= 2

## Decisions Made

- **No counter negative assertion in PSS-11 for T1:** The snmp_command_dispatched_total counter uses device_name="E2E-SIM" as a shared label across all tenants. A T1 negative assertion would be unreliable because T2 commands increment the same counter. T1 tier=3 log assertion is the correct independence proof.
- **PSS-13 uses delta >= 2:** Delta > 0 would pass if only one tenant dispatched. Delta >= 2 is the minimum proof that both tenants contributed at least one command dispatch each.
- **All 6 OIDs primed in all scenarios:** Both T1 and T2 must pass their own readiness grace before stimulus. Priming only T1 OIDs would leave T2 in an indeterminate state during the grace window.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 63 complete: 2-tenant fixture (63-01), independence scenario scripts (63-02)
- run-stage2.sh already references scenarios 59-61 explicitly (from 63-01)
- Ready for Phase 64 (final completion/cleanup if applicable)
- All PSS scenarios 53-61 exist and are covered by run-stage2.sh

---
*Phase: 63-two-tenant-independence*
*Completed: 2026-03-20*
