---
phase: 52-test-library-and-config-artifacts
plan: 02
subsystem: testing
tags: [bash, e2e, simulator, port-forward, kubectl, curl, jq]

# Dependency graph
requires:
  - phase: 51-e2e-simulator-http-control
    provides: e2e_simulator.py HTTP control endpoint on port 8080
provides:
  - sim.sh bash library with sim_set_scenario, reset_scenario, get_active_scenario, poll_until_log
  - run-all.sh wired with source for sim.sh and port-forward to e2e-simulator:8080
affects:
  - 52-03 (config artifacts)
  - 53-scenario-scripts (scenarios 29-35+ use sim.sh functions)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "curl -sf -o /dev/null -w '%{http_code}' pattern for HTTP code validation"
    - "grep > /dev/null 2>&1 (not grep -q) to avoid SIGPIPE under set -euo pipefail"
    - "poll_until_log iterates all replica pods — any match returns success"
    - "SIM_URL global constant at top of library file"

key-files:
  created:
    - tests/e2e/lib/sim.sh
  modified:
    - tests/e2e/run-all.sh

key-decisions:
  - "sim_set_scenario validates HTTP response code (not just curl exit code) for robustness"
  - "reset_scenario is a thin wrapper over sim_set_scenario default — no separate logic"
  - "poll_until_log refreshes logs on every iteration using --since parameter"
  - "Port-forward started unconditionally in run-all.sh (not lazily) so all scenarios have it available"

patterns-established:
  - "Pattern: Source sim.sh after all other lib files in run-all.sh"
  - "Pattern: sim port-forward stacked directly after prometheus port-forward"

# Metrics
duration: 2min
completed: 2026-03-17
---

# Phase 52 Plan 02: sim.sh Library and run-all.sh Wiring Summary

**sim.sh bash library with 4 simulator HTTP control functions and run-all.sh extended with sim source and e2e-simulator:8080 port-forward**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-17T12:08:40Z
- **Completed:** 2026-03-17T12:10:14Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Created `tests/e2e/lib/sim.sh` (94 lines) with all 4 required functions: `sim_set_scenario`, `reset_scenario`, `get_active_scenario`, `poll_until_log`
- Each function returns non-zero on failure; uses `log_info`/`log_error` from common.sh
- `poll_until_log` iterates all `snmp-collector` replica pods and returns on first match (any pod), mirroring the multi-pod pattern from scenario 28
- Extended `run-all.sh` with `source "$SCRIPT_DIR/lib/sim.sh"` and `start_port_forward e2e-simulator 8080 8080` — no other changes

## Task Commits

Each task was committed atomically:

1. **Task 1: Create sim.sh library** - `691900a` (feat)
2. **Task 2: Wire sim.sh and port-forward into run-all.sh** - `eb0b4ab` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `tests/e2e/lib/sim.sh` - Simulator HTTP control library (sim_set_scenario, reset_scenario, get_active_scenario, poll_until_log)
- `tests/e2e/run-all.sh` - Added sim.sh source line and e2e-simulator:8080 port-forward

## Decisions Made

- Used `curl -sf -o /dev/null -w '%{http_code}'` pattern (matching prometheus.sh style) so HTTP status is validated explicitly, not just curl exit code
- `reset_scenario` delegates to `sim_set_scenario default` with no additional logic — belt-and-suspenders per research recommendation
- `get_active_scenario` uses `jq -r '.scenario'` to extract name from `{"scenario": "name"}` JSON response
- Port-forward started unconditionally at runner startup so all scenario scripts have access without any lazy-init logic

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- `sim.sh` library is ready to be sourced by scenario scripts 29-35+ in Phase 53
- `poll_until_log` follows the exact SIGPIPE-safe pattern from scenario 28 (no `grep -q`)
- Port-forward to e2e-simulator:8080 is started before any scenario runs; cleanup trap via `stop_port_forwards` handles teardown
- No concerns; Phase 53 scenario scripts can directly call `sim_set_scenario`, `reset_scenario`, `poll_until_log`

---
*Phase: 52-test-library-and-config-artifacts*
*Completed: 2026-03-17*
