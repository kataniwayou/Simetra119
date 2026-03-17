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

for scenario in "$SCRIPT_DIR"/scenarios/[0-9]*.sh; do
    if [ -f "$scenario" ]; then
        log_info "Running: $(basename "$scenario")"
        source "$scenario"
        echo ""
    fi
done

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

[ "$FAIL_COUNT" -eq 0 ]
