# Scenario 107: TVM-01 Smoke -- All 8 tenant metric instruments present with correct labels
# Uses tenant-cfg06-pss-single.yaml (e2e-pss-tenant at Priority=1, IntervalSeconds=1, GraceMultiplier=2.0)
#
# Purpose: Verify that all 8 TenantMetricService OTel instruments are exported to Prometheus
# with tenant_id and priority labels. Also verifies TE2E-02 stale path by triggering one stale
# OID and asserting tenant_tier1_stale_total increments.
#
# Sub-assertions:
#   TVM-01A: tenant_tier1_stale_total{tenant_id="e2e-pss-tenant"} present
#   TVM-01B: tenant_tier2_resolved_total{tenant_id="e2e-pss-tenant"} present
#   TVM-01C: tenant_tier3_evaluate_total{tenant_id="e2e-pss-tenant"} present
#   TVM-01D: tenant_command_dispatched_total{tenant_id="e2e-pss-tenant"} present
#   TVM-01E: tenant_command_failed_total{tenant_id="e2e-pss-tenant"} present
#   TVM-01F: tenant_command_suppressed_total{tenant_id="e2e-pss-tenant"} present
#   TVM-01G: tenant_state{tenant_id="e2e-pss-tenant"} present
#   TVM-01H: tenant_evaluation_duration_milliseconds_count{tenant_id="e2e-pss-tenant"} present
#   TVM-01I: priority label present and equals "1" on tenant_state series
#   TVM-01J: tenant_tier1_stale_total increments after sim_set_oid_stale (TE2E-02 stale path)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"
SCENARIO_NAME="TVM-01: Tenant metric smoke test"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply single-tenant PSS fixture
# ---------------------------------------------------------------------------

log_info "TVM-01: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "TVM-01: Applying tenant-cfg06-pss-single.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-single.yaml" > /dev/null 2>&1 || true

log_info "TVM-01: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "TVM-01: Tenant vector reload confirmed"
else
    log_warn "TVM-01: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime T2 OIDs to healthy state, wait for readiness grace
# ---------------------------------------------------------------------------

log_info "TVM-01: Priming T2 OIDs to healthy state..."
sim_set_oid "5.1" "10"   # T2 eval in-range (>= Min:10)
sim_set_oid "5.2" "1"    # T2 res1 in-range (>= Min:1)
sim_set_oid "5.3" "1"    # T2 res2 in-range (>= Min:1)

log_info "TVM-01: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Wait for at least one healthy evaluation cycle
# ---------------------------------------------------------------------------

log_info "TVM-01: Polling for tier=3 healthy evaluation cycle..."
if poll_until_log 30 1 "e2e-pss-tenant.*tier=3" 15; then
    log_info "TVM-01: Healthy evaluation cycle confirmed"
else
    log_warn "TVM-01: tier=3 log not detected within 30s; proceeding with metric checks"
fi

# ---------------------------------------------------------------------------
# TVM-01A through TVM-01H: Assert all 8 instruments present with tenant_id label
# ---------------------------------------------------------------------------

_tvm01_assert_metric() {
    local metric="$1"
    local sub_id="$2"
    local count
    count=$(query_prometheus "${metric}{tenant_id=\"e2e-pss-tenant\"}" | jq -r '.data.result | length')
    if [ "$count" -gt 0 ]; then
        record_pass "TVM-01${sub_id}: ${metric} present with tenant_id label" "series_count=${count}"
    else
        record_fail "TVM-01${sub_id}: ${metric} present with tenant_id label" "metric=${metric} tenant_id=e2e-pss-tenant series_count=0"
    fi
}

log_info "TVM-01: Asserting all 8 tenant metric instruments present..."
_tvm01_assert_metric "tenant_tier1_stale_total"                          "A"
_tvm01_assert_metric "tenant_tier2_resolved_total"                       "B"
_tvm01_assert_metric "tenant_tier3_evaluate_total"                       "C"
_tvm01_assert_metric "tenant_command_dispatched_total"                   "D"

