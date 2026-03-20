#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
# run-stage3.sh -- PSS Stage 3 Runner (PSS-INF-01 Stage Gating)
#
# Runs PSS scenarios in three gated stages:
#   Stage 1: Single Tenant Evaluation States (scenarios 53-58)
#   Gate:    Checks FAIL_COUNT after Stage 1; exits early if any Stage 1 failure
#   Stage 2: Two Tenant Independence (scenarios 59-61) -- only if Stage 1 passes
#   Gate:    Checks FAIL_COUNT after Stage 2; exits early if any Stage 2 failure
#   Stage 3: Advance Gate Logic (scenarios 62-68) -- only if Stage 2 passes
#
# Stage 3 fixture lifecycle is managed by this runner (not individual scenarios):
#   - Applies tenant-cfg08-pss-four-tenant.yaml before Stage 3 scenarios
#   - Primes all 12 OIDs to healthy state
#   - Restores original tenant ConfigMap after Stage 3 scenarios complete
#
# Usage: bash tests/e2e/run-stage3.sh
# ============================================================================

# ---------------------------------------------------------------------------
# Directory setup
# ---------------------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FIXTURES_DIR="$SCRIPT_DIR/fixtures"
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

_STAGE3_CONFIGMAP_SAVED=false

cleanup() {
    log_info "Cleaning up..."
    if [ "$_STAGE3_CONFIGMAP_SAVED" = "true" ]; then
        restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true
    fi
    reset_oid_overrides || true
    stop_port_forwards
}
trap cleanup EXIT

# ---------------------------------------------------------------------------
# Banner
# ---------------------------------------------------------------------------

echo ""
echo "============================================="
echo "  PSS Stage 3 Runner"
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
# Stage Gate 1: PSS-INF-01
# ---------------------------------------------------------------------------

if [ "$FAIL_COUNT" -gt 0 ]; then
    log_error "Stage 1 had $FAIL_COUNT failure(s) -- skipping Stage 2 scenarios"
    REPORT_FILE="$REPORT_DIR/e2e-pss-stage3-report-$(date '+%Y%m%d-%H%M%S').md"
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
# Stage Gate 2: PSS-INF-01
# ---------------------------------------------------------------------------

if [ "$FAIL_COUNT" -gt 0 ]; then
    log_error "Stage 2 had $FAIL_COUNT failure(s) -- skipping Stage 3 scenarios"
    REPORT_FILE="$REPORT_DIR/e2e-pss-stage3-report-$(date '+%Y%m%d-%H%M%S').md"
    generate_report "$REPORT_FILE"
    log_info "Report saved to: $REPORT_FILE"
    print_summary
    exit 1
fi

log_info "Stage 2 passed -- proceeding to Stage 3 scenarios"
echo ""

# ---------------------------------------------------------------------------
# Stage 3 Setup: Apply 4-tenant PSS fixture, prime all tenants to healthy
# ---------------------------------------------------------------------------

echo "============================================="
echo "  Stage 3: Advance Gate Logic"
echo "  (scenarios 62-68)"
echo "============================================="
echo ""

log_info "Stage 3 setup: saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true
_STAGE3_CONFIGMAP_SAVED=true

log_info "Stage 3 setup: applying 4-tenant PSS fixture..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg08-pss-four-tenant.yaml" > /dev/null 2>&1 || true

log_info "Stage 3 setup: waiting for tenant vector reload..."
poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30

log_info "Stage 3 setup: priming all 4 tenants to healthy state..."
# G1-T1: OIDs 4.x (e2e_port_utilization eval, e2e_channel_state res1, e2e_bypass_status res2)
sim_set_oid "4.1" "10"
sim_set_oid "4.2" "1"
sim_set_oid "4.3" "1"
# G1-T2: OIDs 5.x (e2e_eval_T2 eval, e2e_res1_T2 res1, e2e_res2_T2 res2)
sim_set_oid "5.1" "10"
sim_set_oid "5.2" "1"
sim_set_oid "5.3" "1"
# G2-T3: OIDs 6.x (e2e_eval_T3 eval, e2e_res1_T3 res1, e2e_res2_T3 res2)
sim_set_oid "6.1" "10"
sim_set_oid "6.2" "1"
sim_set_oid "6.3" "1"
# G2-T4: OIDs 7.x (e2e_eval_T4 eval, e2e_res1_T4 res1, e2e_res2_T4 res2)
sim_set_oid "7.1" "10"
sim_set_oid "7.2" "1"
sim_set_oid "7.3" "1"

