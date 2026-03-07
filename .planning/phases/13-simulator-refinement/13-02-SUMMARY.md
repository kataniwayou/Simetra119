---
phase: 13-simulator-refinement
plan: 02
subsystem: simulator
tags: [pysnmp, snmp, npb, counter64, octetstring, traps]

# Dependency graph
requires:
  - phase: 12-npb-oid-population
    provides: oidmap-npb.json with 68 OIDs defining system and per-port metrics
provides:
  - NPB SNMP simulator serving exactly 68 OIDs matching oidmap-npb.json
  - Realistic traffic profiles with Counter64 counter increments
  - portLinkChange traps for active ports with status varbinds
  - Simetra.NPB-01 community string authentication
affects: [13-simulator-refinement, k8s-deployment, integration-testing]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Data-driven OID registration from metric tables"
    - "Traffic profile dictionary for per-port counter behavior"
    - "OctetString system health with float random walk"

key-files:
  created: []
  modified:
    - simulators/npb/npb_simulator.py

key-decisions:
  - "All 4 system metrics use OctetString per OID map (source of truth over CONTEXT.md Integer32)"
  - "Counter64 wrapping uses modular arithmetic (current + increment) % (COUNTER64_MAX + 1)"

patterns-established:
  - "NPB OID tree: 47477.100.1.* system, 47477.100.2.* per-port, 47477.100.3.* traps"

# Metrics
duration: 3min
completed: 2026-03-07
---

# Phase 13 Plan 02: NPB Simulator Rewrite Summary

**Clean rewrite of NPB simulator serving 68 OIDs (4 OctetString system + 64 per-port) with Simetra.NPB-01 community, traffic-profiled Counter64 counters, and portLinkChange traps**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-07T15:15:27Z
- **Completed:** 2026-03-07T15:18:17Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments
- Complete rewrite of npb_simulator.py from ~1200 lines (old 47477.100.4.* tree with ~560 OIDs) to 463 lines serving exactly 68 OIDs matching oidmap-npb.json
- Traffic profiles (heavy/medium/light/zero) drive realistic Counter64 increments per port with error/drop injection at ~1% rate
- System health OIDs (cpu_util, mem_util, sys_temp, uptime) served as OctetString with float random-walk values
- portLinkChange traps fire for 6 active ports (P1-P3, P5-P7) with correct trap OID and status varbind
- Community string defaults to Simetra.NPB-01, completely replacing old "public" community

## Task Commits

Each task was committed atomically:

1. **Task 1: Rewrite NPB simulator with 68 OIDs, traffic profiles, and system health** - `a3ed5b8` (feat)

## Files Created/Modified
- `simulators/npb/npb_simulator.py` - Complete rewrite: 68-OID NPB SNMP agent with traffic profiles, system health walk, and portLinkChange traps

## Decisions Made
- Followed OID map as source of truth for system metric types (OctetString, not Integer32 from CONTEXT.md) -- resolves the type conflict flagged in 13-RESEARCH.md
- Internal float tracking for system health with string formatting for OctetString output (e.g., "15.0" not "15")

## Deviations from Plan
None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- NPB simulator ready for containerized testing
- K8s health probes may need community string update (flagged in 13-RESEARCH.md Pitfall 1)
- OBP simulator rewrite (13-03) can proceed independently

---
*Phase: 13-simulator-refinement*
*Completed: 2026-03-07*
