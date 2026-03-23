# Scenario 111: TVM-05 Unresolved (evaluate violated) -- tenant_state=3 via tier=4 evaluate path
# Uses tenant-cfg06-pss-single.yaml (e2e-pss-tenant: IntervalSeconds=1, GraceMultiplier=2.0)
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
#
# Tier=4 fires when:
#   - Resolved holders are NOT all violated (tier=2 gate passes, resolved in-range)
#   - Evaluate holder IS violated (value < Min:10)
#
# To produce tier=4: prime all OIDs healthy, then set evaluate=0 (violated),
# leave resolved=1 (in-range). SnapshotJob: tier=2 passes, evaluate violated -> tier=4.
# Commands are dispatched. tenant_state gauge records 3 (Unresolved).
#
# Sub-assertions:
#   TVM-05A: tenant_state gauge == 3 (Unresolved)
#   TVM-05B: tenant_command_dispatched_total delta > 0 (commands dispatched at tier=4)
#   TVM-05C: tenant_evaluation_duration_milliseconds_count delta > 0 (evaluation ran)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply single-tenant PSS fixture
# ---------------------------------------------------------------------------

log_info "TVM-05: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "TVM-05: Applying tenant-cfg06-pss-single.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-single.yaml" > /dev/null 2>&1 || true

log_info "TVM-05: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "TVM-05: Tenant vector reload confirmed"
else
    log_warn "TVM-05: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime T2 OIDs with in-range values to pass readiness grace
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# ---------------------------------------------------------------------------

log_info "TVM-05: Priming T2 OIDs with in-range values for readiness grace..."
sim_set_oid "5.1" "10"   # T2 eval in-range (>= Min:10)
sim_set_oid "5.2" "1"    # T2 res1 in-range (>= Min:1)
sim_set_oid "5.3" "1"    # T2 res2 in-range (>= Min:1)

log_info "TVM-05: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Snapshot counter baselines BEFORE triggering evaluate violation
# ---------------------------------------------------------------------------

BEFORE_DISPATCHED=$(snapshot_counter "tenant_command_dispatched_total" 'tenant_id="e2e-pss-tenant",priority="1"')
BEFORE_DURATION=$(snapshot_counter "tenant_evaluation_duration_milliseconds_count" 'tenant_id="e2e-pss-tenant",priority="1"')
log_info "TVM-05: Baselines: dispatched=${BEFORE_DISPATCHED} duration_count=${BEFORE_DURATION}"

# ---------------------------------------------------------------------------
# Trigger Unresolved via evaluate violation
# Set evaluate=0 (violated, < Min:10). Resolved stay at 1 (in-range, NOT all violated).
# => tier=2 gate passes, tier=4 fires: commands enqueued, tenant_state=3.
# ---------------------------------------------------------------------------

log_info "TVM-05: Setting T2 evaluate to violated (eval=0, resolved unchanged at 1)..."
sim_set_oid "5.1" "0"    # T2 eval violated (< Min:10)

# ---------------------------------------------------------------------------
# Wait for tier=4 to fire (evaluate violated -> Unresolved)
# ---------------------------------------------------------------------------

log_info "TVM-05: Polling for e2e-pss-tenant tier=4 log (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-tenant.*tier=4" 15; then
    log_info "TVM-05: tier=4 Unresolved confirmed in logs"
else
    log_warn "TVM-05: tier=4 log not detected within 30s; continuing with assertions"
fi

log_info "TVM-05: Waiting 10s to accumulate counter deltas..."
sleep 10

# ---------------------------------------------------------------------------
# Sub-assertion TVM-05A: tenant_state gauge == 3 (Unresolved)
# Gauge is queried point-in-time; not summed (use query_prometheus directly).
# ---------------------------------------------------------------------------

STATE=$(query_prometheus 'tenant_state{tenant_id="e2e-pss-tenant",priority="1"}' \
    | jq -r '.data.result[0].value[1] // "-1"' | cut -d. -f1)
log_info "TVM-05: tenant_state=${STATE}"
if [ "$STATE" = "3" ]; then
    record_pass "TVM-05A: tenant_state == 3 (Unresolved)" "state=${STATE}"
else
    record_fail "TVM-05A: tenant_state == 3 (Unresolved)" "expected=3 actual=${STATE}"
fi

# ---------------------------------------------------------------------------
# Sub-assertion TVM-05B: tenant_command_dispatched_total delta > 0
# Commands are dispatched at tier=4 Unresolved (one per command per cycle).
# ---------------------------------------------------------------------------

log_info "TVM-05: Polling for command_dispatched increment (30s timeout)..."
if poll_until 30 2 "tenant_command_dispatched_total" 'tenant_id="e2e-pss-tenant",priority="1"' "$BEFORE_DISPATCHED"; then
    AFTER_DISPATCHED=$(snapshot_counter "tenant_command_dispatched_total" 'tenant_id="e2e-pss-tenant",priority="1"')
    DELTA_DISPATCHED=$((AFTER_DISPATCHED - BEFORE_DISPATCHED))
    log_info "TVM-05: dispatched after=${AFTER_DISPATCHED} delta=${DELTA_DISPATCHED}"
    assert_delta_gt "$DELTA_DISPATCHED" 0 "TVM-05B: tenant_command_dispatched_total delta > 0" \
        "dispatched_delta=${DELTA_DISPATCHED}"
else
    AFTER_DISPATCHED=$(snapshot_counter "tenant_command_dispatched_total" 'tenant_id="e2e-pss-tenant",priority="1"')
    DELTA_DISPATCHED=$((AFTER_DISPATCHED - BEFORE_DISPATCHED))
    log_info "TVM-05: dispatched after=${AFTER_DISPATCHED} delta=${DELTA_DISPATCHED}"
    record_fail "TVM-05B: tenant_command_dispatched_total delta > 0" \
        "dispatched_delta=${DELTA_DISPATCHED} expected > 0 after 30s polling"
fi

# ---------------------------------------------------------------------------
# Sub-assertion TVM-05C: tenant_evaluation_duration_milliseconds_count delta > 0
# Every evaluation cycle records duration regardless of path. Confirms evaluation ran.
# ---------------------------------------------------------------------------

AFTER_DURATION=$(snapshot_counter "tenant_evaluation_duration_milliseconds_count" 'tenant_id="e2e-pss-tenant",priority="1"')
DELTA_DURATION=$((AFTER_DURATION - BEFORE_DURATION))
log_info "TVM-05: duration_count after=${AFTER_DURATION} delta=${DELTA_DURATION}"
assert_delta_gt "$DELTA_DURATION" 0 "TVM-05C: tenant_evaluation_duration_milliseconds_count delta > 0" \
    "duration_count_delta=${DELTA_DURATION}"

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "TVM-05: Clearing OID overrides..."
reset_oid_overrides

log_info "TVM-05: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "TVM-05: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "TVM-05: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