log_info "Stage 3 setup: sleeping 8s for readiness grace (TSS=3 x interval=1s x GM=2.0 = 6s + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Stage 3 scenarios: Advance Gate Logic (scenarios 62-68)
# ---------------------------------------------------------------------------

for scenario in \
    "$SCRIPT_DIR/scenarios/62-pss-14-all-g1-resolved.sh" \
    "$SCRIPT_DIR/scenarios/63-pss-15-all-g1-healthy.sh" \
    "$SCRIPT_DIR/scenarios/64-pss-16-g1-mixed-pass.sh" \
    "$SCRIPT_DIR/scenarios/65-pss-17-all-g1-unresolved.sh" \
    "$SCRIPT_DIR/scenarios/66-pss-18-g1-resolved-unresolved.sh" \
    "$SCRIPT_DIR/scenarios/67-pss-19-g1-healthy-unresolved.sh" \
    "$SCRIPT_DIR/scenarios/68-pss-20-all-g1-not-ready.sh"; do
    if [ -f "$scenario" ]; then
        log_info "Running: $(basename "$scenario")"
        source "$scenario"
        echo ""
    fi
done

# ---------------------------------------------------------------------------
# Stage 3 Cleanup: Reset OID overrides and restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "Stage 3 cleanup: resetting OID overrides..."
reset_oid_overrides || true

log_info "Stage 3 cleanup: restoring original tenant ConfigMap..."
restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true
_STAGE3_CONFIGMAP_SAVED=false

# ---------------------------------------------------------------------------
# Report generation
# ---------------------------------------------------------------------------

REPORT_FILE="$REPORT_DIR/e2e-pss-stage3-report-$(date '+%Y%m%d-%H%M%S').md"
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

# Stage indices (0-based in SCENARIO_RESULTS):
#   Stage 1: indices 0-5   (scenarios 53-58, 6 scenarios)
#   Stage 2: indices 6-8   (scenarios 59-61, 3 scenarios)
#   Stage 3: indices 9-15  (scenarios 62-68, 7 scenarios)

_total_results=${#SCENARIO_RESULTS[@]}

_count_stage() {
    local start="$1"
    local end="$2"
    local pass=0
    local fail=0
    for i in $(seq "$start" "$end"); do
        if [ "$i" -ge "$_total_results" ]; then
            break
        fi
        local entry="${SCENARIO_RESULTS[$i]:-}"
        if [ -z "$entry" ]; then
            continue
        fi
        local status="${entry%%|*}"
        if [ "$status" = "PASS" ]; then
            pass=$((pass + 1))
        elif [ "$status" = "FAIL" ]; then
            fail=$((fail + 1))
        fi
    done
    echo "$pass $fail"
}

read -r _s1_pass _s1_fail <<< "$(_count_stage 0 5)"
read -r _s2_pass _s2_fail <<< "$(_count_stage 6 8)"
read -r _s3_pass _s3_fail <<< "$(_count_stage 9 15)"

printf "  Stage 1 (53-58 Single Tenant):      PASS=%-3s FAIL=%s\n" "$_s1_pass" "$_s1_fail"
printf "  Stage 2 (59-61 Two Tenant):         PASS=%-3s FAIL=%s\n" "$_s2_pass" "$_s2_fail"
printf "  Stage 3 (62-68 Advance Gate):       PASS=%-3s FAIL=%s\n" "$_s3_pass" "$_s3_fail"
echo ""

[ "$FAIL_COUNT" -eq 0 ]
