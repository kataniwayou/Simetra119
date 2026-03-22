---
phase: 65-e2e-runner-fixes
plan: 01
subsystem: testing
tags: [bash, e2e, shellcheck, pss, flaky-tests, report-generation]

# Dependency graph
requires:
  - phase: 64-advance-gate-logic
    provides: Stage 3 scenarios (PSS-18, PSS-19) and run-stage3.sh runner
  - phase: 63-pss-stage-runners
    provides: run-stage2.sh and run-stage3.sh runners
provides:
  - Corrected Stage 1 scenario filenames in run-stage2.sh (all 3 stale names fixed)
  - OID cleanup trap in run-stage2.sh (reset_oid_overrides on exit)
  - Stabilized PSS-18c/19c log-absence assertions (--since=10s matches 10s sleep)
  - Runner-specific _REPORT_CATEGORIES override in both standalone runners
affects: [v2.2-milestone, run-stage2.sh consumers, run-stage3.sh consumers]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Standalone runner overrides _REPORT_CATEGORIES before generate_report so indices start at 0"
    - "--since window in kubectl logs must exactly match observation sleep to avoid pre-window log bleed"

key-files:
  created: []
  modified:
    - tests/e2e/run-stage2.sh
    - tests/e2e/scenarios/66-pss-18-g1-resolved-unresolved.sh
    - tests/e2e/scenarios/67-pss-19-g1-healthy-unresolved.sh
    - tests/e2e/run-stage3.sh

key-decisions:
  - "--since must exactly match sleep duration: 12s > 10s sleep created 2s overlap allowing prior-cycle G2 logs to bleed into log-absence window"
  - "Runner overrides _REPORT_CATEGORIES locally rather than passing it as argument -- generate_report reads global at call time, so pre-call assignment is sufficient"

patterns-established:
  - "Pattern: standalone runners set runner-specific _REPORT_CATEGORIES immediately before every generate_report call site"

# Metrics
duration: 2min
completed: 2026-03-22
---

# Phase 65 Plan 01: E2E Runner Fixes Summary

**Fixed 4 tech debt items: stale Stage 1 filenames in run-stage2.sh, missing OID cleanup trap, flaky PSS-18c/19c --since window overlap, and broken PSS report category rendering in standalone runners**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-22T10:18:14Z
- **Completed:** 2026-03-22T10:20:12Z
- **Tasks:** 2 of 2
- **Files modified:** 4

## Accomplishments

- Corrected all 3 stale Stage 1 scenario filenames in run-stage2.sh (53-pss-01-not-ready.sh, 54-pss-02-stale-to-commands.sh, 55-pss-03-resolved.sh)
- Added `reset_oid_overrides || true` to run-stage2.sh cleanup trap so OID state is always cleared on exit
- Fixed PSS-18c and PSS-19c `--since=10s` (was `--since=12s`) so the log-absence window exactly matches the 10s observation sleep, eliminating the 2s overlap that caused pre-observation-window G2 logs to bleed in as false positives
- Added runner-specific `_REPORT_CATEGORIES` overrides before all generate_report call sites (2 in run-stage2.sh, 3 in run-stage3.sh) so standalone runners render PSS Stage categories correctly when SCENARIO_RESULTS indices start at 0

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix run-stage2.sh filenames and add cleanup trap** - `d00bbc1` (fix)
2. **Task 2: Fix flaky PSS-18c/19c assertions and standalone report category** - `8aefce0` (fix)

**Plan metadata:** (see final commit)

## Files Created/Modified

- `tests/e2e/run-stage2.sh` - Fixed 3 stale filenames, added reset_oid_overrides to cleanup, added 2 _REPORT_CATEGORIES overrides
- `tests/e2e/scenarios/66-pss-18-g1-resolved-unresolved.sh` - Changed --since=12s to --since=10s, updated record_pass message and header comment
- `tests/e2e/scenarios/67-pss-19-g1-healthy-unresolved.sh` - Changed --since=12s to --since=10s, updated record_pass message and header comment
- `tests/e2e/run-stage3.sh` - Added 3 _REPORT_CATEGORIES overrides (one per generate_report call site)

## Decisions Made

- `--since` must exactly match the observation `sleep` duration: `--since=12s` with `sleep 10` created a 2-second overlap allowing prior-cycle G2 logs produced before the gate-block was established to appear inside the query window, causing intermittent false-positive failures.
- Runner overrides `_REPORT_CATEGORIES` as a local assignment immediately before each `generate_report` call rather than passing it as a function argument. `generate_report` reads the global at call time, so pre-call assignment is the correct and minimal approach.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All 4 tech debt items from v2.2-MILESTONE-AUDIT.md are closed
- run-stage2.sh and run-stage3.sh are ready for production use with correct filenames, cleanup semantics, and correct standalone report rendering
- PSS-18 and PSS-19 scenarios no longer have flaky log-absence assertions

---
*Phase: 65-e2e-runner-fixes*
*Completed: 2026-03-22*
