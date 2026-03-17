---
phase: 53-single-tenant-scenarios
plan: "02"
subsystem: testing
tags: [e2e, bash, snapshot-job, tier-evaluation, command-dispatch, prometheus, kubectl]

# Dependency graph
requires:
  - phase: 53-01
    provides: tenant-cfg01-single.yaml fixture, suppression fixture, Snapshot Evaluation report category
  - phase: 51-e2e-sim-http
    provides: sim_set_scenario healthy/command_trigger/default HTTP control endpoints
provides:
  - STS-01 healthy baseline scenario (tier=3 no-action, zero command counters)
  - STS-02 evaluate violated scenario (tier=4 command dispatch, sent counter delta > 0)
  - STS-03 resolved gate scenario (tier=2 ConfirmedBad, zero command counters, tier=4 absent)
affects: [53-03, 53-04, phase-54, phase-55, run-all.sh scenario ordering]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - save_configmap/apply-fixture/poll_until_log/assert/reset_scenario/restore_configmap lifecycle pattern for all STS scenarios
    - Dual assertion per scenario (log presence AND counter delta)
    - Negative assertion via pod log grep (not poll_until_log) for absence checks

key-files:
  created:
    - tests/e2e/scenarios/29-sts-01-healthy.sh
    - tests/e2e/scenarios/30-sts-02-evaluate-violated.sh
    - tests/e2e/scenarios/31-sts-03-resolved-gate.sh
  modified: []

key-decisions:
  - "poll_until_log 90s timeout for tier=4 in STS-02 — TimeSeriesSize=3 requires ~30s of poll cycles before series is full enough to trigger command dispatch"
  - "Negative tier=4 assertion in STS-03 uses a direct pod log grep (since=60s) rather than poll_until_log — absence check needs a snapshot, not a poll"
  - "sim_set_scenario default called explicitly in STS-03 for clarity, even though reset_scenario is equivalent"

patterns-established:
  - "STS log patterns match SnapshotJob.cs exactly — use UTF-8 em-dash (U+2014) in grep strings"
  - "Tier-log assertions always use poll_until_log; counter assertions snapshot before and after"
  - "Negative assertions grep recent pod logs directly (kubectl logs --since=60s | grep pattern)"

# Metrics
duration: 3min
completed: 2026-03-17
---

# Phase 53 Plan 02: Single-Tenant Scenarios (STS-01/02/03) Summary

**Bash E2E scenario scripts for tier=3 healthy, tier=4 command dispatch, and tier=2 ConfirmedBad branches of SnapshotJob's 4-tier evaluation tree, each asserting both log presence and Prometheus counter deltas**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-17T13:22:36Z
- **Completed:** 2026-03-17T13:25:16Z
- **Tasks:** 2
- **Files modified:** 3 created

## Accomplishments

- STS-01: healthy baseline script asserting tier=3 log and zero command/suppression counter deltas
- STS-02: evaluate-violated script asserting tier=4 log and command_sent delta > 0 via assert_delta_gt
- STS-03: resolved-gate script asserting tier=2 ConfirmedBad log, zero command counters, and absence of tier=4 in recent pod logs

## Task Commits

Each task was committed atomically:

1. **Task 1: STS-01 healthy baseline script** - `73c45c3` (feat)
2. **Task 2: STS-02 evaluate violated and STS-03 resolved gate scripts** - `74c8711` (feat)

**Plan metadata:** _(see final docs commit below)_

## Files Created/Modified

- `tests/e2e/scenarios/29-sts-01-healthy.sh` - Tier=3 healthy no-action scenario: applies tenant-cfg01-single.yaml, sets healthy sim scenario, polls for tier=3 log, asserts zero command/suppressed deltas
- `tests/e2e/scenarios/30-sts-02-evaluate-violated.sh` - Tier=4 command dispatch scenario: applies fixture, sets command_trigger sim scenario, polls for tier=4 log, asserts sent delta > 0
- `tests/e2e/scenarios/31-sts-03-resolved-gate.sh` - Tier=2 ConfirmedBad scenario: applies fixture, sets default sim scenario (all Resolved violated), polls for tier=2 log, asserts zero command deltas, negative-asserts tier=4 absence

## Decisions Made

- **STS-02 timeout 90s:** The command_trigger scenario requires the TimeSeriesSize=3 Evaluate holder to accumulate 3 violated samples across ~30s of poll cycles before tier=4 fires. The 90s poll_until_log window accommodates this safely.
- **STS-03 negative assertion:** Checking that tier=4 is absent while ConfirmedBad is active uses a direct kubectl logs grep (since=60s) rather than poll_until_log. This is a snapshot check — polling would just time out; a single-pass grep of recent logs is the correct approach.
- **Explicit sim_set_scenario default in STS-03:** Called explicitly for documentation clarity even though reset_scenario is an alias for the same call. Makes the scenario intent obvious in code.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- STS-01, STS-02, STS-03 scripts complete. Scenarios 29-31 are in sequence, ready for run-all.sh inclusion.
- Phase 53-03 (STS-04 suppression window) and any remaining single-tenant scenarios can proceed.
- All three scripts follow the established save/apply/poll/assert/cleanup pattern — ready for report category integration in the phase summary.

---
*Phase: 53-single-tenant-scenarios*
*Completed: 2026-03-17*
