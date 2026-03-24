# Phase 83: Command Interpreter - Research

**Researched:** 2026-03-24
**Domain:** Bash CLI script, kubectl port-forward, curl HTTP calls, pattern parsing
**Confidence:** HIGH

## Summary

Phase 83 delivers a single standalone Bash script (`tests/e2e/lib/sim_command.sh`) that accepts a `{Tenant}-{V/S}-{#}E-{#}R` pattern, sources the OID map from Phase 82, validates the command, and fires `curl` calls against the simulator HTTP API. All required infrastructure (OID map, simulator endpoints, existing port-forward patterns) already exists in the codebase. This phase is purely an integration layer — no new dependencies, no new K8s resources.

The implementation pattern is well-established in the project: `tests/e2e/lib/kubectl.sh` already shows the canonical `kubectl port-forward ... &` + PID tracking + `trap ... EXIT` cleanup approach. The `tests/e2e/lib/sim.sh` library already provides `sim_set_oid` and `sim_set_oid_stale` as reference implementations for the exact HTTP call patterns needed. The new script mirrors these patterns in a self-contained standalone form.

The core logic is: parse pattern → validate (tenant, mode, counts) → for each OID slot of the tenant, call `sim_set_oid` with violated/healthy value (V mode) or `sim_set_oid_stale`/`sim_set_oid` with healthy value (S mode). Every invocation fully declares state — non-affected slots are always explicitly reset to their healthy value.

**Primary recommendation:** Write `sim_command.sh` as a standalone script that sources `oid_map.sh` via path relative to `BASH_SOURCE[0]`, manages its own port-forward on port 8080 using the `PF_PIDS` + `trap cleanup EXIT` pattern from `kubectl.sh`, and uses `curl` (matching `sim.sh`) for all HTTP calls. No new library files needed.

## Standard Stack

This phase requires no external libraries — it is pure Bash + standard Unix tools.

### Core
| Tool | Version | Purpose | Why Standard |
|------|---------|---------|--------------|
| bash | system (>=4.0 required for `declare -A`) | Script interpreter and associative arrays | Already used by all lib/ scripts; `oid_map.sh` uses `declare -A` so bash 4+ is required |
| curl | system | HTTP calls to simulator | Already used by `sim.sh` for all simulator interactions; `-sf -o /dev/null -w '%{http_code}'` pattern is established |
| kubectl | system (cluster access) | Port-forward management | Already used by `kubectl.sh` for all port-forward and ConfigMap operations |

### Supporting
| Tool | Purpose | When to Use |
|------|---------|-------------|
| `declare -A` (bash builtin) | Associative arrays for OID_MAP lookup | Used by `oid_map.sh` — sourcing it requires bash 4+ |
| `trap ... EXIT` (bash builtin) | Guaranteed port-forward cleanup | Used by all existing test runners |
| `printf '\033[0;31m...\033[0m'` | Red error output | Only for error messages (no color on success) |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| curl | wget | curl is already the project standard in sim.sh — no reason to introduce wget |
| inline port-forward | source kubectl.sh | kubectl.sh is designed to be sourced by test suites, not standalone scripts; inline is cleaner for a standalone script |
| relative path sourcing | absolute path | `BASH_SOURCE[0]` relative path is robust regardless of CWD; absolute paths break portability |

**Installation:** None required. All tools are already present in the dev environment.

## Architecture Patterns

### Recommended File Structure

```
tests/e2e/lib/
├── oid_map.sh       # Phase 82 deliverable — sourced by sim_command.sh
├── sim_command.sh   # Phase 83 deliverable — standalone script
├── common.sh        # Existing — NOT sourced by sim_command.sh
├── kubectl.sh       # Existing — NOT sourced (patterns inlined)
└── sim.sh           # Existing — NOT sourced (patterns inlined)
```

`sim_command.sh` is a standalone script, not a library. It does NOT source common.sh, kubectl.sh, or sim.sh. All required patterns (port-forward, curl calls) are inlined. Only `oid_map.sh` is sourced.

### Pattern 1: Self-Relative Source of oid_map.sh

**What:** Source `oid_map.sh` using a path derived from `BASH_SOURCE[0]`, so the script works regardless of what directory it is invoked from.

**When to use:** Any standalone script that needs to source a sibling file from the same `lib/` directory.

**Example:**
```bash
# Source: established pattern in tests/e2e/run-all.sh and scenarios/
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${SCRIPT_DIR}/oid_map.sh"
```

