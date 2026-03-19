# Scenario 50: SNS-B2 Advance gate block -- both G1 tenants Not Ready
# Uses tenant-cfg05-four-tenant-snapshot.yaml (G1: T1+T2 priority=1, G2: T3+T4 priority=2)
#
# Gate rule: blocks if ANY G1 tenant is Unresolved (Not Ready is an Unresolved result).
# This scenario: G1 tenants receive no data -> IsReady = false (empty series) -> Not Ready
# -> ANY G1 not ready -> gate BLOCKS -> G2 tenants (T3, T4) are NOT evaluated.
#
# Timing note: ReadinessGrace = TimeSeriesSize(3) * IntervalSeconds(1) * GraceMultiplier(2) = 6s
# After 6s, IsReady = (ReadSeries().Length > 0) || (UtcNow >= ConstructedAt + 6s).
# With empty series AND past grace, IsReady = true (time-based). But G1 holders with no data
# still have empty series -> staleness may skip, but we assert "not ready" log within 5s.
# We assert the "not ready" log quickly, before the grace window expires.
#
# Per-OID values:
#   T1 (4.x): no sim_set_oid calls -> all OIDs return 0 (default scenario) -> empty series initially
#   T2 (5.x): no sim_set_oid calls -> all OIDs return 0 (default scenario) -> empty series initially
#   T3 (6.x): eval=10, res1=1, res2=1 -> Healthy (primed; would be evaluated if gate passed)
#   T4 (7.x): eval=10, res1=1, res2=1 -> Healthy (primed; would be evaluated if gate passed)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply 4-tenant fixture
# ---------------------------------------------------------------------------

log_info "SNS-B2: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "SNS-B2: Applying 4-tenant fixture..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg05-four-tenant-snapshot.yaml" > /dev/null 2>&1 || true

log_info "SNS-B2: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "SNS-B2: Tenant vector reload confirmed"
else
    log_warn "SNS-B2: Tenant vector reload log not found within 60s — proceeding anyway"
fi

# Prime only G2 tenants (T3 and T4) so they would be evaluated if gate passed.
# G1 tenants (T1, T2) are NOT primed — their holders remain empty -> Not Ready.
sim_set_oid "6.1" "10"    # T3 eval
sim_set_oid "6.2" "1"     # T3 res1
sim_set_oid "6.3" "1"     # T3 res2
sim_set_oid "7.1" "10"    # T4 eval
sim_set_oid "7.2" "1"     # T4 res1
sim_set_oid "7.3" "1"     # T4 res2

# DO NOT prime G1 tenants — they must stay in not-ready state for the assertion window.
# DO NOT sleep 8 — we must assert "not ready" before the 6s grace window expires.

# ---------------------------------------------------------------------------
# === SNS-B2a: G1-T1 is "not ready" (within grace window) ===
# Short timeout (5s) — must catch the "not ready" log before grace expires.
# ---------------------------------------------------------------------------

log_info "SNS-B2a: Polling for G1-T1 not ready log (short timeout 5s)..."
if poll_until_log 5 1 "e2e-tenant-G1-T1.*not ready" 5; then
    record_pass "SNS-B2a: G1-T1 not ready (in grace window)" "log=G1T1_not_ready_found"
else
    record_fail "SNS-B2a: G1-T1 not ready (in grace window)" "not ready log for G1-T1 not found within 5s"
fi

# ---------------------------------------------------------------------------
# === SNS-B2b: G1-T2 is "not ready" (within grace window) ===
# ---------------------------------------------------------------------------

log_info "SNS-B2b: Polling for G1-T2 not ready log (short timeout 5s)..."
if poll_until_log 5 1 "e2e-tenant-G1-T2.*not ready" 5; then
    record_pass "SNS-B2b: G1-T2 not ready (in grace window)" "log=G1T2_not_ready_found"
else
    record_fail "SNS-B2b: G1-T2 not ready (in grace window)" "not ready log for G1-T2 not found within 5s"
fi

# ---------------------------------------------------------------------------
# === SNS-B2c: Gate blocks -- G2 NOT evaluated (negative assertion) ===
# G1 not ready -> gate blocks -> G2 must have no tier logs.
# Use sleep 5 (not 10) and --since=10s to stay within the not-ready window.
# ---------------------------------------------------------------------------

log_info "SNS-B2c: Waiting 5s to accumulate SnapshotJob cycles while gate is blocked..."
sleep 5

log_info "SNS-B2c: Checking G2 tier logs absent while G1 not ready..."
PODS=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true
G2_FOUND=0
for POD in $PODS; do
    G2_LOGS=$(kubectl logs "$POD" -n simetra --since=10s 2>/dev/null \
        | grep "e2e-tenant-G2.*tier=" || echo "") || true
    if [ -n "$G2_LOGS" ]; then
        G2_FOUND=1
        log_info "SNS-B2c: UNEXPECTED G2 tier log in pod ${POD}: ${G2_LOGS}"
        break
    fi
done

if [ "$G2_FOUND" -eq 0 ]; then
    record_pass "SNS-B2c: G2 not evaluated (gate blocked — G1 not ready)" "G2 tier logs absent in 10s window"
else
    record_fail "SNS-B2c: G2 not evaluated (gate blocked — G1 not ready)" "G2 tier log found — gate did NOT block"
fi

# ---------------------------------------------------------------------------
# Cleanup: reset OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "SNS-B2: Resetting OID overrides..."
reset_oid_overrides

log_info "SNS-B2: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "SNS-B2: Failed to restore tenant ConfigMap"
else
    log_warn "SNS-B2: Original tenant ConfigMap snapshot not found — skipping restore"
fi
