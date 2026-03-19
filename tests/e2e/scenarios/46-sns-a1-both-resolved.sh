# Scenario 46: SNS-A1 Advance gate pass -- both G1 tenants Resolved
# Uses tenant-cfg05-four-tenant-snapshot.yaml (G1: T1+T2 priority=1, G2: T3+T4 priority=2)
#
# Gate rule: ALL G1 tenants must be Resolved or Healthy for gate to pass.
# This scenario: T1 resolved=0 (< Min:1) x2, T2 resolved=0 x2 -> both Resolved (tier=2)
# -> gate PASSES -> G2 tenants (T3, T4) are evaluated.
#
# Per-OID values:
#   T1 (4.x): eval=10 (in-range), res1=0, res2=0 (both violated < Min:1) -> Resolved
#   T2 (5.x): eval=10 (in-range), res1=0, res2=0 (both violated < Min:1) -> Resolved
#   T3 (6.x): eval=10, res1=1, res2=1 (all in-range from priming) -> Healthy
#   T4 (7.x): eval=10, res1=1, res2=1 (all in-range from priming) -> Healthy

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply 4-tenant fixture
# ---------------------------------------------------------------------------

log_info "SNS-A1: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "SNS-A1: Applying 4-tenant fixture..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg05-four-tenant-snapshot.yaml" > /dev/null 2>&1 || true

log_info "SNS-A1: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "SNS-A1: Tenant vector reload confirmed"
else
    log_warn "SNS-A1: Tenant vector reload log not found within 60s — proceeding anyway"
fi

# Prime all 4 tenants to healthy state (passes readiness grace)
sim_set_oid "4.1" "10"    # T1 eval (in-range >= Min:10)
sim_set_oid "4.2" "1"     # T1 res1 (in-range >= Min:1)
sim_set_oid "4.3" "1"     # T1 res2 (in-range >= Min:1)
sim_set_oid "5.1" "10"    # T2 eval
sim_set_oid "5.2" "1"     # T2 res1
sim_set_oid "5.3" "1"     # T2 res2
sim_set_oid "6.1" "10"    # T3 eval
sim_set_oid "6.2" "1"     # T3 res1
sim_set_oid "6.3" "1"     # T3 res2
sim_set_oid "7.1" "10"    # T4 eval
sim_set_oid "7.2" "1"     # T4 res1
sim_set_oid "7.3" "1"     # T4 res2

log_info "SNS-A1: Waiting 8s for readiness grace (TimeSeriesSize=3, IntervalSeconds=1, GraceMultiplier=2 -> grace=6s)..."
sleep 8

# ---------------------------------------------------------------------------
# Set both G1 tenants to Resolved: resolved violated, eval in-range
# ---------------------------------------------------------------------------

# T1: all resolved violated -> tier=2 Resolved
sim_set_oid "4.2" "0"     # T1 res1 violated (< Min:1)
sim_set_oid "4.3" "0"     # T1 res2 violated (< Min:1)
# T2: all resolved violated -> tier=2 Resolved
sim_set_oid "5.2" "0"     # T2 res1 violated (< Min:1)
sim_set_oid "5.3" "0"     # T2 res2 violated (< Min:1)

# ---------------------------------------------------------------------------
# === SNS-A1a: G1-T1 reaches tier=2 Resolved ===
# ---------------------------------------------------------------------------

log_info "SNS-A1a: Polling for G1-T1 tier=2 Resolved log..."
if poll_until_log 30 1 "e2e-tenant-G1-T1.*tier=2" 15; then
    record_pass "SNS-A1a: G1-T1 tier=2 Resolved" "log=tier2_G1T1_found"
else
    record_fail "SNS-A1a: G1-T1 tier=2 Resolved" "tier=2 log for G1-T1 not found within 30s"
fi

# ---------------------------------------------------------------------------
# === SNS-A1b: G1-T2 reaches tier=2 Resolved ===
# ---------------------------------------------------------------------------

log_info "SNS-A1b: Polling for G1-T2 tier=2 Resolved log..."
if poll_until_log 30 1 "e2e-tenant-G1-T2.*tier=2" 15; then
    record_pass "SNS-A1b: G1-T2 tier=2 Resolved" "log=tier2_G1T2_found"
else
    record_fail "SNS-A1b: G1-T2 tier=2 Resolved" "tier=2 log for G1-T2 not found within 30s"
fi

# ---------------------------------------------------------------------------
# === SNS-A1c: Gate passes -- G2-T3 is evaluated (tier log present) ===
# G2 tenants are Healthy from priming, so expect tier=3 log.
# ---------------------------------------------------------------------------

log_info "SNS-A1c: Polling for G2-T3 tier log (gate-pass assertion)..."
if poll_until_log 30 1 "e2e-tenant-G2-T3.*tier=" 15; then
    record_pass "SNS-A1c: G2-T3 evaluated (gate passed — both G1 Resolved)" "log=G2T3_tier_found"
else
    record_fail "SNS-A1c: G2-T3 evaluated (gate passed — both G1 Resolved)" "G2-T3 tier log not found within 30s — gate may have blocked"
fi

# ---------------------------------------------------------------------------
# Cleanup: reset OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "SNS-A1: Resetting OID overrides..."
reset_oid_overrides

log_info "SNS-A1: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "SNS-A1: Failed to restore tenant ConfigMap"
else
    log_warn "SNS-A1: Original tenant ConfigMap snapshot not found — skipping restore"
fi