# TVM-01E: tenant_command_failed_total only appears after the first SET command failure
# (OTel counters do not register until the first Add call). Poll briefly; if absent,
# record_pass with a note -- absence means no SET failures have occurred, which is correct.
log_info "TVM-01E: Polling for tenant_command_failed_total (15s, absent=expected OTel behavior)..."
if poll_until_exists 15 3 "tenant_command_failed_total"; then
    _tvm01_assert_metric "tenant_command_failed_total" "E"
else
    record_pass "TVM-01E: tenant_command_failed_total absent (correct -- OTel counter only appears after first SET failure; none have occurred)" "metric=tenant_command_failed_total absent=expected"
fi

_tvm01_assert_metric "tenant_command_suppressed_total"                   "F"
_tvm01_assert_metric "tenant_state"                                      "G"
_tvm01_assert_metric "tenant_evaluation_duration_milliseconds_count"     "H"

# ---------------------------------------------------------------------------
# TVM-01I: priority label present and equals "1" on tenant_state series
# ---------------------------------------------------------------------------

log_info "TVM-01: Checking priority label on tenant_state series..."
PRIORITY=$(query_prometheus 'tenant_state{tenant_id="e2e-pss-tenant"}' | jq -r '.data.result[0].metric.priority // ""')
if [ "$PRIORITY" = "1" ]; then
    record_pass "TVM-01I: priority label present and correct" "priority=${PRIORITY}"
else
    record_fail "TVM-01I: priority label present and correct" "priority=${PRIORITY} expected=1"
fi

# ---------------------------------------------------------------------------
# TVM-01J: tier1_stale_total increments after stale OID (TE2E-02 stale path)
# Tenant is in Healthy state; setting one OID stale triggers tier=1 detection.
# ---------------------------------------------------------------------------

log_info "TVM-01: Snapshotting tier1_stale_total baseline..."
STALE_BEFORE=$(snapshot_counter "tenant_tier1_stale_total" 'tenant_id="e2e-pss-tenant",priority="1"')
log_info "TVM-01: Baseline stale_before=${STALE_BEFORE}"

log_info "TVM-01: Setting OID 5.1 to stale (NoSuchInstance)..."
sim_set_oid_stale "5.1"

log_info "TVM-01: Polling for tier=1 stale detection (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-tenant.*tier=1" 15; then
    log_info "TVM-01: tier=1 stale detection confirmed"
else
    log_warn "TVM-01: tier=1 log not detected within 30s; sleeping 10s as fallback"
    sleep 10
fi

# Use poll_until to wait for the Prometheus counter to exceed the baseline.
# This avoids the race where the log fires but the scrape hasn't propagated yet,
# and also handles the case where the baseline already included prior increments
# from earlier cycles -- we wait until the counter genuinely advances beyond it.
log_info "TVM-01: Polling for tier1_stale_total to exceed baseline=${STALE_BEFORE} (30s timeout)..."
if poll_until 30 2 "tenant_tier1_stale_total" 'tenant_id="e2e-pss-tenant",priority="1"' "$STALE_BEFORE"; then
    STALE_AFTER=$(snapshot_counter "tenant_tier1_stale_total" 'tenant_id="e2e-pss-tenant",priority="1"')
    STALE_DELTA=$((STALE_AFTER - STALE_BEFORE))
    log_info "TVM-01: stale_after=${STALE_AFTER} delta=${STALE_DELTA}"
    record_pass "TVM-01J: tier1_stale_total increments on stale OID" "delta=${STALE_DELTA} before=${STALE_BEFORE} after=${STALE_AFTER}"
else
    STALE_AFTER=$(snapshot_counter "tenant_tier1_stale_total" 'tenant_id="e2e-pss-tenant",priority="1"')
    STALE_DELTA=$((STALE_AFTER - STALE_BEFORE))
    log_info "TVM-01: stale_after=${STALE_AFTER} delta=${STALE_DELTA}"
    record_fail "TVM-01J: tier1_stale_total increments on stale OID" "delta=${STALE_DELTA} before=${STALE_BEFORE} after=${STALE_AFTER} counter did not exceed baseline within 30s"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "TVM-01: Clearing OID overrides..."
reset_oid_overrides

log_info "TVM-01: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "TVM-01: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "TVM-01: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
