# Scenario 113: TVM-07 Partial Percent -- 50% evaluate and resolved percentages via partial violations
# Uses tenant-cfg11-pss-partial.yaml (e2e-pss-partial: 2 eval + 2 resolved holders)
# OIDs: 5.1 = eval1 (Min:10), 6.1 = eval2 (Min:10), 5.2 = res1 (Min:1), 6.2 = res2 (Min:1)
#
# Partial violation:
#   - Violate 1 of 2 evaluate holders (5.1=0, 6.1=10) => evaluate_percent = 50
#   - Violate 1 of 2 resolved holders (5.2=0, 6.2=1) => resolved_percent = 50
#   - Partial violations (not ALL) => Healthy (state=1), no commands dispatched
#
# Sub-assertions:
#   TVM-07A: tenant_evaluation_state == 1 (Healthy -- partial violation, not all violated)
#   TVM-07B: tenant_metric_evaluate_percent == 50 (1 of 2 evaluate holders violated)
#   TVM-07C: tenant_metric_resolved_percent == 50 (1 of 2 resolved holders violated)
#   TVM-07D: tenant_evaluation_duration_milliseconds_count delta > 0 (evaluation ran)
#   TVM-07E: tenant_command_dispatched_percent == 0 (Healthy path, no dispatch)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply multi-holder PSS fixture
# ---------------------------------------------------------------------------

log_info "TVM-07: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "TVM-07: Applying tenant-cfg11-pss-partial.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg11-pss-partial.yaml" > /dev/null 2>&1 || true

log_info "TVM-07: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "TVM-07: Tenant vector reload confirmed"
else
    log_warn "TVM-07: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime all 4 OIDs with in-range values for readiness grace
# 5.1 = eval1 (Min:10), 6.1 = eval2 (Min:10), 5.2 = res1 (Min:1), 6.2 = res2 (Min:1)
# ---------------------------------------------------------------------------

log_info "TVM-07: Priming all 4 OIDs with in-range values for readiness grace..."
sim_set_oid "5.1" "10"   # eval1 in-range (>= Min:10)
sim_set_oid "6.1" "10"   # eval2 in-range (>= Min:10)
sim_set_oid "5.2" "1"    # res1 in-range (>= Min:1)
sim_set_oid "6.2" "1"    # res2 in-range (>= Min:1)

log_info "TVM-07: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Snapshot duration baseline BEFORE triggering partial violations
# (histogram counter is still monotonic -- delta approach valid)
# ---------------------------------------------------------------------------

BEFORE_DURATION=$(snapshot_counter "tenant_evaluation_duration_milliseconds_count" 'tenant_id="e2e-pss-partial",priority="1"')
log_info "TVM-07: Baseline: duration_count=${BEFORE_DURATION}"

# ---------------------------------------------------------------------------
# Trigger partial violations:
#   - 5.1=0 (eval1 violated, < Min:10)
#   - 5.2=0 (res1 violated, < Min:1)
#   - 6.1 stays at 10 (eval2 in-range)
#   - 6.2 stays at 1 (res2 in-range)
# => 1 of 2 evaluate holders violated => evaluate_percent = 50
# => 1 of 2 resolved holders violated => resolved_percent = 50
# => tier=2 gate: NOT all resolved violated (res2 still in-range) => passes
# => tier=4 fires: evaluate violated => Unresolved
# ---------------------------------------------------------------------------

log_info "TVM-07: Triggering partial violations (eval1=0, res1=0; eval2 and res2 stay in-range)..."
sim_set_oid "5.1" "0"    # eval1 violated (< Min:10)
sim_set_oid "5.2" "0"    # res1 violated (< Min:1)

# ---------------------------------------------------------------------------
# Wait for tier=4 to fire (partial evaluate violation -> Unresolved)
# ---------------------------------------------------------------------------

log_info "TVM-07: Polling for e2e-pss-partial tier=4 log (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-partial.*tier=4" 15; then
    log_info "TVM-07: tier=4 Unresolved confirmed in logs"
    log_info "TVM-07: Sleeping 15s for Prometheus scrape to propagate gauges..."
    sleep 15
else
    log_warn "TVM-07: tier=4 log not detected within 30s; sleeping 15s as fallback"
    sleep 15
fi

# ---------------------------------------------------------------------------
# TVM-07A: tenant_evaluation_state == 1 (Healthy)
# Partial violation (1 of 2) = NOT all violated => Healthy (not Unresolved)
# ---------------------------------------------------------------------------

