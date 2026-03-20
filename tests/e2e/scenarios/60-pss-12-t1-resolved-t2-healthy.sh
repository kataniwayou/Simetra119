# Scenario 60: PSS-12 T1=Resolved T2=Healthy -- tenant independence within same priority group
# Uses tenant-cfg07-pss-two-tenant.yaml (e2e-pss-t1 + e2e-pss-t2, Priority=1)
# T1 OIDs: 5.1 = eval (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# T2 OIDs: 6.1 = eval (Min:10), 6.2 = res1 (Min:1), 6.3 = res2 (Min:1)
#
# Independence property: T1 resolved metrics ALL violated -> T1 tier=2 (resolved, no commands)
# T2 OIDs untouched -> T2 stays healthy tier=3
# Each tenant evaluates from its own metrics alone.
#
# Sub-assertions:
#   60a: e2e-pss-t1 logs tier=2 (resolved, all resolved metrics violated)
#   60b: e2e-pss-t2 logs tier=3 (healthy, all in-range, unaffected by T1)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply two-tenant PSS fixture
# ---------------------------------------------------------------------------

log_info "PSS-12: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "PSS-12: Applying tenant-cfg07-pss-two-tenant.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg07-pss-two-tenant.yaml" > /dev/null 2>&1 || true

log_info "PSS-12: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "PSS-12: Tenant vector reload confirmed"
else
    log_warn "PSS-12: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime ALL 6 OIDs with in-range values to pass readiness grace
# T1 OIDs: 5.1 = eval (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# T2 OIDs: 6.1 = eval (Min:10), 6.2 = res1 (Min:1), 6.3 = res2 (Min:1)
# ---------------------------------------------------------------------------

log_info "PSS-12: Priming all 6 OIDs with in-range values for readiness grace..."
sim_set_oid "5.1" "10"   # T1 eval in-range (>= Min:10)
sim_set_oid "5.2" "1"    # T1 res1 in-range (>= Min:1)
sim_set_oid "5.3" "1"    # T1 res2 in-range (>= Min:1)
sim_set_oid "6.1" "10"   # T2 eval in-range (>= Min:10)
sim_set_oid "6.2" "1"    # T2 res1 in-range (>= Min:1)
sim_set_oid "6.3" "1"    # T2 res2 in-range (>= Min:1)

log_info "PSS-12: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Stimulus: set T1 resolved OIDs to violated (BOTH) -- T1 -> tier=2 Resolved
# T1 eval stays at 10 (in-range), T1 res1+res2 = 0 (violated) -> AreAllResolvedViolated=true
# T2 OIDs untouched -- T2 stays healthy (all in-range)
# ---------------------------------------------------------------------------

log_info "PSS-12: Setting T1 resolved OIDs to violated (5.2=0, 5.3=0); T2 OIDs remain in-range..."
sim_set_oid "5.2" "0"    # T1 res1 violated (< Min:1)
sim_set_oid "5.3" "0"    # T1 res2 violated (< Min:1)

# ---------------------------------------------------------------------------
# Sub-scenario 60a: T1 = Resolved (tier=2)
# T1 resolved OIDs both violated => AreAllResolvedViolated=true => tier=2 fires
# Evaluation stops early -- no commands enqueued for T1
# ---------------------------------------------------------------------------

log_info "PSS-12: Polling for e2e-pss-t1 tier=2 (resolved) log (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-t1.*tier=2" 15; then
    record_pass "PSS-12A: e2e-pss-t1 tier=2 resolved (all resolved metrics violated)" "log=t1_tier2_resolved_found"
else
    record_fail "PSS-12A: e2e-pss-t1 tier=2 resolved (all resolved metrics violated)" "tier=2 log for e2e-pss-t1 not found within 30s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 60b: T2 = Healthy (tier=3)
# T2 OIDs all in-range => tier=2 gate passes (not all resolved violated),
# tier=4 gate passes (eval not violated) => tier=3 Healthy
# T2 evaluation is fully independent of T1's resolved violation
# ---------------------------------------------------------------------------

log_info "PSS-12: Polling for e2e-pss-t2 tier=3 (healthy) log (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-t2.*tier=3" 15; then
    record_pass "PSS-12B: e2e-pss-t2 tier=3 healthy (T2 unaffected by T1 resolved violation)" "log=t2_tier3_healthy_found"
else
    record_fail "PSS-12B: e2e-pss-t2 tier=3 healthy (T2 unaffected by T1 resolved violation)" "tier=3 log for e2e-pss-t2 not found within 30s"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "PSS-12: Clearing OID overrides..."
reset_oid_overrides

log_info "PSS-12: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "PSS-12: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "PSS-12: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