### Pattern 2: Standalone Port-Forward with Cleanup

**What:** Start `kubectl port-forward` in background, record PID, use `trap cleanup EXIT` to kill it on exit (success, error, or signal).

**When to use:** Any standalone script that needs to talk to a cluster service from the host.

**Example (derived from kubectl.sh pattern):**
```bash
# Source: tests/e2e/lib/kubectl.sh (start_port_forward / stop_port_forwards)
PF_PID=""

cleanup() {
    if [ -n "${PF_PID}" ]; then
        kill "${PF_PID}" 2>/dev/null || true
    fi
}
trap cleanup EXIT

kubectl port-forward svc/e2e-simulator 8080:8080 -n simetra &>/dev/null &
PF_PID=$!
sleep 2   # Allow port-forward to establish before first curl
```

Note: Port 8080 is the established simulator port (used in run-all.sh: `start_port_forward e2e-simulator 8080 8080`). Use port 8080.

### Pattern 3: Curl HTTP Call with HTTP Code Check

**What:** Use `curl -sf -o /dev/null -w '%{http_code}'` to make POST calls and capture the HTTP response code. Check for "200" explicitly.

**When to use:** All simulator HTTP calls in the script.

**Example:**
```bash
# Source: tests/e2e/lib/sim.sh (sim_set_oid)
http_code=$(curl -sf -o /dev/null -w '%{http_code}' \
    -X POST "http://localhost:8080/oid/${oid}/${value}" 2>/dev/null) || {
    echo -e "\033[0;31mERROR: curl failed for OID ${oid}\033[0m" >&2
    exit 1
}
if [ "$http_code" != "200" ]; then
    echo -e "\033[0;31mERROR: HTTP ${http_code} setting OID ${oid}\033[0m" >&2
    exit 1
fi
```

### Pattern 4: Pattern Parsing with bash Regex

**What:** Parse `{Tenant}-{V/S}-{#}E-{#}R` using Bash's `=~` regex operator to extract components.

**When to use:** Input validation before any HTTP calls are made.

**Example:**
```bash
# Parse: T1_P1-V-2E-1R
PATTERN="$1"
if [[ "$PATTERN" =~ ^([A-Z0-9_]+)-([VS])-([0-9]+)E-([0-9]+)R$ ]]; then
    TENANT="${BASH_REMATCH[1]}"   # T1_P1
    MODE="${BASH_REMATCH[2]}"     # V or S
    E_COUNT="${BASH_REMATCH[3]}"  # 2
    R_COUNT="${BASH_REMATCH[4]}"  # 1
else
    echo -e "\033[0;31mERROR: Invalid pattern '${PATTERN}'. Expected: {Tenant}-{V/S}-{#}E-{#}R (e.g. T1_P1-V-2E-1R)\033[0m" >&2
    exit 1
fi
```

### Pattern 5: OID_MAP Lookup for All Slots

**What:** Iterate from slot 1 to TENANT_EVAL_COUNT (or TENANT_RES_COUNT) for the tenant. For the first N slots, apply violated/stale value; for remaining slots, apply healthy value.

**When to use:** The core translation loop — always walks all slots for both E and R roles.

**Example (V mode):**
```bash
# Source: oid_map.sh key format OID_MAP[TENANT.ROLE.N.FIELD]
e_total="${TENANT_EVAL_COUNT[${TENANT}]}"
r_total="${TENANT_RES_COUNT[${TENANT}]}"

# Evaluate slots
for i in $(seq 1 "$e_total"); do
    oid="${OID_MAP[${TENANT}.E.${i}.oid]}"
    if [ "$i" -le "$E_COUNT" ]; then
        value="${OID_MAP[${TENANT}.E.${i}.violated]}"  # 0
    else
        value="${OID_MAP[${TENANT}.E.${i}.healthy]}"   # 10
    fi
    # POST http://localhost:8080/oid/${oid}/${value}
done

# Resolved slots
for i in $(seq 1 "$r_total"); do
    oid="${OID_MAP[${TENANT}.R.${i}.oid]}"
    if [ "$i" -le "$R_COUNT" ]; then
        value="${OID_MAP[${TENANT}.R.${i}.violated]}"  # 0
    else
        value="${OID_MAP[${TENANT}.R.${i}.healthy]}"   # 1
    fi
    # POST http://localhost:8080/oid/${oid}/${value}
done
```