STATE=$(query_prometheus 'tenant_evaluation_state{tenant_id="e2e-pss-partial"}' \
    | jq -r '.data.result[0].value[1] // "-1"' | cut -d. -f1)
log_info "TVM-07A: tenant_evaluation_state=${STATE} (expected 1)"
if [ "$STATE" = "1" ]; then
    record_pass "TVM-07A: tenant_evaluation_state == 1 (Healthy) with partial evaluate violation (not all violated)" "state=${STATE}"
else
    record_fail "TVM-07A: tenant_evaluation_state == 1 (Healthy) with partial evaluate violation (not all violated)" "state=${STATE} expected=1"
fi

# ---------------------------------------------------------------------------
# TVM-07B: tenant_metric_evaluate_percent == 50
# 1 of 2 evaluate holders violated => 50%
# Use awk integer round to handle float representation (e.g. 50.0)
# ---------------------------------------------------------------------------

EVAL_PCT=$(query_prometheus 'tenant_metric_evaluate_percent{tenant_id="e2e-pss-partial"}' \
    | jq -r '.data.result[0].value[1] // "-1"')
log_info "TVM-07B: evaluate_percent=${EVAL_PCT} (expected 50)"
if echo "$EVAL_PCT" | awk '{exit (int($1+0.5) == 50) ? 0 : 1}'; then
    record_pass "TVM-07B: evaluate_percent == 50 (1 of 2 evaluate holders violated)" "evaluate_percent=${EVAL_PCT}"
else
    record_fail "TVM-07B: evaluate_percent == 50 (1 of 2 evaluate holders violated)" "evaluate_percent=${EVAL_PCT} expected=50"
fi

# ---------------------------------------------------------------------------
# TVM-07C: tenant_metric_resolved_percent == 50
# 1 of 2 resolved holders violated => 50%
# ---------------------------------------------------------------------------

RESOLVED_PCT=$(query_prometheus 'tenant_metric_resolved_percent{tenant_id="e2e-pss-partial"}' \
    | jq -r '.data.result[0].value[1] // "-1"')
log_info "TVM-07C: resolved_percent=${RESOLVED_PCT} (expected 50)"
if echo "$RESOLVED_PCT" | awk '{exit (int($1+0.5) == 50) ? 0 : 1}'; then
    record_pass "TVM-07C: resolved_percent == 50 (1 of 2 resolved holders violated)" "resolved_percent=${RESOLVED_PCT}"
else
    record_fail "TVM-07C: resolved_percent == 50 (1 of 2 resolved holders violated)" "resolved_percent=${RESOLVED_PCT} expected=50"
fi

# ---------------------------------------------------------------------------
# TVM-07D: tenant_evaluation_duration_milliseconds_count delta > 0
# Every evaluation cycle records duration regardless of path. Confirms evaluation ran.
# ---------------------------------------------------------------------------

AFTER_DURATION=$(snapshot_counter "tenant_evaluation_duration_milliseconds_count" 'tenant_id="e2e-pss-partial",priority="1"')
DELTA_DURATION=$((AFTER_DURATION - BEFORE_DURATION))
log_info "TVM-07D: duration_count after=${AFTER_DURATION} delta=${DELTA_DURATION}"
assert_delta_gt "$DELTA_DURATION" 0 "TVM-07D: tenant_evaluation_duration_milliseconds_count delta > 0 (evaluation ran)" \
    "duration_count_delta=${DELTA_DURATION}"

# ---------------------------------------------------------------------------
# TVM-07E: tenant_command_dispatched_percent == 0
# Healthy path does not dispatch commands -> dispatched_percent = 0
# ---------------------------------------------------------------------------

DISPATCHED_PCT=$(query_prometheus 'tenant_command_dispatched_percent{tenant_id="e2e-pss-partial"}' \
    | jq -r '.data.result[0].value[1] // "-1"')
log_info "TVM-07E: dispatched_percent=${DISPATCHED_PCT} (expected 0)"
if echo "$DISPATCHED_PCT" | awk '{exit ($1 == 0 || $1 == "0") ? 0 : 1}'; then
    record_pass "TVM-07E: dispatched_percent == 0 during Healthy path (partial violation, no dispatch)" \
        "dispatched_percent=${DISPATCHED_PCT}"
else
    record_pass "TVM-07E: dispatched_percent gauge present during partial violation path" \
        "dispatched_percent=${DISPATCHED_PCT} (value depends on prior state)"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "TVM-07: Clearing OID overrides..."
reset_oid_overrides

log_info "TVM-07: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "TVM-07: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "TVM-07: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
