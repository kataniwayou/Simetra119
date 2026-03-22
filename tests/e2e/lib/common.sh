#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
# common.sh -- Shared utilities for E2E pipeline counter verification
# ============================================================================

# Color constants
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

# Global tracking
SCENARIO_RESULTS=()
SCENARIO_EVIDENCE=()
PASS_COUNT=0
FAIL_COUNT=0

# ---------------------------------------------------------------------------
# Logging
# ---------------------------------------------------------------------------

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# ---------------------------------------------------------------------------
# Result tracking
# ---------------------------------------------------------------------------

record_pass() {
    local scenario_name="$1"
    local evidence="${2:-}"
    SCENARIO_RESULTS+=("PASS|${scenario_name}")
    SCENARIO_EVIDENCE+=("${scenario_name}|${evidence}")
    PASS_COUNT=$((PASS_COUNT + 1))
    echo -e "${GREEN}PASS${NC}: ${scenario_name}"
}

record_fail() {
    local scenario_name="$1"
    local evidence="${2:-}"
    SCENARIO_RESULTS+=("FAIL|${scenario_name}")
    SCENARIO_EVIDENCE+=("${scenario_name}|${evidence}")
    FAIL_COUNT=$((FAIL_COUNT + 1))
    echo -e "${RED}FAIL${NC}: ${scenario_name}"
}

# ---------------------------------------------------------------------------
# Assertions
# ---------------------------------------------------------------------------

assert_delta_gt() {
    local delta="$1"
    local threshold="$2"
    local scenario_name="$3"
    local evidence="${4:-}"

    if [ "$delta" -gt "$threshold" ]; then
        record_pass "$scenario_name" "delta=${delta} > threshold=${threshold}. ${evidence}"
    else
        record_fail "$scenario_name" "delta=${delta} <= threshold=${threshold}. ${evidence}"
    fi
}

assert_exists() {
    local metric_name="$1"
    local scenario_name="$2"
    local evidence="${3:-}"

    local result
    result=$(query_prometheus "{__name__=\"${metric_name}\"}")
    local count
    count=$(echo "$result" | jq -r '.data.result | length')

    if [ "$count" -gt 0 ]; then
        record_pass "$scenario_name" "metric ${metric_name} exists (${count} series). ${evidence}"
    else
        record_fail "$scenario_name" "metric ${metric_name} not found. ${evidence}"
    fi
}

assert_delta_eq() {
    local delta="$1"
    local expected="$2"
    local scenario_name="$3"
    local evidence="${4:-}"

    if [ "$delta" -eq "$expected" ]; then
        record_pass "$scenario_name" "delta=${delta} == expected=${expected}. ${evidence}"
    else
        record_fail "$scenario_name" "delta=${delta} != expected=${expected}. ${evidence}"
    fi
}

assert_delta_ge() {
    local delta="$1"
    local minimum="$2"
    local scenario_name="$3"
    local evidence="${4:-}"

    if [ "$delta" -ge "$minimum" ]; then
        record_pass "$scenario_name" "delta=${delta} >= minimum=${minimum}. ${evidence}"
    else
        record_fail "$scenario_name" "delta=${delta} < minimum=${minimum}. ${evidence}"
    fi
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

print_summary() {
    echo ""
    echo "=== Test Summary ==="
    echo "Total: $((PASS_COUNT + FAIL_COUNT))  Pass: ${PASS_COUNT}  Fail: ${FAIL_COUNT}"

    if [ "$FAIL_COUNT" -gt 0 ]; then
        echo ""
        echo "Failures:"
        for result in "${SCENARIO_RESULTS[@]}"; do
            local status="${result%%|*}"
            local name="${result#*|}"
            if [ "$status" = "FAIL" ]; then
                echo "  - ${name}"
            fi
        done
    fi
    echo "===================="
}
