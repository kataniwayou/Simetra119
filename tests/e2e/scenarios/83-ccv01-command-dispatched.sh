# Scenario 83: CCV-01 Command Dispatched -- snmp.command.dispatched increments at tier=4 dispatch
# Uses tenant-cfg06-pss-single.yaml (e2e-pss-tenant: IntervalSeconds=1, SuppressionWindowSeconds=10)
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
#
# Tier=4 fires when:
#   - Resolved holders are NOT all violated (tier=2 gate passes)
#   - Evaluate holder IS violated (< Min:10)
#
# To produce tier=4: set evaluate=0 (violated), leave resolved=1 (not violated).
# SnapshotJob dispatches command and increments snmp.command.dispatched.
#
# Sub-assertions:
#   83: CCV-01: command.dispatched increments at tier=4 dispatch

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply single-tenant PSS fixture
# ---------------------------------------------------------------------------

log_info "CCV-01: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "CCV-01: Applying tenant-cfg06-pss-single.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-single.yaml" > /dev/null 2>&1 || true

log_info "CCV-01: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "CCV-01: Tenant vector reload confirmed"
else
    log_warn "CCV-01: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime T2 OIDs with in-range values to pass readiness grace
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# ---------------------------------------------------------------------------

log_info "CCV-01: Priming T2 OIDs with in-range values for readiness grace..."
sim_set_oid "5.1" "10"   # T2 eval in-range (>= Min:10)
sim_set_oid "5.2" "1"    # T2 res1 in-range (>= Min:1)
sim_set_oid "5.3" "1"    # T2 res2 in-range (>= Min:1)

log_info "CCV-01: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Capture dispatched counter baseline BEFORE setting evaluate to violated
# Delta after tier=4 fires must be >= 1 (command dispatched)
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"')
log_info "CCV-01: Baseline dispatched=${BEFORE_SENT}"

# ---------------------------------------------------------------------------
# Set T2 evaluate OID to violated (< Min:10 = value 0)
# T2 res1 and res2 stay at 1 from priming (in-range, NOT all violated)
# => tier=2 gate passes, tier=4 fires (commands enqueued, dispatched increments)
# ---------------------------------------------------------------------------

log_info "CCV-01: Setting T2 evaluate to violated (eval=0, res unchanged at 1)..."
sim_set_oid "5.1" "0"    # T2 eval violated (< Min:10)

# ---------------------------------------------------------------------------
# CCV-01: snmp_command_dispatched_total counter increment
# Poll until dispatched increments above baseline (30s timeout, 2s interval)
# ---------------------------------------------------------------------------

log_info "CCV-01: Polling for dispatched counter increment (30s timeout)..."
if poll_until 30 2 "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "CCV-01: After: dispatched=${AFTER_SENT} delta=${DELTA_SENT}"
    assert_delta_ge "$DELTA_SENT" 1 \
        "CCV-01: command.dispatched increments at tier=4 dispatch" \
        "$(get_evidence "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"')"
else
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "CCV-01: After: dispatched=${AFTER_SENT} delta=${DELTA_SENT}"
    record_fail "CCV-01: command.dispatched increments at tier=4 dispatch" \
        "dispatched_delta=${DELTA_SENT} expected >= 1 after 30s polling; $(get_evidence "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"')"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "CCV-01: Clearing OID overrides..."
reset_oid_overrides

log_info "CCV-01: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "CCV-01: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "CCV-01: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
