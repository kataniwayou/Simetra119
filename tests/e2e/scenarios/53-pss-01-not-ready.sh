# Scenario 53: PSS-01 Not Ready -- freshly loaded tenant is not ready during readiness grace window
# Uses tenant-cfg06-pss-single.yaml (e2e-pss-tenant at Priority=1, IntervalSeconds=1, GraceMultiplier=2.0)
#
# ReadinessGrace = TimeSeriesSize * IntervalSeconds * GraceMultiplier = 3 * 1 * 2.0 = 6s
#
# Sequence:
#   1. Apply single-tenant PSS fixture
#   2. Wait for tenant vector reload
#   3. DO NOT prime (deliberately skip sim_set_oid so no data arrives)
#   4. With 1s SnapshotJob interval the first cycle fires within ~1s of reload
#   5. SnapshotJob logs "not ready" for e2e-pss-tenant because ReadSeries().Length == 0
#      and < 6s have elapsed since ConstructedAt
#
# NOTE: This script asserts BEFORE the grace window expires. The 1s cycle interval
# means the log appears within a few seconds of fixture application.

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
# PSS-01A: Assert "not ready" log appears BEFORE grace window expires
# Do NOT prime with sim_set_oid -- holders must remain empty so SnapshotJob
# logs "not ready" on the first evaluation cycle.
# With IntervalSeconds=1 the first cycle fires within ~1s of reload.
# Timeout=15s, interval=1s, since=15s -- grace is 6s so log must appear early.
# ---------------------------------------------------------------------------

log_info "PSS-01: Polling for e2e-pss-tenant not ready log (15s timeout, before grace expires)..."
if poll_until_log 15 1 "e2e-pss-tenant.*not ready" 15; then
    record_pass "PSS-01A: e2e-pss-tenant not ready during grace window" "log=not_ready_found_before_grace_expires"
else
    record_fail "PSS-01A: e2e-pss-tenant not ready during grace window" "not ready log for e2e-pss-tenant not found within 15s"
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
