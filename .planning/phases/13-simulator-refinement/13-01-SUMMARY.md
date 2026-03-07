---
phase: 13-simulator-refinement
plan: 01
subsystem: simulators
tags: [pysnmp, snmp-agent, obp, power-walk, community-string]

# Dependency graph
requires:
  - phase: 12-npb-oid-population
    provides: OID map convention (oidmap-obp.json with 24 entries)
provides:
  - OBP simulator serving 24 poll OIDs matching oidmap-obp.json 1:1
  - Power random walk on 16 R1-R4 receiver OIDs
  - Simetra.OBP-01 community string convention in simulator
  - StateChange traps with channel poll OID as varbind
affects: [13-simulator-refinement remaining plans, k8s-health-probes]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Data-driven OID registration via LINK_METRICS table"
    - "Power random walk with random_walk_int(current, step, low, high)"

key-files:
  created: []
  modified:
    - simulators/obp/obp_simulator.py

key-decisions:
  - "Trap varbind uses channel poll OID (not trap OID) per SNMP convention"
  - "Power update interval set to 10 seconds for visible dashboard movement"

patterns-established:
  - "LINK_METRICS table: define (suffix, state_key, syntax_cls) tuples, loop links x metrics for registration"
  - "Community string: Simetra.{DEVICE_NAME} convention, configurable via COMMUNITY env var"

# Metrics
duration: 5min
completed: 2026-03-07
---

# Phase 13 Plan 01: OBP Simulator Rewrite Summary

**Clean rewrite of OBP simulator: 24 poll OIDs (4 links x 6 metrics), Simetra.OBP-01 community, R1-R4 power random walk, StateChange traps with channel varbind**

## Performance

- **Duration:** 5 min
- **Started:** 2026-03-07T15:14:33Z
- **Completed:** 2026-03-07T15:19:33Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments
- Expanded OBP simulator from 8 to 24 poll OIDs, achieving 1:1 match with oidmap-obp.json
- Switched community string from "public" to Simetra.OBP-01 (configurable via DEVICE_NAME/COMMUNITY env vars)
- Added 16 power OIDs (4 links x 4 receivers) with random walk (step=2, bounds [-200,-50]) every 10 seconds
- Each link has distinct power baselines across R1-R4 for distinguishable dashboard traces
- Trap varbind now correctly uses channel poll OID instead of trap OID

## Task Commits

Each task was committed atomically:

1. **Task 1: Rewrite OBP simulator with 24 OIDs, power walk, and community string** - `e583650` (feat)

## Files Created/Modified
- `simulators/obp/obp_simulator.py` - Complete OBP SNMP agent rewrite (320 lines): 24 poll OIDs, power random walk, StateChange traps, Simetra.OBP-01 community

## Decisions Made
- Trap varbind uses the channel poll OID (`BYPASS_PREFIX.{link}.3.4.0`) rather than repeating the trap OID -- this matches SNMP convention where varbinds reference the polled object that changed
- Power update interval set to 10 seconds (POWER_UPDATE_INTERVAL) for visible movement on dashboards while not overwhelming the agent

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- OBP simulator ready for deployment with correct 24 OIDs
- K8s health probes still hardcode "public" community -- must be updated in a separate plan before deploying new simulator image
- NPB simulator rewrite (13-02) can proceed independently

---
*Phase: 13-simulator-refinement*
*Completed: 2026-03-07*
