# Scenario 59: PSS-11 T1=Healthy T2=Unresolved -- tenant independence within same priority group
# Uses tenant-cfg07-pss-two-tenant.yaml (e2e-pss-t1 + e2e-pss-t2, Priority=1)
# T1 OIDs: 5.1 = eval (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# T2 OIDs: 6.1 = eval (Min:10), 6.2 = res1 (Min:1), 6.3 = res2 (Min:1)
#
# Independence property: T2 eval violated -> T2 tier=4 (unresolved), T1 untouched -> T1 tier=3 (healthy)
# T1's metric values do not affect T2's evaluation and vice versa.
#
# Sub-assertions:
#   59a: e2e-pss-t1 logs tier=3 (healthy, all in-range)
#   59b: e2e-pss-t2 logs tier=4 commands enqueued (evaluate violated)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply two-tenant PSS fixture
# ---------------------------------------------------------------------------

log_info "PSS-11: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "PSS-11: Applying tenant-cfg07-pss-two-tenant.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg07-pss-two-tenant.yaml" > /dev/null 2>&1 || true

log_info "PSS-11: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "PSS-11: Tenant vector reload confirmed"
else
    log_warn "PSS-11: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime ALL 6 OIDs with in-range values to pass readiness grace
# T1 OIDs: 5.1 = eval (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# T2 OIDs: 6.1 = eval (Min:10), 6.2 = res1 (Min:1), 6.3 = res2 (Min:1)
# ---------------------------------------------------------------------------

log_info "PSS-11: Priming all 6 OIDs with in-range values for readiness grace..."
sim_set_oid "5.1" "10"   # T1 eval in-range (>= Min:10)
sim_set_oid "5.2" "1"    # T1 res1 in-range (>= Min:1)
sim_set_oid "5.3" "1"    # T1 res2 in-range (>= Min:1)
sim_set_oid "6.1" "10"   # T2 eval in-range (>= Min:10)
sim_set_oid "6.2" "1"    # T2 res1 in-range (>= Min:1)
sim_set_oid "6.3" "1"    # T2 res2 in-range (>= Min:1)

log_info "PSS-11: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Stimulus: set T2 eval OID to violated -- only T2 becomes unresolved
# T1 OIDs are untouched -- T1 stays healthy (all in-range)
# ---------------------------------------------------------------------------

log_info "PSS-11: Setting T2 eval to violated (6.1=0); T1 OIDs remain in-range..."
sim_set_oid "6.1" "0"    # T2 eval violated (< Min:10) -- T2 -> Unresolved

# ---------------------------------------------------------------------------
# Sub-scenario 59a: T1 = Healthy (tier=3)
# T1 OIDs all in-range => tier=2 gate passes (not all resolved violated),
# tier=4 gate passes (eval not violated) => tier=3 Healthy
# ---------------------------------------------------------------------------

log_info "PSS-11: Polling for e2e-pss-t1 tier=3 (healthy) log (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-t1.*tier=3" 15; then
    record_pass "PSS-11A: e2e-pss-t1 tier=3 healthy (T1 unaffected by T2 violation)" "log=t1_tier3_healthy_found"
else
    record_fail "PSS-11A: e2e-pss-t1 tier=3 healthy (T1 unaffected by T2 violation)" "tier=3 log for e2e-pss-t1 not found within 30s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 59b: T2 = Unresolved (tier=4) with commands enqueued
# T2 eval violated (6.1=0), resolved in-range (6.2=1, 6.3=1)
# => tier=2 gate passes (not all resolved violated), tier=4 fires (commands enqueued)
# ---------------------------------------------------------------------------

log_info "PSS-11: Polling for e2e-pss-t2 tier=4 commands enqueued log (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-t2.*tier=4 — commands enqueued\|e2e-pss-t2.*tier=4 -- commands enqueued" 15; then
    record_pass "PSS-11B: e2e-pss-t2 tier=4 commands enqueued (unresolved, independent of T1)" "log=t2_tier4_commands_found"
else
    record_fail "PSS-11B: e2e-pss-t2 tier=4 commands enqueued (unresolved, independent of T1)" "tier=4 commands enqueued log for e2e-pss-t2 not found within 30s"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "PSS-11: Clearing OID overrides..."
reset_oid_overrides

log_info "PSS-11: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "PSS-11: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "PSS-11: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
