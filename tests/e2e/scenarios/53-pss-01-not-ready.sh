# Scenario 53: PSS-01 Stale-path Unresolved -- G1 tenant reaches Unresolved via staleness
# Uses tenant-cfg06-pss-single.yaml (e2e-pss-tenant at Priority=1, IntervalSeconds=1, GraceMultiplier=2.0)
#
# Original design relied on "not ready" (empty holders in grace window), but CopyFrom during
# TenantVectorWatcher reload carries over existing series data from prior scenarios, making
# truly empty holders impractical. Using sim_set_oid_stale achieves the same Unresolved result
# through the staleness path (tier=1 stale -> tier=4 Unresolved).
#
# Sequence:
#   1. Apply single-tenant PSS fixture
#   2. Wait for tenant vector reload
#   3. Prime T2 OIDs to healthy, wait for readiness grace
#   4. Set T2 OIDs to stale (NoSuchInstance)
#   5. SnapshotJob detects staleness -> tier=1 -> tier=4 Unresolved

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply single-tenant PSS fixture
# ---------------------------------------------------------------------------

log_info "PSS-01: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "PSS-01: Applying tenant-cfg06-pss-single.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-single.yaml" > /dev/null 2>&1 || true

log_info "PSS-01: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "PSS-01: Tenant vector reload confirmed"
else
    log_warn "PSS-01: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime T2 OIDs to healthy, wait for readiness grace, then stale
# ---------------------------------------------------------------------------

log_info "PSS-01: Priming T2 OIDs to healthy state..."
sim_set_oid "5.1" "10"   # T2 eval (in-range >= Min:10)
sim_set_oid "5.2" "1"    # T2 res1 (in-range >= Min:1)
sim_set_oid "5.3" "1"    # T2 res2 (in-range >= Min:1)

log_info "PSS-01: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

log_info "PSS-01: Setting T2 OIDs to stale (NoSuchInstance)..."
sim_set_oid_stale "5.1"
sim_set_oid_stale "5.2"
sim_set_oid_stale "5.3"

# ---------------------------------------------------------------------------
# PSS-01A: Assert tenant reaches Unresolved (tier=4 via stale path)
# ---------------------------------------------------------------------------

log_info "PSS-01: Polling for e2e-pss-tenant tier=4 log (stale -> Unresolved)..."
if poll_until_log 30 1 "e2e-pss-tenant.*tier=4" 15; then
    record_pass "PSS-01A: e2e-pss-tenant Unresolved via stale path" "log=tier4_found_after_stale"
else
    record_fail "PSS-01A: e2e-pss-tenant Unresolved via stale path" "tier=4 log for e2e-pss-tenant not found within 30s"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "PSS-01: Clearing OID overrides..."
reset_oid_overrides

log_info "PSS-01: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "PSS-01: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "PSS-01: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
