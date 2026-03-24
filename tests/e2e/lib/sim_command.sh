#!/usr/bin/env bash
# sim_command.sh — Phase 83 Command Interpreter
# Usage: ./sim_command.sh [-v] {Tenant}-{V/S}-{#}E-{#}R
#
# Translates a terse tenant simulation pattern into simulator HTTP API calls.
# Supports V (violate) and S (stale) modes for Evaluate and Resolved metric slots.
# Non-affected slots are always explicitly reset to their healthy value (CMD-07).
#
# Examples:
#   ./sim_command.sh T1_P1-V-2E-1R       # violate 2E and 1R, reset remaining slots
#   ./sim_command.sh T1_P1-S-1E-0R       # stale 1E, reset all others to healthy
#   ./sim_command.sh T1_P1-V-0E-0R       # reset all slots to healthy
#   ./sim_command.sh -v T2_P1-V-4E-4R    # verbose — prints each POST call to stderr
#
# Note: conflicts with run-all.sh if both are running concurrently (both use port 8080).

set -euo pipefail

# Bash 4+ required for associative arrays (oid_map.sh uses declare -A)
if [ "${BASH_VERSINFO[0]}" -lt 4 ]; then
    echo "ERROR: bash 4+ required (found ${BASH_VERSION})" >&2
    exit 1
fi

# ---------------------------------------------------------------------------
# Source OID map
# ---------------------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${SCRIPT_DIR}/oid_map.sh"

# Sanity check: verify oid_map.sh loaded correctly
if [ -z "${OID_MAP[T1_P1.E.1.oid]+x}" ]; then
    echo "ERROR: oid_map.sh did not load correctly — OID_MAP[T1_P1.E.1.oid] is missing" >&2
    exit 1
fi

# ---------------------------------------------------------------------------
# Constants and helpers
# ---------------------------------------------------------------------------

SIM_URL="http://localhost:8080"
RED='\033[0;31m'
NC='\033[0m'

error() {
    echo -e "${RED}ERROR: $*${NC}" >&2
    exit 1
}

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

# 1. Tenant validation
valid=0
for t in $VALID_TENANTS; do
    [ "$t" = "$TENANT" ] && valid=1 && break
done
[ "$valid" -eq 1 ] || error "Unknown tenant '${TENANT}'. Valid: ${VALID_TENANTS}"

# 2. Count validation
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

sim_stale() {
    local oid="$1"
    [ "$VERBOSE" -eq 1 ] && echo "[verbose] POST ${SIM_URL}/oid/${oid}/stale" >&2
    local http_code
    http_code=$(curl -sf -o /dev/null -w '%{http_code}' \
        -X POST "${SIM_URL}/oid/${oid}/stale" 2>/dev/null) \
        || error "curl failed setting OID ${oid} stale"
    [ "$http_code" = "200" ] || error "HTTP ${http_code} setting OID ${oid} stale"
}

# ---------------------------------------------------------------------------
# Translation loop
# ---------------------------------------------------------------------------

apply_role() {
    local role="$1" count="$2" total="$3"
    local i oid
    for i in $(seq 1 "$total"); do
        oid="${OID_MAP[${TENANT}.${role}.${i}.oid]}"
        if [ "$i" -le "$count" ]; then
            if [ "$MODE" = "V" ]; then
                sim_post "$oid" "${OID_MAP[${TENANT}.${role}.${i}.violated]}"
            else
                # S mode: stale the affected slots
                sim_stale "$oid"
            fi
        else
            # Non-affected slots always reset to healthy (CMD-07)
            sim_post "$oid" "${OID_MAP[${TENANT}.${role}.${i}.healthy]}"
        fi
    done
}

# ---------------------------------------------------------------------------
# Main execution
# ---------------------------------------------------------------------------

apply_role "E" "$E_COUNT" "$e_total"
apply_role "R" "$R_COUNT" "$r_total"

# Silent on success (silence = success convention)
