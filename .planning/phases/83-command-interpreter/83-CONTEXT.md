# Phase 83: Command Interpreter - Context

**Gathered:** 2026-03-24
**Status:** Ready for planning

<domain>
## Phase Boundary

A standalone Bash script that accepts `{Tenant}-{V/S}-{#}E-{#}R` patterns, validates them against the OID mapping from Phase 82, and translates them into simulator HTTP API calls. Run from the host machine via Claude Code CLI. No extra commands (no reset, no status) — pattern commands only.

</domain>

<decisions>
## Implementation Decisions

### Invocation model
- Standalone script at `tests/e2e/lib/sim_command.sh` (run directly, not sourced)
- Run from host machine, not inside the cluster
- Script manages its own `kubectl port-forward` to the simulator (starts in background, cleans up on exit)
- Assumes tenant fixture ConfigMap is already applied — script only handles pattern commands
- Single pattern argument per invocation: `./sim_command.sh T1_P1-V-2E-1R`

### Output & feedback
- Silent on success (Unix convention — silence means it worked)
- Errors in red with helpful hints: "Unknown tenant T1_P3. Valid: T1_P1, T2_P1, T1_P2, T2_P2"
- `-v` flag for verbose/debug mode: shows each HTTP call made to the simulator
- No color on success output (there is none), red for errors only

### Multi-command patterns
- One pattern per invocation only
- No special commands (no RESET, no STATUS, no LIST)
- To affect multiple tenants, run the script multiple times
- User checks Grafana to observe state changes

### Healthy reset behavior
- Every command is a **full state declaration** for the tenant
- Non-violated/non-stale metrics are explicitly set to their healthy value
- `T1_P1-V-2E-0R` → 2 Evaluate violated, remaining Evaluate + all Resolved reset to healthy
- `T1_P1-V-0E-0R` → effectively resets the tenant to all-healthy
- `T1_P1-S-0E-0R` → also all-healthy (no stale metrics)
- Which specific slots get violated/staled doesn't matter (first N in order is fine)
- V or S mode — one mode per command, cannot mix in a single pattern

### Claude's Discretion
- Port-forward implementation details (port number, cleanup mechanism)
- How to source oid_map.sh from the script
- curl vs wget for HTTP calls
- Exact error message wording (beyond "helpful with hints")

</decisions>

<specifics>
## Specific Ideas

- Pattern format is fixed: `{Tenant}-{V/S}-{#}E-{#}R`
- V = violate mode (set OID to violated value), S = stale mode (call sim_set_oid_stale)
- The numbers represent how many metrics of that role to violate/stale, not which specific ones
- Sources `tests/e2e/lib/oid_map.sh` for all OID lookups (OID_MAP, TENANT_EVAL_COUNT, TENANT_RES_COUNT, VALID_TENANTS)

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 83-command-interpreter*
*Context gathered: 2026-03-24*