**S mode difference:** For stale slots, POST to `/oid/${oid}/stale` instead of `/oid/${oid}/${value}`. For healthy slots, still POST to `/oid/${oid}/${healthy_value}` (not stale) — this is how `T1_P1-V-2E-2R` then `T1_P1-S-2E-0R` correctly cancels the previous violate state.

### Pattern 6: Verbose Flag

**What:** Check for `-v` as the first argument before the pattern argument. In verbose mode, print each HTTP call as it is made to stderr.

**When to use:** Always — `-v` is part of the spec.

**Example:**
```bash
VERBOSE=0
if [ "${1:-}" = "-v" ]; then
    VERBOSE=1
    shift
fi
# ...
if [ "$VERBOSE" -eq 1 ]; then
    echo "[verbose] POST http://localhost:8080/oid/${oid}/${value}" >&2
fi
```

### Pattern 7: Tenant Validation with Helpful Error

**What:** After parsing the tenant name from the pattern, check it against `VALID_TENANTS` (sourced from `oid_map.sh`). If not found, print all valid tenants.

**Example:**
```bash
# VALID_TENANTS="T1_P1 T2_P1 T1_P2 T2_P2" (from oid_map.sh)
valid=0
for t in $VALID_TENANTS; do
    if [ "$t" = "$TENANT" ]; then
        valid=1
        break
    fi
done
if [ "$valid" -eq 0 ]; then
    echo -e "\033[0;31mERROR: Unknown tenant '${TENANT}'. Valid: ${VALID_TENANTS}\033[0m" >&2
    exit 1
fi
```

### Pattern 8: Count Validation Against Mapping

**What:** After tenant validation, verify that E_COUNT <= TENANT_EVAL_COUNT and R_COUNT <= TENANT_RES_COUNT. Both checks use the same sourced arrays from `oid_map.sh`.

**Example:**
```bash
e_total="${TENANT_EVAL_COUNT[${TENANT}]}"
r_total="${TENANT_RES_COUNT[${TENANT}]}"

if [ "$E_COUNT" -gt "$e_total" ]; then
    echo -e "\033[0;31mERROR: ${TENANT} has ${e_total} Evaluate metrics, cannot violate/stale ${E_COUNT}E\033[0m" >&2
    exit 1
fi
if [ "$R_COUNT" -gt "$r_total" ]; then
    echo -e "\033[0;31mERROR: ${TENANT} has ${r_total} Resolved metrics, cannot violate/stale ${R_COUNT}R\033[0m" >&2
    exit 1
fi
```

### Anti-Patterns to Avoid

- **Sourcing common.sh/sim.sh/kubectl.sh:** These are library files designed for the test suite harness, not for standalone scripts. They assume test runner context (SCENARIO_RESULTS, PF_PIDS array, etc.). Inline the needed patterns instead.
- **Using `set -e` without error guards on cleanup:** `kill $PF_PID` may fail if the port-forward already died. Always use `|| true` on cleanup operations.
- **Starting port-forward without a sleep:** The kubectl port-forward process needs ~1-2 seconds to establish before the first curl call. The existing pattern uses `sleep 2`.
- **Not resetting healthy slots:** CMD-07 requires non-violated metrics to be set to healthy value. Omitting this leaves the simulator in a stale partial state from a previous command.
- **Mixing >&2 and stdout for errors:** Error output must go to stderr (>&2) so that the silent-on-success convention is preserved for stdout.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| OID lookup table | Custom parsing of fixture YAML | `source oid_map.sh` | oid_map.sh was purpose-built in Phase 82 exactly for this; it has all 72 entries with healthy/violated values |
| HTTP calls | Custom TCP/socket code | `curl` with `-sf -o /dev/null -w '%{http_code}'` | Exact pattern already verified in sim.sh; handles errors correctly |
| Port-forward lifecycle | Complex process management | `kubectl port-forward ... &` + PID + `trap cleanup EXIT` | kubectl.sh pattern already handles the gotchas (kill on EXIT, sleep 2 wait) |
| Pattern parsing | Hand-written string splitting | Bash `=~` with `BASH_REMATCH` | Clean, no subshells needed, handles edge cases in one regex |

**Key insight:** Everything this script needs already exists in the codebase. The script is glue code, not new infrastructure.

## Common Pitfalls

### Pitfall 1: Bash 3 vs Bash 4 Associative Arrays

**What goes wrong:** `declare -A` requires bash 4+. macOS ships bash 3.2 as `/bin/bash`. If the script uses `#!/bin/bash`, it will fail on macOS with "declare: -A: invalid option".

