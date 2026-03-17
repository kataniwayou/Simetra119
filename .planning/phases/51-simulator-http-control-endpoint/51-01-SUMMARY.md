---
phase: 51-simulator-http-control-endpoint
plan: 01
subsystem: testing
tags: [aiohttp, pysnmp, snmp-simulator, e2e-testing, asyncio, scenario-switching]

# Dependency graph
requires: []
provides:
  - HTTP-controlled SNMP simulator with 5 hardcoded scenarios and 15 OIDs
  - POST /scenario/{name} endpoint for runtime scenario switching
  - GET /scenario and GET /scenarios endpoints for inspection
  - 6 new test-purpose OIDs in .999.4.x subtree
  - WritableDynamicInstance for SNMP SET round-trip on command response OID
  - noSuchInstance staleness support via NoSuchInstanceError
affects:
  - 52-simulator-k8s-http-port
  - 53-e2e-test-scenarios
  - 54-e2e-test-execution
  - 55-e2e-test-validation

# Tech tracking
tech-stack:
  added: [aiohttp==3.13.3 (already in requirements.txt)]
  patterns:
    - Scenario registry with _make_scenario() helper and STALE sentinel
    - DynamicInstance reads from SCENARIOS[_active_scenario] dict by OID string
    - WritableDynamicInstance stores SET value in active scenario dict
    - aiohttp AppRunner/TCPSite started via loop.run_until_complete() BEFORE open_dispatcher()
    - MibScalar.setMaxAccess("readwrite") required for VACM to permit SET

key-files:
  created: []
  modified:
    - simulators/e2e-sim/e2e_simulator.py

key-decisions:
  - "Use _make_scenario(overrides) helper so every scenario is a complete 15-OID dict — avoids merge logic that masks scenario isolation bugs"
  - "Store raw pysnmp type object (not converted to int) in scenario dict on SET — getSyntax().clone() accepts same-type objects directly"
  - "STALE sentinel is a module-level object() — identity comparison (is STALE) is safe and cannot be accidentally triggered by valid OID values"
  - "run_until_complete(start_http_server()) called before open_dispatcher() — hard ordering constraint, open_dispatcher() blocks the event loop"
  - "setMaxAccess('readwrite') on MibScalar for .4.4 — VACM checks this before dispatching to instance writeCommit"

patterns-established:
  - "Scenario switching: module-level _active_scenario string changed by HTTP handler; DynamicInstance.getValue() reads SCENARIOS[_active_scenario] on every call"
  - "Staleness: raise NoSuchInstanceError(name=name, idx=(0,)) from getValue() — collector silently skips noSuchInstance, no RecordFailure"
  - "cbFun pattern: both writeTest and writeCommit must call ctx['cbFun'](varBind, **ctx) or pysnmp state machine stalls"

# Metrics
duration: 3min
completed: 2026-03-17
---

# Phase 51 Plan 01: Simulator HTTP Control Endpoint Summary

**aiohttp HTTP server on port 8080 with 5-scenario registry (15 OIDs) enabling runtime SNMP value switching without simulator restart**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-17T10:54:32Z
- **Completed:** 2026-03-17T10:57:07Z
- **Tasks:** 2 of 2
- **Files modified:** 1

## Accomplishments

- Added 5 hardcoded scenarios (default, threshold_breach, threshold_clear, bypass_active, stale) each containing all 15 OIDs, built via `_make_scenario(overrides)` helper
- Refactored `DynamicInstance` to read from `SCENARIOS[_active_scenario]` by OID string; staleness returns `noSuchInstance` via `NoSuchInstanceError`
- Added `WritableDynamicInstance` for `.999.4.4` (e2e_command_response) with SET round-trip support and `cbFun` callbacks
- Registered 6 new test-purpose OIDs in `.999.4.x` subtree; `.4.4` scalar has `readwrite` maxAccess for VACM permission
- HTTP server (aiohttp AppRunner/TCPSite) starts on `0.0.0.0:8080` via `loop.run_until_complete()` before `snmpEngine.open_dispatcher()`; shutdown handler calls `runner.cleanup()`

## Task Commits

Each task was committed atomically:

1. **Task 1: Scenario registry, DynamicInstance, WritableDynamicInstance, 6 new OIDs** - `e44f6ca` (feat)
2. **Task 2: aiohttp HTTP control endpoint** - included in `e44f6ca` (same file write, same commit)

Note: Tasks 1 and 2 both modify only `e2e_simulator.py`. The complete implementation was written in a single file write, captured in a single atomic commit with a message covering both task scopes.

## Files Created/Modified

- `simulators/e2e-sim/e2e_simulator.py` - Complete rework: scenario registry (STALE sentinel, _make_scenario helper, SCENARIOS dict with 5 scenarios), refactored DynamicInstance with scenario lookup, WritableDynamicInstance with SET support, 6 new .999.4.x OIDs, aiohttp HTTP control endpoint (post_scenario/get_scenario/get_scenarios/start_http_server), startup ordering (HTTP before dispatcher), graceful shutdown with runner.cleanup()

## Decisions Made

- Used `_make_scenario(overrides)` helper so every scenario carries all 15 OIDs explicitly — avoids "falls back to default" merge logic that can mask scenario isolation bugs in tests
- Stored raw pysnmp value object (not converted to Python int) in scenario dict on SNMP SET — `getSyntax().clone()` accepts same-type objects, avoids type mismatch on subsequent GET
- `STALE = object()` identity sentinel — `is STALE` comparison cannot be accidentally triggered by any valid numeric or string OID value
- Placed all 6 new OIDs in `.999.4.x` subtree — `.999.1.x` (mapped), `.999.2.x` (unmapped), `.999.3.x` (trap) are reserved; avoids MIB tree collision
- `sorted(SCENARIOS.keys())` in GET /scenarios handler for deterministic ordering in test assertions

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- `e2e_simulator.py` is complete and syntax-valid; ready for Docker rebuild
- Phase 52 (K8s HTTP port) must add `containerPort: 8080/TCP` to e2e-sim Deployment and Service manifests, and `EXPOSE 8080/tcp` to Dockerfile
- Phase 53+ test scripts can switch scenarios via `curl -X POST http://localhost:8080/scenario/{name}`
- Key constraint preserved: `loop.run_until_complete(start_http_server())` is line 404, `snmpEngine.open_dispatcher()` is line 425

---
*Phase: 51-simulator-http-control-endpoint*
*Completed: 2026-03-17*
