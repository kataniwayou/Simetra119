# Scenario 61: PSS-13 Both Unresolved -- both tenants reach tier=4 with independent command dispatch
# Uses tenant-cfg07-pss-two-tenant.yaml (e2e-pss-t1 + e2e-pss-t2, Priority=1)
# T1 OIDs: 5.1 = eval (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# T2 OIDs: 6.1 = eval (Min:10), 6.2 = res1 (Min:1), 6.3 = res2 (Min:1)
#
# Independence property: BOTH tenants' evaluate OIDs violated simultaneously.
# Both reach tier=4 independently and each dispatches commands.
# Counter delta >= 2 (shared device_name="E2E-SIM") proves both tenants dispatched.
#
# Sub-assertions:
#   61a: e2e-pss-t1 logs tier=4 (unresolved)
#   61b: e2e-pss-t2 logs tier=4 (unresolved)
#   61c: snmp_command_dispatched_total counter delta >= 2 (both tenants dispatched commands)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply two-tenant PSS fixture
# ---------------------------------------------------------------------------

log_info "PSS-13: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "PSS-13: Applying tenant-cfg07-pss-two-tenant.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg07-pss-two-tenant.yaml" > /dev/null 2>&1 || true

log_info "PSS-13: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "PSS-13: Tenant vector reload confirmed"
else
    log_warn "PSS-13: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime ALL 6 OIDs with in-range values to pass readiness grace
# T1 OIDs: 5.1 = eval (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# T2 OIDs: 6.1 = eval (Min:10), 6.2 = res1 (Min:1), 6.3 = res2 (Min:1)
# ---------------------------------------------------------------------------

log_info "PSS-13: Priming all 6 OIDs with in-range values for readiness grace..."
sim_set_oid "5.1" "10"   # T1 eval in-range (>= Min:10)
sim_set_oid "5.2" "1"    # T1 res1 in-range (>= Min:1)
sim_set_oid "5.3" "1"    # T1 res2 in-range (>= Min:1)
sim_set_oid "6.1" "10"   # T2 eval in-range (>= Min:10)
sim_set_oid "6.2" "1"    # T2 res1 in-range (>= Min:1)
sim_set_oid "6.3" "1"    # T2 res2 in-range (>= Min:1)

log_info "PSS-13: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Capture sent counter baseline BEFORE stimulus
# Both tenants will dispatch commands -- delta >= 2 proves independence
# Counter label device_name="E2E-SIM" is shared across both tenants
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
log_info "PSS-13: Baseline sent=${BEFORE_SENT}"

# ---------------------------------------------------------------------------
# Stimulus: set BOTH tenants' eval OIDs to violated
# T1 eval violated (5.1=0): T1 resolved in-range (5.2=1, 5.3=1) => T1 tier=4
# T2 eval violated (6.1=0): T2 resolved in-range (6.2=1, 6.3=1) => T2 tier=4
# ---------------------------------------------------------------------------

log_info "PSS-13: Setting both T1 eval (5.1=0) and T2 eval (6.1=0) to violated..."
sim_set_oid "5.1" "0"    # T1 eval violated (< Min:10) -> T1 Unresolved
sim_set_oid "6.1" "0"    # T2 eval violated (< Min:10) -> T2 Unresolved

# ---------------------------------------------------------------------------
# Sub-scenario 61a: T1 = Unresolved (tier=4)
# T1 eval violated, T1 resolved in-range => tier=2 gate passes, tier=4 fires
# ---------------------------------------------------------------------------

log_info "PSS-13: Polling for e2e-pss-t1 tier=4 log (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-t1.*tier=4" 15; then
    record_pass "PSS-13A: e2e-pss-t1 tier=4 unresolved (evaluate violated)" "log=t1_tier4_found"
else
    record_fail "PSS-13A: e2e-pss-t1 tier=4 unresolved (evaluate violated)" "tier=4 log for e2e-pss-t1 not found within 30s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 61b: T2 = Unresolved (tier=4)
# T2 eval violated, T2 resolved in-range => tier=2 gate passes, tier=4 fires
# ---------------------------------------------------------------------------

log_info "PSS-13: Polling for e2e-pss-t2 tier=4 log (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-t2.*tier=4" 15; then
    record_pass "PSS-13B: e2e-pss-t2 tier=4 unresolved (evaluate violated)" "log=t2_tier4_found"
else
    record_fail "PSS-13B: e2e-pss-t2 tier=4 unresolved (evaluate violated)" "tier=4 log for e2e-pss-t2 not found within 30s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 61c: snmp_command_dispatched_total counter delta >= 2
# Both tenants dispatched commands -- shared device_name="E2E-SIM" counter
# delta >= 2 proves both tenants contributed at least one command dispatch each
# ---------------------------------------------------------------------------

log_info "PSS-13: Polling for sent counter increment by both tenants (30s timeout)..."
if poll_until 30 2 "snmp_command_dispatched_total" 'device_name="E2E-SIM"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "PSS-13: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    if [ "$DELTA_SENT" -ge 2 ]; then
        record_pass "PSS-13C: Both tenants dispatched commands (delta >= 2)" "sent_delta=${DELTA_SENT}"
    else
        record_fail "PSS-13C: Both tenants dispatched commands (delta >= 2)" "sent_delta=${DELTA_SENT} expected >= 2 (only one tenant dispatched)"
    fi
else
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "PSS-13: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_fail "PSS-13C: Both tenants dispatched commands (delta >= 2)" "sent_delta=${DELTA_SENT} expected >= 2 after 30s polling"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "PSS-13: Clearing OID overrides..."
reset_oid_overrides

log_info "PSS-13: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "PSS-13: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "PSS-13: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
