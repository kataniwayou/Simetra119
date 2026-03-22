#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
# run-stage2.sh -- PSS Stage 2 Runner (PSS-INF-01 Stage Gating)
#
# Runs PSS scenarios in two gated stages:
#   Stage 1: Single Tenant Evaluation States (scenarios 53-58)
#   Gate:    Checks FAIL_COUNT after Stage 1; exits early if any Stage 1 failure
#   Stage 2: Two Tenant Independence (scenarios 59-61) -- only if Stage 1 passes
#
# Usage: bash tests/e2e/run-stage2.sh
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
    reset_oid_overrides || true
    stop_port_forwards
}
trap cleanup EXIT

# ---------------------------------------------------------------------------
# Banner
# ---------------------------------------------------------------------------

echo ""
echo "============================================="
echo "  PSS Stage 2 Runner"
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
# Stage 1: Single Tenant Evaluation States (scenarios 53-58)
# ---------------------------------------------------------------------------

echo "============================================="
echo "  Stage 1: Single Tenant Evaluation States"
echo "  (scenarios 53-58)"
echo "============================================="
echo ""

for scenario in \
    "$SCRIPT_DIR/scenarios/53-pss-01-not-ready.sh" \
    "$SCRIPT_DIR/scenarios/54-pss-02-stale-to-commands.sh" \
    "$SCRIPT_DIR/scenarios/55-pss-03-resolved.sh" \
    "$SCRIPT_DIR/scenarios/56-pss-04-unresolved.sh" \
    "$SCRIPT_DIR/scenarios/57-pss-05-healthy.sh" \
    "$SCRIPT_DIR/scenarios/58-pss-06-suppression.sh"; do
    if [ -f "$scenario" ]; then
        log_info "Running: $(basename "$scenario")"
        source "$scenario"
        echo ""
    fi
done

# ---------------------------------------------------------------------------
# Stage Gate: PSS-INF-01
# ---------------------------------------------------------------------------

if [ "$FAIL_COUNT" -gt 0 ]; then
    log_error "Stage 1 had $FAIL_COUNT failure(s) -- skipping Stage 2 scenarios"
    REPORT_FILE="$REPORT_DIR/e2e-pss-stage2-report-$(date '+%Y%m%d-%H%M%S').md"
    generate_report "$REPORT_FILE"
    log_info "Report saved to: $REPORT_FILE"
    print_summary
    exit 1
fi

log_info "Stage 1 passed -- proceeding to Stage 2 scenarios"
echo ""

# ---------------------------------------------------------------------------
# Stage 2: Two Tenant Independence (scenarios 59-61)
# ---------------------------------------------------------------------------

echo "============================================="
echo "  Stage 2: Two Tenant Independence"
echo "  (scenarios 59-61)"
echo "============================================="
echo ""

for scenario in \
    "$SCRIPT_DIR/scenarios/59-pss-11-t1-healthy-t2-unresolved.sh" \
    "$SCRIPT_DIR/scenarios/60-pss-12-t1-resolved-t2-healthy.sh" \
    "$SCRIPT_DIR/scenarios/61-pss-13-both-unresolved.sh"; do
    if [ -f "$scenario" ]; then
        log_info "Running: $(basename "$scenario")"
        source "$scenario"
        echo ""
    fi
done

# ---------------------------------------------------------------------------
# Report generation
# ---------------------------------------------------------------------------

REPORT_FILE="$REPORT_DIR/e2e-pss-stage2-report-$(date '+%Y%m%d-%H%M%S').md"
generate_report "$REPORT_FILE"
log_info "Report saved to: $REPORT_FILE"

# ---------------------------------------------------------------------------
# Summary and exit
# ---------------------------------------------------------------------------

print_summary

[ "$FAIL_COUNT" -eq 0 ]