**Why it happens:** Apple's bash is ancient due to GPLv3 licensing. Many Mac users have bash 5 installed via Homebrew at `/usr/local/bin/bash` or `/opt/homebrew/bin/bash`, but `#!/bin/bash` picks up the system one.

**How to avoid:** Use `#!/usr/bin/env bash` (already the convention in the entire lib/ directory — every existing script uses this shebang). This picks up the user's PATH-resolved bash, which is typically modern on developer machines. Add a version guard at the top to fail clearly if bash < 4:

```bash
#!/usr/bin/env bash
if [ "${BASH_VERSINFO[0]}" -lt 4 ]; then
    echo "ERROR: bash 4+ required (found ${BASH_VERSION})" >&2
    exit 1
fi
```

**Warning signs:** `declare: -A: invalid option` error when running the script.

### Pitfall 2: Port-Forward Race Condition

**What goes wrong:** The first `curl` call fails because `kubectl port-forward` hasn't finished binding the local port yet.

**Why it happens:** `kubectl port-forward` starts asynchronously. The port is not immediately available after `&`.

**How to avoid:** Sleep 2 seconds after starting port-forward before making any HTTP calls. This is the established pattern in `kubectl.sh` (`sleep 2` in `start_port_forward`). Consider adding a retry loop for the first curl call if robustness is needed.

**Warning signs:** `curl: (7) Failed to connect to localhost port 8080: Connection refused` on the first call.

### Pitfall 3: Port 8080 Conflict

