---
phase: 53-single-tenant-scenarios
plan: 01
subsystem: testing
tags: [e2e, bash, snmp-simulator, prometheus, k8s, configmap]

# Dependency graph
requires:
  - phase: 52-e2e-simulator-enhancements
    provides: command_trigger scenario, e2e_set_bypass command, 3-group E2E-SIM device
  - phase: 51-e2e-http-control
    provides: HTTP control endpoint for sim_set_scenario
provides:
  - healthy simulator scenario (.4.1=5, .4.2=2, .4.3=2) producing Tier 3 path
  - tenant-cfg01-suppression.yaml with SuppressionWindowSeconds=30 and distinct ID
  - report.sh Snapshot Evaluation category covering indices 28-32
affects: [53-single-tenant-scenarios plans 02+, Phase 54, Phase 55]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Suppression fixture uses distinct tenant ID to prevent cache bleed across scenarios"
    - "SuppressionWindowSeconds=30 > SnapshotJob interval=15s so suppression is observable"

key-files:
  created:
    - tests/e2e/fixtures/tenant-cfg01-suppression.yaml
  modified:
    - simulators/e2e-sim/e2e_simulator.py
    - tests/e2e/lib/report.sh

key-decisions:
  - "SuppressionWindowSeconds=30 in suppression fixture (not 10) so window outlasts one 15s SnapshotJob cycle"
  - "Distinct tenant ID e2e-tenant-A-supp prevents suppression cache bleed from other scenarios"
  - "Device Lifecycle trimmed to indices 26-27; placeholder Watcher Resilience and Tenant Vector removed"

patterns-established:
  - "Suppression fixture: always use distinct tenant ID and window > SnapshotJob interval"

# Metrics
duration: 2min
completed: 2026-03-17
---

# Phase 53 Plan 01: Single-Tenant Scenario Prerequisites Summary

**"healthy" simulator scenario, suppression-tuned tenant fixture, and Snapshot Evaluation report category — three prerequisites unblocking all five STS scenario scripts**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-17T13:18:59Z
- **Completed:** 2026-03-17T13:20:36Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- Added `"healthy"` scenario to e2e_simulator.py SCENARIOS dict: .4.1=5, .4.2=2, .4.3=2 — the only scenario producing Tier 3 Healthy path (resolved in-range AND evaluate in-range)
- Created `tenant-cfg01-suppression.yaml` with `SuppressionWindowSeconds=30` and distinct tenant ID `e2e-tenant-A-supp` for STS-04 suppression window test
- Fixed `report.sh` _REPORT_CATEGORIES: removed two placeholder entries with no scenario files, trimmed Device Lifecycle upper bound, added Snapshot Evaluation covering Phase 53 scenarios (indices 28-32)

## Task Commits

Each task was committed atomically:

1. **Task 1: Add healthy simulator scenario and suppression fixture** - `15d9359` (feat)
2. **Task 2: Fix report.sh categories and add Snapshot Evaluation** - `1e56198` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `simulators/e2e-sim/e2e_simulator.py` - Added 7th scenario: "healthy" with .4.1=5, .4.2=2, .4.3=2
- `tests/e2e/fixtures/tenant-cfg01-suppression.yaml` - New: suppression-tuned tenant with 30s window, ID e2e-tenant-A-supp
- `tests/e2e/lib/report.sh` - Fixed _REPORT_CATEGORIES: 6 entries → 5 entries; Device Lifecycle 26-27; Snapshot Evaluation 28-32

## Decisions Made

- **SuppressionWindowSeconds=30** was chosen over keeping the default 10s: the SnapshotJob fires every 15s, so a 10s suppression window expires before the next cycle; 30s ensures the second cycle (T=15s) is still within the window and the suppressed counter increments
- **Distinct tenant ID `e2e-tenant-A-supp`** prevents the suppression cache key (which includes tenant ID) from bleeding between scenarios that use the single-tenant fixture vs the suppression fixture
- **Two placeholder report categories removed** (Watcher Resilience, Tenant Vector) rather than keeping them as dead entries; Device Lifecycle trimmed from 26-28 to 26-27 to match the two actual scenario files

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All prerequisites for Phase 53 scenario scripts are in place
- STS-01 through STS-05 can be authored in plans 02+ without any further infrastructure changes
- The "healthy" scenario is unblocked; STS-04 suppression test has its fixture ready with correct window timing

---
*Phase: 53-single-tenant-scenarios*
*Completed: 2026-03-17*
