---
phase: 83-command-interpreter
plan: 01
subsystem: testing
tags: [bash, e2e, simulator, kubectl, port-forward, oid-map]

# Dependency graph
requires:
  - phase: 82-fixture-and-oid-mapping
    provides: oid_map.sh with OID_MAP associative array for all 4 tenants (T1_P1, T2_P1, T1_P2, T2_P2)
provides:
  - Standalone Bash command interpreter sim_command.sh accepting {Tenant}-{V/S}-{#}E-{#}R patterns
  - Pattern validation with 3 error types (malformed pattern, unknown tenant, count exceeded)
  - V-mode translation to violated/healthy OID value curl POSTs
  - S-mode translation to stale/healthy OID curl POSTs
  - Self-managed kubectl port-forward lifecycle with trap cleanup
  - Silent-on-success convention
affects:
  - e2e-test-scenarios
  - v2.6-manual-simulation-workflows

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "BASH_SOURCE-relative source for library scripts (portable across invocation paths)"
    - "Port-forward lifecycle: background kubectl + PF_PID capture + trap cleanup EXIT"
    - "Silent success convention: no stdout on success, red stderr on error"
    - "apply_role() loop: first N slots get violated/stale, remaining slots get healthy reset"

key-files:
  created:
    - tests/e2e/lib/sim_command.sh
  modified: []

key-decisions:
  - "#!/usr/bin/env bash shebang (not #!/bin/bash) to support macOS which ships bash 3)"
  - "Bash 4+ version guard required because OID_MAP is an associative array (bash 4 feature)"
  - "Port-forward managed inside sim_command.sh — caller needs no kubectl knowledge"
  - "Non-affected metric slots always reset to healthy on every invocation (stateless idempotent behavior)"
  - "Human verification checkpoint included — cluster-level functional test performed by operator"

patterns-established:
  - "sim_command.sh pattern: source oid_map.sh, validate, port-forward, translate, clean up"
  - "Verbose flag -v prints POST details to stderr without breaking silent-success contract"

# Metrics
duration: ~30min
completed: 2026-03-24
---

# Phase 83 Plan 01: Command Interpreter Summary

**Standalone sim_command.sh interpreter accepting {Tenant}-{V/S}-{#}E-{#}R patterns, translating them to simulator HTTP calls via self-managed kubectl port-forward with 3 validation error types and silent-on-success behavior**

## Performance

- **Duration:** ~30 min
- **Started:** 2026-03-24
- **Completed:** 2026-03-24
- **Tasks:** 2 (1 auto + 1 human-verify checkpoint)
- **Files modified:** 1

## Accomplishments

- Created `tests/e2e/lib/sim_command.sh` — the final v2.6 deliverable enabling single-command tenant simulation
- Implemented pattern parsing via bash regex `^([A-Z0-9_]+)-([VS])-([0-9]+)E-([0-9]+)R$` with BASH_REMATCH extraction
- Implemented all 3 error types: malformed pattern (CMD-06), unknown tenant (CMD-04), count exceeded (CMD-05)
- V-mode: violated OID values for first N slots, healthy reset for remaining slots (CMD-03, CMD-07)
- S-mode: stale endpoint calls for first N slots, healthy reset for remaining slots (CMD-08, CMD-07)
- Port-forward lifecycle fully self-managed: background kubectl process + PF_PID + trap cleanup EXIT
- Human verification approved by operator

## Task Commits

Each task was committed atomically:

1. **Task 1: Create sim_command.sh standalone command interpreter** - `106bcc8` (feat)
2. **Task 2: Human verification checkpoint** - approved (no commit — checkpoint type)

**Plan metadata:** (docs: complete command interpreter plan — this commit)

## Files Created/Modified

- `tests/e2e/lib/sim_command.sh` - Standalone command interpreter; sources oid_map.sh, manages port-forward, parses and validates patterns, translates to simulator HTTP API calls

## Decisions Made

- Used `#!/usr/bin/env bash` (not `#!/bin/bash`) to avoid macOS bash 3 which lacks associative arrays
- Added explicit Bash 4+ version guard since OID_MAP depends on `declare -A` (bash 4 feature)
- Port-forward is managed inside the script — the operator runs one command with no kubectl prerequisite
- Every invocation fully resets non-affected slots to healthy values, making each command idempotent and stateless (no "undo" command needed — just run 0E-0R)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required. The script manages its own kubectl port-forward.

## Next Phase Readiness

- v2.6 command interpreter is complete and human-verified
- Phase 83 is the final phase of v2.6 E2E Manual Tenant Simulation Suite
- Operator can now drive any of the 4 tenants (T1_P1, T2_P1, T1_P2, T2_P2) into any violation/stale state with a single terse command
- Milestone v2.6 is complete

---
*Phase: 83-command-interpreter*
*Completed: 2026-03-24*