**What goes wrong:** `kubectl port-forward` fails because port 8080 is already in use on the host (e.g., from a previous run that didn't clean up, or another test runner is active).

**Why it happens:** Port 8080 is a common development port. If a previous `sim_command.sh` invocation crashed without cleanup, or `run-all.sh` is running concurrently, the port is occupied.

**How to avoid:** Check port availability before starting, or catch the port-forward startup error. The script should print a clear message: "Port 8080 already in use — is another test runner running?"

**Warning signs:** `kubectl port-forward` exits immediately with non-zero; curl calls fail with connection refused.

### Pitfall 4: Stale Mode Does Not Cancel Previous Violations Automatically

**What goes wrong:** User runs `T1_P1-V-2E-1R`, then `T1_P1-S-2E-0R`. The resolved slots that were set to `violated=0` by the first command stay at `0` (not healthy) unless the S command explicitly calls `sim_set_oid` with the healthy value for them.

**Why it happens:** The simulator maintains state independently per OID. Setting some OIDs to stale has no effect on other OIDs that remain at `0`.

**How to avoid:** CMD-07 requires this: ALL non-stale slots must be set to their healthy value. The S-mode loop must call `sim_set_oid "${oid}" "${healthy}"` for every non-stale slot, covering both E and R roles. "First N in stale mode" means: slots 1..N get `sim_set_oid_stale`, slots N+1..total AND all slots of the other role get `sim_set_oid "${oid}" "${healthy}"`.

**Warning signs:** Grafana shows unexpected state persistence between commands.

### Pitfall 5: Error Output Going to Stdout Instead of Stderr

**What goes wrong:** Error messages appear in stdout, which breaks the Unix convention and confuses scripts that capture stdout.

**Why it happens:** Forgetting `>&2` on error echo statements.

**How to avoid:** All error messages must use `echo ... >&2`. The script produces NO stdout output on success (silence = success convention per CONTEXT.md).

### Pitfall 6: OID_MAP Key Access Returns Empty String

**What goes wrong:** `${OID_MAP[T1_P1.E.1.oid]}` returns empty string instead of "8.1", causing curl to POST to `/oid//10` which returns 404.

**Why it happens:** `oid_map.sh` not successfully sourced (wrong path), or bash version issue with associative arrays.

**How to avoid:** After sourcing `oid_map.sh`, add a sanity check:
```bash
if [ -z "${OID_MAP[T1_P1.E.1.oid]+x}" ]; then
    echo "ERROR: oid_map.sh not loaded correctly" >&2
    exit 1
fi
```

**Warning signs:** HTTP 404 errors with empty OID segments in the URL path.

## Code Examples

### Complete Script Skeleton

```bash
#!/usr/bin/env bash
# sim_command.sh — Phase 83 Command Interpreter
# Usage: ./sim_command.sh [-v] {Tenant}-{V/S}-{#}E-{#}R
# Source: this file is tests/e2e/lib/sim_command.sh

set -euo pipefail

# Bash 4+ required for associative arrays (oid_map.sh uses declare -A)
if [ "${BASH_VERSINFO[0]}" -lt 4 ]; then
    echo "ERROR: bash 4+ required (found ${BASH_VERSION})" >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${SCRIPT_DIR}/oid_map.sh"

SIM_URL="http://localhost:8080"
RED='\033[0;31m'
NC='\033[0m'

error() { echo -e "${RED}ERROR: $*${NC}" >&2; exit 1; }

# ---------------------------------------------------------------------------
# Port-forward lifecycle
# ---------------------------------------------------------------------------

PF_PID=""
cleanup() {
    if [ -n "${PF_PID}" ]; then
        kill "${PF_PID}" 2>/dev/null || true
    fi
}
trap cleanup EXIT

kubectl port-forward svc/e2e-simulator 8080:8080 -n simetra &>/dev/null &
PF_PID=$!
sleep 2

# ---------------------------------------------------------------------------
# Argument parsing
# ---------------------------------------------------------------------------

VERBOSE=0
if [ "${1:-}" = "-v" ]; then
    VERBOSE=1
    shift
fi

[ $# -eq 1 ] || error "Usage: $(basename "$0") [-v] {Tenant}-{V/S}-{#}E-{#}R"

PATTERN="$1"

if [[ "$PATTERN" =~ ^([A-Z0-9_]+)-([VS])-([0-9]+)E-([0-9]+)R$ ]]; then
    TENANT="${BASH_REMATCH[1]}"
    MODE="${BASH_REMATCH[2]}"
    E_COUNT="${BASH_REMATCH[3]}"
    R_COUNT="${BASH_REMATCH[4]}"
else
    error "Invalid pattern '${PATTERN}'. Expected: {Tenant}-{V/S}-{#}E-{#}R (e.g. T1_P1-V-2E-1R)"
fi

# ---------------------------------------------------------------------------
# Validation
# ---------------------------------------------------------------------------

valid=0
for t in $VALID_TENANTS; do
    [ "$t" = "$TENANT" ] && valid=1 && break
done
[ "$valid" -eq 1 ] || error "Unknown tenant '${TENANT}'. Valid: ${VALID_TENANTS}"

e_total="${TENANT_EVAL_COUNT[${TENANT}]}"
r_total="${TENANT_RES_COUNT[${TENANT}]}"

[ "$E_COUNT" -le "$e_total" ] || error "${TENANT} has ${e_total} Evaluate metrics, cannot affect ${E_COUNT}E"
[ "$R_COUNT" -le "$r_total" ] || error "${TENANT} has ${r_total} Resolved metrics, cannot affect ${R_COUNT}R"

# ---------------------------------------------------------------------------
# HTTP call helpers
# ---------------------------------------------------------------------------

sim_post() {
    local oid="$1" value="$2"
    [ "$VERBOSE" -eq 1 ] && echo "[verbose] POST ${SIM_URL}/oid/${oid}/${value}" >&2
    local http_code
    http_code=$(curl -sf -o /dev/null -w '%{http_code}' \
        -X POST "${SIM_URL}/oid/${oid}/${value}" 2>/dev/null) \
        || error "curl failed for OID ${oid}"
    [ "$http_code" = "200" ] || error "HTTP ${http_code} setting OID ${oid}=${value}"
}

# ---------------------------------------------------------------------------
# Translation loop
# ---------------------------------------------------------------------------

apply_role() {
    local role="$1" count="$2" total="$3"
    for i in $(seq 1 "$total"); do
        local oid="${OID_MAP[${TENANT}.${role}.${i}.oid]}"
        if [ "$i" -le "$count" ]; then
            if [ "$MODE" = "V" ]; then
                sim_post "$oid" "${OID_MAP[${TENANT}.${role}.${i}.violated]}"
            else
                # S mode: stale the affected slots
                [ "$VERBOSE" -eq 1 ] && echo "[verbose] POST ${SIM_URL}/oid/${oid}/stale" >&2
                local http_code
                http_code=$(curl -sf -o /dev/null -w '%{http_code}' \
                    -X POST "${SIM_URL}/oid/${oid}/stale" 2>/dev/null) \
                    || error "curl failed setting OID ${oid} stale"
                [ "$http_code" = "200" ] || error "HTTP ${http_code} setting OID ${oid} stale"
            fi
        else
            # Non-affected slots always reset to healthy (CMD-07)
            sim_post "$oid" "${OID_MAP[${TENANT}.${role}.${i}.healthy]}"
        fi
    done
}

apply_role "E" "$E_COUNT" "$e_total"
apply_role "R" "$R_COUNT" "$r_total"

# Silent on success (Unix convention)
```

### Validation: Tenant Count Check

```bash
# Source: oid_map.sh TENANT_EVAL_COUNT / TENANT_RES_COUNT
# T1_P1: 2E/2R, T2_P1: 4E/4R, T1_P2: 2E/2R, T2_P2: 4E/4R
#
# T2_P1-V-5E-0R → ERROR: T2_P1 has 4 Evaluate metrics, cannot affect 5E
# T1_P1-V-3E-0R → ERROR: T1_P1 has 2 Evaluate metrics, cannot affect 3E
```

### S Mode: Stale + Healthy Reset

```bash
# T1_P1-S-2E-0R:
#   sim_post_stale  8.1  (E slot 1 — stale)
#   sim_post_stale  8.2  (E slot 2 — stale)
#   (no more E slots for T1_P1)
#   sim_post 8.3 1       (R slot 1 — healthy, CMD-07)
#   sim_post 8.4 1       (R slot 2 — healthy, CMD-07)
#
# This correctly cancels any prior T1_P1-V-*-*R violation by overwriting
# R slots with healthy values.
```

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|-----------------|--------|
| Manual curl calls per OID | sim_command.sh pattern command | Single invocation sets all slots atomically |
| Sourcing sim.sh from test suites | Inline HTTP patterns in standalone script | No harness dependency; runs independently |

**No deprecated patterns apply** — this is a new script with no legacy to replace.

## Open Questions

1. **Port 8080 conflict when run-all.sh is executing concurrently**
   - What we know: Both run-all.sh and sim_command.sh want port 8080 for the simulator
   - What's unclear: Whether the user intends to run them simultaneously
   - Recommendation: Document in the script header that it conflicts with run-all.sh; no code change needed (the port-forward will fail fast and give a clear error)

2. **Whether to add a post-forward readiness check before the first curl**
   - What we know: `sleep 2` works in practice (same pattern in kubectl.sh used by all tests)
   - What's unclear: Whether 2 seconds is always sufficient in slow cluster environments
   - Recommendation: Keep `sleep 2` (matches existing pattern); if it becomes a problem, a retry loop can be added in a follow-up

## Sources

### Primary (HIGH confidence)

- `tests/e2e/lib/oid_map.sh` — Inspected directly; all OID_MAP entries, TENANT_EVAL_COUNT, TENANT_RES_COUNT, VALID_TENANTS confirmed present
- `tests/e2e/lib/sim.sh` — Inspected directly; `sim_set_oid` and `sim_set_oid_stale` curl patterns confirmed
- `tests/e2e/lib/kubectl.sh` — Inspected directly; `start_port_forward`/`stop_port_forwards`/`PF_PIDS` pattern confirmed
- `tests/e2e/lib/common.sh` — Inspected directly; RED/NC color constants and stderr logging conventions confirmed
- `tests/e2e/run-all.sh` — Inspected directly; port 8080 for e2e-simulator confirmed; `trap cleanup EXIT` pattern confirmed
- `tests/e2e/scenarios/42-sns-02-stale-to-commands.sh` — Inspected directly; sim_set_oid / sim_set_oid_stale usage patterns confirmed
- `.planning/phases/83-command-interpreter/83-CONTEXT.md` — User decisions confirmed
- `.planning/phases/82-fixture-and-oid-mapping/82-02-SUMMARY.md` — Phase 82 deliverables confirmed: oid_map.sh exists and is ready

### Secondary (MEDIUM confidence)

- macOS bash 3 vs 4 issue: well-known ecosystem pattern; `#!/usr/bin/env bash` mitigation confirmed in all existing lib/ scripts

### Tertiary (LOW confidence)

- None — all findings are directly from codebase inspection

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all tools verified in existing scripts; no external dependencies
- Architecture patterns: HIGH — every pattern derived directly from codebase inspection of lib/ and scenarios/
- Pitfalls: HIGH — pitfalls derived from reading existing code and known bash gotchas; macOS bash is MEDIUM (common knowledge, not project-specific verification)

**Research date:** 2026-03-24
**Valid until:** 2026-04-24 (30 days — stable Bash/kubectl/curl; oid_map.sh stable unless new tenants added in a later phase)
