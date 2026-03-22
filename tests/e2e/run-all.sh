#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
# run-all.sh -- E2E System Verification Test Runner
#
# Single entry point: sources lib/ modules, runs pre-flight checks, manages
# port-forwards, executes scenario scripts sequentially, generates report.
#
# Usage: bash tests/e2e/run-all.sh
# ============================================================================

# ---------------------------------------------------------------------------
# Directory setup
# ---------------------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPORT_DIR="$SCRIPT_DIR/reports"
mkdir -p "$REPORT_DIR"

# ---------------------------------------------------------------------------
# Source libraries
# ---------------------------------------------------------------------------

source "$SCRIPT_DIR/lib/common.sh"
source "$SCRIPT_DIR/lib/prometheus.sh"
source "$SCRIPT_DIR/lib/kubectl.sh"
source "$SCRIPT_DIR/lib/report.sh"
source "$SCRIPT_DIR/lib/sim.sh"

# ---------------------------------------------------------------------------
# Cleanup trap
# ---------------------------------------------------------------------------

cleanup() {
    log_info "Cleaning up..."
    stop_port_forwards
}
trap cleanup EXIT

# ---------------------------------------------------------------------------
# Banner
# ---------------------------------------------------------------------------

echo ""
echo "============================================="
echo "  E2E System Verification"
echo "  $(date -u '+%Y-%m-%d %H:%M:%S UTC')"
echo "============================================="
echo ""

# ---------------------------------------------------------------------------
# Start port-forwards
# ---------------------------------------------------------------------------

start_port_forward prometheus 9090 9090
start_port_forward e2e-simulator 8080 8080

# ---------------------------------------------------------------------------
# Pre-flight checks
# ---------------------------------------------------------------------------

log_info "Pre-flight: checking snmp-collector pods..."
if ! check_pods_ready; then
    log_error "Pre-flight FAILED: not all pods running"
    exit 1
fi

log_info "Pre-flight: checking Prometheus..."
if ! check_prometheus_reachable; then
    log_error "Pre-flight FAILED: Prometheus not reachable at localhost:9090"
    exit 1
fi

log_info "Pre-flight checks passed"
echo ""

# ---------------------------------------------------------------------------
# Scenario execution
# ---------------------------------------------------------------------------

echo "============================================="
echo "  Running scenarios"
echo "============================================="
echo ""

while IFS= read -r scenario; do
    if [ -f "$scenario" ]; then
        log_info "Running: $(basename "$scenario")"
        source "$scenario"
        echo ""
    fi
done < <(ls -1 "$SCRIPT_DIR"/scenarios/[0-9]*.sh 2>/dev/null | sort -V)

# ---------------------------------------------------------------------------
# Report generation
# ---------------------------------------------------------------------------

REPORT_FILE="$REPORT_DIR/e2e-report-$(date '+%Y%m%d-%H%M%S').md"
generate_report "$REPORT_FILE"
log_info "Report saved to: $REPORT_FILE"

# ---------------------------------------------------------------------------
# Summary and exit
# ---------------------------------------------------------------------------

print_summary

# ---------------------------------------------------------------------------
# Cross-Stage PSS Summary
# ---------------------------------------------------------------------------

echo ""
echo "============================================="
echo "  Cross-Stage PSS Summary"
echo "============================================="
echo ""

# PSS stage indices (0-based in SCENARIO_RESULTS):
#   Stage 1: indices 52-57  (scenarios 53-58, 6 scenarios)
#   Stage 2: indices 58-60  (scenarios 59-61, 3 scenarios)
#   Stage 3: indices 61-67  (scenarios 62-68, 7 scenarios)

_pss_total_results=${#SCENARIO_RESULTS[@]}

_pss_count_stage() {
    local start="$1"
    local end="$2"
    local pass=0
    local fail=0
    local ran=0
    for i in $(seq "$start" "$end"); do
        if [ "$i" -ge "$_pss_total_results" ]; then
            break
        fi
        local entry="${SCENARIO_RESULTS[$i]:-}"
        if [ -z "$entry" ]; then
            continue
        fi
        ran=$((ran + 1))
        local status="${entry%%|*}"
        if [ "$status" = "PASS" ]; then
            pass=$((pass + 1))
        elif [ "$status" = "FAIL" ]; then
            fail=$((fail + 1))
        fi
    done
    echo "$pass $fail $ran"
}

read -r _pss_s1_pass _pss_s1_fail _pss_s1_ran <<< "$(_pss_count_stage 52 57)"
read -r _pss_s2_pass _pss_s2_fail _pss_s2_ran <<< "$(_pss_count_stage 58 60)"
read -r _pss_s3_pass _pss_s3_fail _pss_s3_ran <<< "$(_pss_count_stage 61 67)"

if [ "$_pss_s1_ran" -gt 0 ]; then
    printf "  Stage 1 (53-58 Single Tenant):      PASS=%-3s FAIL=%s\n" "$_pss_s1_pass" "$_pss_s1_fail"
fi
if [ "$_pss_s2_ran" -gt 0 ]; then
    printf "  Stage 2 (59-61 Two Tenant):         PASS=%-3s FAIL=%s\n" "$_pss_s2_pass" "$_pss_s2_fail"
fi
if [ "$_pss_s3_ran" -gt 0 ]; then
    printf "  Stage 3 (62-68 Advance Gate):       PASS=%-3s FAIL=%s\n" "$_pss_s3_pass" "$_pss_s3_fail"
fi
if [ "$_pss_s1_ran" -eq 0 ] && [ "$_pss_s2_ran" -eq 0 ] && [ "$_pss_s3_ran" -eq 0 ]; then
    echo "  No PSS scenarios ran."
fi
echo ""

[ "$FAIL_COUNT" -eq 0 ]
