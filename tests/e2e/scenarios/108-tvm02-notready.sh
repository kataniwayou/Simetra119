# Scenario 108: TVM-02 NotReady -- tenant_state=0 during readiness grace window
# Uses tenant-cfg06-pss-single.yaml (e2e-pss-tenant at Priority=1, IntervalSeconds=1, GraceMultiplier=2.0)
#
# Design: NotReady is triggered by applying the fixture WITHOUT priming OIDs and
# querying immediately. The tenant has no poll data yet -- SnapshotJob returns
# TenantState.NotReady (0) on all evaluation cycles during the grace window.
#
# NotReady path records ONLY state gauge + duration (no tier or command counters).
#
# Sub-assertions:
#   TVM-02A: tenant_state = 0 (NotReady) during grace window
#   TVM-02B: tenant_evaluation_duration_milliseconds_count > 0 (duration recorded for NotReady)
#   TVM-02C: tenant_tier1_stale_total delta == 0 (tier counters do NOT increment)
#   TVM-02D: tenant_tier2_resolved_total delta == 0
#   TVM-02E: tenant_tier3_evaluate_total delta == 0

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"
SCENARIO_NAME="TVM-02: NotReady evaluation path"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply single-tenant PSS fixture
# ---------------------------------------------------------------------------

log_info "TVM-02: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "TVM-02: Applying tenant-cfg06-pss-single.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-single.yaml" > /dev/null 2>&1 || true

log_info "TVM-02: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "TVM-02: Tenant vector reload confirmed"
else
    log_warn "TVM-02: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Do NOT prime OIDs -- tenant must remain in grace window (NotReady)
# Snapshot tier counter baselines immediately after reload (before evaluation)
# ---------------------------------------------------------------------------

log_info "TVM-02: Snapshotting tier counter baselines (pre-evaluation, NotReady window)..."
TIER1_BEFORE=$(snapshot_counter "tenant_tier1_stale_total"    'tenant_id="e2e-pss-tenant",priority="1"')
TIER2_BEFORE=$(snapshot_counter "tenant_tier2_resolved_total" 'tenant_id="e2e-pss-tenant",priority="1"')
TIER3_BEFORE=$(snapshot_counter "tenant_tier3_evaluate_total" 'tenant_id="e2e-pss-tenant",priority="1"')
log_info "TVM-02: Baselines tier1=${TIER1_BEFORE} tier2=${TIER2_BEFORE} tier3=${TIER3_BEFORE}"

# ---------------------------------------------------------------------------
# Wait for at least one SnapshotJob evaluation cycle during the grace window
# poll_until_exists waits for tenant_state to appear (first NotReady cycle)
# ---------------------------------------------------------------------------

log_info "TVM-02: Polling for tenant_state series to appear (max 30s)..."
if poll_until_exists 30 3 'tenant_state'; then
    log_info "TVM-02: tenant_state series detected"
else
    log_warn "TVM-02: tenant_state series not detected within 30s; proceeding"
fi

# Brief additional sleep to allow at least one counter scrape cycle
sleep 5

# ---------------------------------------------------------------------------
# TVM-02A: tenant_state gauge equals 0 (NotReady) during grace window
# ---------------------------------------------------------------------------

log_info "TVM-02: Querying tenant_state for e2e-pss-tenant..."
STATE=$(query_prometheus 'tenant_state{tenant_id="e2e-pss-tenant"}' | jq -r '.data.result[0].value[1] // "-1"' | cut -d. -f1)
log_info "TVM-02: tenant_state=${STATE}"

if [ "$STATE" = "0" ]; then
    record_pass "TVM-02A: tenant_state=0 (NotReady) during grace window" "state=${STATE}"
else
    record_fail "TVM-02A: tenant_state=0 (NotReady) during grace window" "state=${STATE} expected=0"
fi

# ---------------------------------------------------------------------------
# TVM-02B: tenant_evaluation_duration_milliseconds_count > 0
# Duration histogram is recorded even on the NotReady path
# ---------------------------------------------------------------------------

log_info "TVM-02: Querying evaluation duration histogram count..."
DURATION_COUNT=$(query_prometheus 'tenant_evaluation_duration_milliseconds_count{tenant_id="e2e-pss-tenant"}' | jq -r '.data.result[0].value[1] // "0"' | cut -d. -f1)
log_info "TVM-02: duration_count=${DURATION_COUNT}"

if [ "$DURATION_COUNT" -gt 0 ]; then
    record_pass "TVM-02B: duration histogram recorded during NotReady" "count=${DURATION_COUNT}"
else
    record_fail "TVM-02B: duration histogram recorded during NotReady" "count=${DURATION_COUNT} expected>0"
fi

# ---------------------------------------------------------------------------
# TVM-02C/D/E: Tier counters must NOT increment during NotReady path
# Sleep 10s to observe counter stability, then snapshot and compare deltas
# ---------------------------------------------------------------------------

log_info "TVM-02: Waiting 10s to observe tier counter stability during NotReady..."
sleep 10

TIER1_AFTER=$(snapshot_counter "tenant_tier1_stale_total"    'tenant_id="e2e-pss-tenant",priority="1"')
TIER2_AFTER=$(snapshot_counter "tenant_tier2_resolved_total" 'tenant_id="e2e-pss-tenant",priority="1"')
TIER3_AFTER=$(snapshot_counter "tenant_tier3_evaluate_total" 'tenant_id="e2e-pss-tenant",priority="1"')

TIER1_DELTA=$((TIER1_AFTER - TIER1_BEFORE))
TIER2_DELTA=$((TIER2_AFTER - TIER2_BEFORE))
TIER3_DELTA=$((TIER3_AFTER - TIER3_BEFORE))
log_info "TVM-02: tier1_delta=${TIER1_DELTA} tier2_delta=${TIER2_DELTA} tier3_delta=${TIER3_DELTA}"

assert_delta_eq "$TIER1_DELTA" 0 "TVM-02C: tier1_stale_total does NOT increment during NotReady" "delta=${TIER1_DELTA}"
assert_delta_eq "$TIER2_DELTA" 0 "TVM-02D: tier2_resolved_total does NOT increment during NotReady" "delta=${TIER2_DELTA}"
assert_delta_eq "$TIER3_DELTA" 0 "TVM-02E: tier3_evaluate_total does NOT increment during NotReady" "delta=${TIER3_DELTA}"

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "TVM-02: Clearing OID overrides..."
reset_oid_overrides

log_info "TVM-02: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "TVM-02: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "TVM-02: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
