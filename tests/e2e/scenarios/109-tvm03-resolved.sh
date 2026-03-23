# Scenario 109: TVM-03 Resolved -- tier=2 stops evaluation when all resolved holders are violated
# Uses tenant-cfg06-pss-single.yaml (e2e-pss-tenant: IntervalSeconds=1, GraceMultiplier=2.0)
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
#
# Resolved path (tier=2) fires when ALL resolved holders are violated (both res OIDs < Min:1 = value 0).
# Evaluation stops at tier=2 -- no tier=3 or tier=4, no commands dispatched.
# tenant_state gauge = 2 (Resolved).
#
# Sub-assertions:
#   TVM-03A: tenant_state gauge == 2 (Resolved)
#   TVM-03B: tenant_evaluation_duration_milliseconds_count delta > 0 (evaluation ran during Resolved)
#   TVM-03C: tenant_command_dispatched_total delta == 0 (no commands at tier=2)
#
# NOTE on tier counters: During the Resolved path, tier2_resolved increments per non-violated
# resolved count (which may be 0 when ALL resolved are violated). These counters are not reliably
# assertable for this path. The assertion set is: state + duration + no-commands.

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply single-tenant PSS fixture
# ---------------------------------------------------------------------------

log_info "TVM-03: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "TVM-03: Applying tenant-cfg06-pss-single.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-single.yaml" > /dev/null 2>&1 || true

log_info "TVM-03: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "TVM-03: Tenant vector reload confirmed"
else
    log_warn "TVM-03: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime T2 OIDs with in-range values to pass readiness grace
# ---------------------------------------------------------------------------

log_info "TVM-03: Priming T2 OIDs for readiness grace..."
sim_set_oid "5.1" "10"   # T2 eval in-range (>= Min:10)
sim_set_oid "5.2" "1"    # T2 res1 in-range (>= Min:1)
sim_set_oid "5.3" "1"    # T2 res2 in-range (>= Min:1)

log_info "TVM-03: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Trigger Resolved path: both resolved OIDs violated (both < Min:1 = value 0)
# T2 eval remains at 10 (in-range) -- tier=2 fires before tier=3 check
# ---------------------------------------------------------------------------

log_info "TVM-03: Setting both resolved OIDs to violated (0)..."
sim_set_oid "5.2" "0"    # T2 res1 violated (< Min:1)
sim_set_oid "5.3" "0"    # T2 res2 violated (< Min:1)

# ---------------------------------------------------------------------------
# Wait for Resolved state -- poll for tier=2 log
# ---------------------------------------------------------------------------

log_info "TVM-03: Polling for tier=2 log (all resolved violated, 45s timeout)..."
if poll_until_log 45 1 "e2e-pss-tenant.*tier=2" 15; then
    log_info "TVM-03: tier=2 log confirmed for e2e-pss-tenant"
    log_info "TVM-03: Sleeping 15s for Prometheus scrape to propagate state=2..."
    sleep 15
else
    log_warn "TVM-03: tier=2 log not found within 45s; sleeping 15s as fallback"
    sleep 15
fi

# ---------------------------------------------------------------------------
# Snapshot baselines AFTER state is Resolved (avoids prior-scenario carry-over)
# ---------------------------------------------------------------------------

BEFORE_DURATION=$(snapshot_counter "tenant_evaluation_duration_milliseconds_count" 'tenant_id="e2e-pss-tenant",priority="1"')
BEFORE_DISPATCHED=$(snapshot_counter "tenant_command_dispatched_total" 'tenant_id="e2e-pss-tenant",priority="1"')
log_info "TVM-03: Baselines (post-Resolved) -- duration_count=${BEFORE_DURATION} dispatched=${BEFORE_DISPATCHED}"

log_info "TVM-03: Waiting 10s to accumulate counter deltas in Resolved state..."
sleep 10

# ---------------------------------------------------------------------------
# Snapshot after values
# ---------------------------------------------------------------------------

AFTER_DURATION=$(snapshot_counter "tenant_evaluation_duration_milliseconds_count" 'tenant_id="e2e-pss-tenant",priority="1"')
AFTER_DISPATCHED=$(snapshot_counter "tenant_command_dispatched_total" 'tenant_id="e2e-pss-tenant",priority="1"')
DELTA_DURATION=$((AFTER_DURATION - BEFORE_DURATION))
DELTA_DISPATCHED=$((AFTER_DISPATCHED - BEFORE_DISPATCHED))
log_info "TVM-03: After -- duration_count=${AFTER_DURATION} delta=${DELTA_DURATION} dispatched=${AFTER_DISPATCHED} delta=${DELTA_DISPATCHED}"

# ---------------------------------------------------------------------------
# TVM-03A: tenant_state gauge == 2 (Resolved)
# ---------------------------------------------------------------------------

STATE=$(query_prometheus 'tenant_state{tenant_id="e2e-pss-tenant"}' \
    | jq -r '.data.result[0].value[1] // "-1"' | cut -d. -f1)
log_info "TVM-03A: tenant_state=${STATE} (expected 2)"
if [ "$STATE" = "2" ]; then
    record_pass "TVM-03A: tenant_state=2 (Resolved) when all resolved holders violated" "state=${STATE}"
else
    record_fail "TVM-03A: tenant_state=2 (Resolved) when all resolved holders violated" "state=${STATE} expected=2"
fi

# ---------------------------------------------------------------------------
# TVM-03B: tenant_evaluation_duration_milliseconds_count delta > 0
# Evaluation ran during Resolved path (duration is always recorded)
# ---------------------------------------------------------------------------

assert_delta_gt "$DELTA_DURATION" 0 \
    "TVM-03B: duration histogram count increments during Resolved path" \
    "delta=${DELTA_DURATION}"

# ---------------------------------------------------------------------------
# TVM-03C: tenant_command_dispatched_total delta == 0
# Evaluation stops at tier=2 -- no commands dispatched
# ---------------------------------------------------------------------------

assert_delta_eq "$DELTA_DISPATCHED" 0 \
    "TVM-03C: no commands dispatched during Resolved path (tier=2 stops evaluation)" \
    "delta=${DELTA_DISPATCHED} expected=0"

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "TVM-03: Clearing OID overrides..."
reset_oid_overrides

log_info "TVM-03: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "TVM-03: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "TVM-03: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
