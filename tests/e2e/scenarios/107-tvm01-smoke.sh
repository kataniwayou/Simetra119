# Scenario 107: TVM-01 Smoke -- All 8 tenant metric instruments present with correct labels
# Uses tenant-cfg06-pss-single.yaml (e2e-pss-tenant at Priority=1, IntervalSeconds=1, GraceMultiplier=2.0)
#
# Purpose: Verify that all 8 v2.5 TenantMetricService OTel instruments are exported to Prometheus
# with tenant_id and priority labels. Also verifies stale path by triggering one stale OID and
# asserting tenant_metric_stale_percent is > 0. Confirms old v2.4 counters are absent.
#
# Sub-assertions:
#   TVM-01A: tenant_metric_stale_percent{tenant_id="e2e-pss-tenant"} present
#   TVM-01B: tenant_metric_resolved_percent{tenant_id="e2e-pss-tenant"} present
#   TVM-01C: tenant_metric_evaluate_percent{tenant_id="e2e-pss-tenant"} present
#   TVM-01D: tenant_command_dispatched_percent{tenant_id="e2e-pss-tenant"} present
#   TVM-01E: tenant_command_failed_percent{tenant_id="e2e-pss-tenant"} present
#   TVM-01F: tenant_command_suppressed_percent{tenant_id="e2e-pss-tenant"} present
#   TVM-01G: tenant_evaluation_state{tenant_id="e2e-pss-tenant"} present
#   TVM-01H: tenant_evaluation_duration_milliseconds_count{tenant_id="e2e-pss-tenant"} present
#   TVM-01I: priority label present and equals "1" on tenant_evaluation_state series
#   TVM-01J: tenant_metric_stale_percent > 0 after sim_set_oid_stale (TE2E-02 stale path)
#   TVM-01K: tenant_tier1_stale_total absent (v2.4 counter removed)
#   TVM-01L: tenant_tier2_resolved_total absent (v2.4 counter removed)
#   TVM-01M: tenant_tier3_evaluate_total absent (v2.4 counter removed)
#   TVM-01N: tenant_command_dispatched_total absent (v2.4 counter removed)
#   TVM-01O: tenant_command_failed_total absent (v2.4 counter removed)
#   TVM-01P: tenant_command_suppressed_total absent (v2.4 counter removed)

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

log_info "TVM-01: Asserting all 8 v2.5 tenant metric instruments present..."
_tvm01_assert_metric "tenant_metric_stale_percent"                       "A"
_tvm01_assert_metric "tenant_metric_resolved_percent"                    "B"
_tvm01_assert_metric "tenant_metric_evaluate_percent"                    "C"
_tvm01_assert_metric "tenant_command_dispatched_percent"                 "D"
_tvm01_assert_metric "tenant_command_failed_percent"                     "E"
_tvm01_assert_metric "tenant_command_suppressed_percent"                 "F"
_tvm01_assert_metric "tenant_evaluation_state"                           "G"
_tvm01_assert_metric "tenant_evaluation_duration_milliseconds_count"     "H"

# ---------------------------------------------------------------------------
# TVM-01I: priority label present and equals "1" on tenant_evaluation_state series
# ---------------------------------------------------------------------------

log_info "TVM-01: Checking priority label on tenant_evaluation_state series..."
PRIORITY=$(query_prometheus 'tenant_evaluation_state{tenant_id="e2e-pss-tenant"}' | jq -r '.data.result[0].metric.priority // ""')
if [ "$PRIORITY" = "1" ]; then
    record_pass "TVM-01I: priority label present and correct" "priority=${PRIORITY}"
else
    record_fail "TVM-01I: priority label present and correct" "priority=${PRIORITY} expected=1"
fi

# ---------------------------------------------------------------------------
# TVM-01J: stale_percent > 0 after stale OID (TE2E-02 stale path)
# Tenant is in Healthy state; setting one OID stale triggers tier=1 detection.
# Gauges are always present after first recording -- query directly (no poll_until needed).
# ---------------------------------------------------------------------------

log_info "TVM-01: Setting OID 5.1 to stale (NoSuchInstance)..."
sim_set_oid_stale "5.1"

log_info "TVM-01: Polling for tier=1 stale detection (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-tenant.*tier=1" 15; then
    log_info "TVM-01: tier=1 stale detection confirmed"
else
    log_warn "TVM-01: tier=1 log not detected within 30s; sleeping 10s as fallback"
    sleep 10
fi

# Query stale_percent gauge directly -- gauges always have a value after first recording.
# After a stale OID cycle, stale_percent should be non-zero.
VALUE=$(query_prometheus 'tenant_metric_stale_percent{tenant_id="e2e-pss-tenant"}' | jq -r '.data.result[0].value[1] // "0"')
log_info "TVM-01: tenant_metric_stale_percent=${VALUE}"

if echo "$VALUE" | awk '{exit ($1 > 0) ? 0 : 1}'; then
    record_pass "TVM-01J: tenant_metric_stale_percent > 0 after stale OID" "stale_percent=${VALUE}"
else
    record_fail "TVM-01J: tenant_metric_stale_percent > 0 after stale OID" "stale_percent=${VALUE} expected>0"
fi

# ---------------------------------------------------------------------------
# TVM-01K through TVM-01P: Assert old v2.4 counter names are ABSENT
# ---------------------------------------------------------------------------

_tvm01_assert_absent() {
    local metric="$1"
    local sub_id="$2"
    local count
    count=$(query_prometheus "{__name__=\"${metric}\",tenant_id=\"e2e-pss-tenant\"}" | jq -r '.data.result | length')
    if [ "$count" -eq 0 ]; then
        record_pass "TVM-01${sub_id}: ${metric} absent (v2.4 counter removed)" "series_count=0"
    else
        record_fail "TVM-01${sub_id}: ${metric} absent (v2.4 counter removed)" "metric=${metric} series_count=${count} expected=0"
    fi
}

log_info "TVM-01: Asserting v2.4 counter names absent..."
_tvm01_assert_absent "tenant_tier1_stale_total"         "K"
_tvm01_assert_absent "tenant_tier2_resolved_total"      "L"
_tvm01_assert_absent "tenant_tier3_evaluate_total"      "M"
_tvm01_assert_absent "tenant_command_dispatched_total"  "N"
_tvm01_assert_absent "tenant_command_failed_total"      "O"
_tvm01_assert_absent "tenant_command_suppressed_total"  "P"

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
