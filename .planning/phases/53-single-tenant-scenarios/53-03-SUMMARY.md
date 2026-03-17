---
phase: 53-single-tenant-scenarios
plan: 03
subsystem: testing
tags: [bash, e2e, snmp, suppression, staleness, prometheus, kubernetes]

requires:
  - phase: 53-01
    provides: tenant-cfg01-suppression.yaml fixture with SuppressionWindowSeconds=30, healthy simulator scenario, Snapshot Evaluation report category

provides:
  - STS-04: 3-window suppression lifecycle scenario (send -> suppress -> send again after expiry)
  - STS-05: staleness detection scenario (tier=1 stale log + zero command activity)

affects:
  - 53-02 (produces scenarios 32 and 33 which complete the 5-scenario suite alongside 29-31)
  - 54-multi-tenant-scenarios (follow-on phase using same scenario pattern)
  - 55-reporting (scenario scripts feed into report generation)

tech-stack:
  added: []
  patterns:
    - "3-window suppression lifecycle: baseline -> poll_until to detect suppression -> sleep for expiry -> poll_until_log for re-send"
    - "Staleness priming: sim_set_scenario healthy + sleep 20 before sim_set_scenario stale ensures null slots are populated"
    - "Zero-delta assertion: snapshot_counter BEFORE, poll_until_log, snapshot_counter AFTER, [ delta -eq 0 ] with record_pass/record_fail"

key-files:
  created:
    - tests/e2e/scenarios/32-sts-04-suppression-window.sh
    - tests/e2e/scenarios/33-sts-05-staleness.sh
  modified: []

key-decisions:
  - "STS-04 uses poll_until to detect Window 2 suppression (not fixed sleep), but sleep 20 is used for Window 3 expiry wait -- the only fixed sleep in Phase 53 because no log event signals window expiry"
  - "STS-05 uses sim_set_scenario healthy + sleep 20 priming before stale switch -- required because HasStaleness returns false for null slots; slots must hold recent data to age out"
  - "STS-04 suppressed counter filter uses device_name=e2e-tenant-A-supp (tenant ID, not device name) per IncrementCommandSuppressed(tenant.Id) in SnapshotJob.cs"

patterns-established:
  - "Suppression window timing: suppress fixture must have SuppressionWindowSeconds > SnapshotJob interval (30s > 15s), otherwise window expires before second cycle fires"
  - "Staleness priming: any test switching to stale scenario must first prime with healthy + sleep to ensure non-null slot state"

duration: 3min
completed: 2026-03-17
---

# Phase 53 Plan 03: STS-04 Suppression Window and STS-05 Staleness Detection Summary

**Bash E2E scenario scripts for suppression lifecycle (3 windows, 6 sub-scenarios) and staleness detection (tier=1 stale log + zero command counters) completing the 5-scenario Phase 53 single-tenant coverage**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-17T13:23:43Z
- **Completed:** 2026-03-17T13:26:45Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- STS-04 (`32-sts-04-suppression-window.sh`): three sequential assertion windows validate the full suppression cache lifecycle -- first command sent (32a, 32b), second cycle suppressed within 30s window (32c, 32d), third cycle sent again after expiry (32e, 32f)
- STS-05 (`33-sts-05-staleness.sh`): staleness detection scenario primes with healthy data, switches to stale, waits for tier=1 stale log, asserts zero sent and suppressed counter deltas (33a, 33b, 33c)
- Both scripts follow the established save/apply/poll/assert/cleanup pattern from scenarios 28 and Phase 52 libraries

## Task Commits

Each task was committed atomically:

1. **Task 1: STS-04 suppression window script** - `1d00856` (feat)
2. **Task 2: STS-05 staleness detection script** - `7cfb343` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `tests/e2e/scenarios/32-sts-04-suppression-window.sh` - STS-04: 3-window suppression lifecycle with 6 sub-scenarios (32a-32f)
- `tests/e2e/scenarios/33-sts-05-staleness.sh` - STS-05: staleness detection with priming step and 3 sub-scenarios (33a-33c)

## Decisions Made

- **sleep 20 in STS-04 Window 3 is the only fixed sleep in Phase 53** -- no log event signals suppression window expiry, so a fixed sleep is required after the last suppression to guarantee the 30s window has passed before polling for the re-send
- **Staleness priming is mandatory** -- without `sim_set_scenario healthy` + `sleep 20` before switching to stale, metric slots may be null (never polled) and `HasStaleness` returns `false` for null slots, preventing tier=1 from triggering
- **STS-04 suppressed counter label is `device_name="e2e-tenant-A-supp"`** (the tenant ID from tenant-cfg01-suppression.yaml), not the device hostname -- this matches `IncrementCommandSuppressed(tenant.Id)` in SnapshotJob.cs

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Scenarios 32 and 33 complete the STS-01 through STS-05 coverage of all 4 tier branches (Tier 1 stale, Tier 2 ConfirmedBad, Tier 3 Healthy, Tier 4 Commanded/Suppressed)
- Plan 53-02 (scenarios 29-31 for STS-01 through STS-03) is still pending execution; once complete all 5 scenario files will exist
- No blockers for downstream phases

---
*Phase: 53-single-tenant-scenarios*
*Completed: 2026-03-17*
