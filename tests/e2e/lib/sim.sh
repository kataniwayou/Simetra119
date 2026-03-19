#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
# sim.sh -- E2E simulator HTTP control utilities
#
# Requires port-forward to e2e-simulator:8080 to be active.
# Source this file to access sim_set_scenario, reset_scenario,
# get_active_scenario, and poll_until_log.
# ============================================================================

# Global simulator URL
SIM_URL="http://localhost:8080"

# ---------------------------------------------------------------------------
# sim_set_scenario <name>
# POST to /scenario/{name}. Returns 1 on curl failure or non-200 response.
# ---------------------------------------------------------------------------

sim_set_scenario() {
    local name="$1"
    log_info "Setting simulator scenario: ${name}"
    local http_code
    http_code=$(curl -sf -o /dev/null -w '%{http_code}' \
        -X POST "${SIM_URL}/scenario/${name}" 2>/dev/null) || {
        log_error "curl failed setting scenario ${name}"
        return 1
    }
    if [ "$http_code" = "200" ]; then
        log_info "Scenario active: ${name}"
    else
        log_error "Unexpected HTTP ${http_code} setting scenario ${name}"
        return 1
    fi
}

# ---------------------------------------------------------------------------
# reset_scenario
# Convenience wrapper: resets simulator to the default scenario.
# Call at the start of every snapshot scenario (belt-and-suspenders reset).
# ---------------------------------------------------------------------------

reset_scenario() {
    reset_oid_overrides
    sim_set_scenario default
}

# ---------------------------------------------------------------------------
# sim_set_oid <oid_suffix> <value>
# POST to /oid/{oid}/{value}. oid_suffix is relative to E2E prefix,
# e.g. "4.1" for T1 evaluate, "5.1" for T2 evaluate.
# ---------------------------------------------------------------------------

sim_set_oid() {
    local oid="$1"
    local value="$2"
    log_info "Setting OID ${oid} = ${value}"
    local http_code
    http_code=$(curl -sf -o /dev/null -w '%{http_code}' \
        -X POST "${SIM_URL}/oid/${oid}/${value}" 2>/dev/null) || {
        log_error "curl failed setting OID ${oid}"
        return 1
    }
    if [ "$http_code" = "200" ]; then
        return 0
    else
        log_error "Unexpected HTTP ${http_code} setting OID ${oid}"
        return 1
    fi
}

# ---------------------------------------------------------------------------
# sim_set_oid_stale <oid_suffix>
# POST to /oid/{oid}/stale. Makes the OID return NoSuchInstance.
# ---------------------------------------------------------------------------

sim_set_oid_stale() {
    local oid="$1"
    log_info "Setting OID ${oid} to stale"
    local http_code
    http_code=$(curl -sf -o /dev/null -w '%{http_code}' \
        -X POST "${SIM_URL}/oid/${oid}/stale" 2>/dev/null) || {
        log_error "curl failed setting OID ${oid} stale"
        return 1
    }
    if [ "$http_code" = "200" ]; then
        return 0
    else
        log_error "Unexpected HTTP ${http_code} setting OID ${oid} stale"
        return 1
    fi
}

# ---------------------------------------------------------------------------
# reset_oid_overrides
# DELETE /oid/overrides -- clear all per-OID overrides, fall back to scenario.
# ---------------------------------------------------------------------------

reset_oid_overrides() {
    curl -sf -o /dev/null -X DELETE "${SIM_URL}/oid/overrides" 2>/dev/null || true
}

# ---------------------------------------------------------------------------
# get_active_scenario
# GET /scenario. Returns the current scenario name via stdout.
# Returns 1 on failure.
# ---------------------------------------------------------------------------

get_active_scenario() {
    local response
    response=$(curl -sf "${SIM_URL}/scenario" 2>/dev/null) || {
        log_error "curl failed getting active scenario"
        return 1
    }
    echo "$response" | jq -r '.scenario'
}

# ---------------------------------------------------------------------------
# poll_until_log <timeout_s> <interval_s> <grep_pattern> [since_seconds]
# Search all snmp-collector replica pods for a log pattern.
# Returns 0 on first match (any pod), 1 on timeout.
# Default since_seconds: 60
# ---------------------------------------------------------------------------

poll_until_log() {
    local timeout="$1"
    local interval="$2"
    local pattern="$3"
    local since="${4:-60}"

    local deadline
    deadline=$(( $(date +%s) + timeout ))

    local PODS
    PODS=$(kubectl get pods -n simetra -l app=snmp-collector \
        -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true

    while [ "$(date +%s)" -lt "$deadline" ]; do
        for POD in $PODS; do
            local logs
            logs=$(kubectl logs "$POD" -n simetra --since="${since}s" 2>/dev/null) || true
            # Use grep > /dev/null 2>&1 (not grep -q) to avoid SIGPIPE under pipefail
            if echo "$logs" | grep "${pattern}" > /dev/null 2>&1; then
                return 0
            fi
        done
        sleep "$interval"
    done
    return 1
}
