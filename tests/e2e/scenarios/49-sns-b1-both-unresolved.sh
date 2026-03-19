# Scenario 49: SNS-B1 Advance gate block -- both G1 tenants Unresolved
# Uses tenant-cfg05-four-tenant-snapshot.yaml (G1: T1+T2 priority=1, G2: T3+T4 priority=2)
#
# Gate rule: blocks if ANY G1 tenant is Unresolved.
# This scenario: T1 eval=0 (< Min:10), res in-range -> Unresolved (tier=4 commands enqueued)
#                T2 eval=0 (< Min:10), res in-range -> Unresolved (tier=4 commands enqueued)
# -> ANY G1 Unresolved -> gate BLOCKS -> G2 tenants (T3, T4) are NOT evaluated.
#
# Per-OID values after priming:
#   T1 (4.x): eval=0 (violated < Min:10), res1=1, res2=1 (in-range) -> Unresolved
#   T2 (5.x): eval=0 (violated < Min:10), res1=1, res2=1 (in-range) -> Unresolved
#   T3 (6.x): eval=10, res1=1, res2=1 -> Healthy (would be evaluated if gate passed)
#   T4 (7.x): eval=10, res1=1, res2=1 -> Healthy (would be evaluated if gate passed)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply 4-tenant fixture
# ---------------------------------------------------------------------------

log_info "SNS-B1: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "SNS-B1: Applying 4-tenant fixture..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg05-four-tenant-snapshot.yaml" > /dev/null 2>&1 || true

log_info "SNS-B1: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "SNS-B1: Tenant vector reload confirmed"
else
    log_warn "SNS-B1: Tenant vector reload log not found within 60s — proceeding anyway"
fi

# Prime all 4 tenants to healthy state (passes readiness grace)
sim_set_oid "4.1" "10"    # T1 eval
sim_set_oid "4.2" "1"     # T1 res1
sim_set_oid "4.3" "1"     # T1 res2
sim_set_oid "5.1" "10"    # T2 eval
sim_set_oid "5.2" "1"     # T2 res1
sim_set_oid "5.3" "1"     # T2 res2
sim_set_oid "6.1" "10"    # T3 eval
sim_set_oid "6.2" "1"     # T3 res1
sim_set_oid "6.3" "1"     # T3 res2
sim_set_oid "7.1" "10"    # T4 eval
sim_set_oid "7.2" "1"     # T4 res1
sim_set_oid "7.3" "1"     # T4 res2

log_info "SNS-B1: Waiting 8s for readiness grace (TimeSeriesSize=3, IntervalSeconds=1, GraceMultiplier=2 -> grace=6s)..."
sleep 8

# ---------------------------------------------------------------------------
# Set both G1 tenants to Unresolved: resolved in-range, evaluate violated
# ---------------------------------------------------------------------------

# T1: eval violated -> tier=4 Unresolved (commands enqueued)
sim_set_oid "4.1" "0"     # T1 eval violated (< Min:10)
# T1 res1=1, res2=1 stay from priming (in-range)

# T2: eval violated -> tier=4 Unresolved (commands enqueued)
sim_set_oid "5.1" "0"     # T2 eval violated (< Min:10)
# T2 res1=1, res2=1 stay from priming (in-range)

# ---------------------------------------------------------------------------
# === SNS-B1a: G1-T1 reaches tier=4 Unresolved (commands enqueued) ===
# ---------------------------------------------------------------------------

log_info "SNS-B1a: Polling for G1-T1 tier=4 log..."
if poll_until_log 30 1 "e2e-tenant-G1-T1.*tier=4 — commands enqueued\|e2e-tenant-G1-T1.*tier=4 -- commands enqueued" 15; then
    record_pass "SNS-B1a: G1-T1 tier=4 Unresolved" "log=tier4_G1T1_found"
else
    record_fail "SNS-B1a: G1-T1 tier=4 Unresolved" "tier=4 log for G1-T1 not found within 30s"
fi

# ---------------------------------------------------------------------------
# === SNS-B1b: G1-T2 reaches tier=4 Unresolved (commands enqueued) ===
# ---------------------------------------------------------------------------

log_info "SNS-B1b: Polling for G1-T2 tier=4 log..."
if poll_until_log 30 1 "e2e-tenant-G1-T2.*tier=4 — commands enqueued\|e2e-tenant-G1-T2.*tier=4 -- commands enqueued" 15; then
    record_pass "SNS-B1b: G1-T2 tier=4 Unresolved" "log=tier4_G1T2_found"
else
    record_fail "SNS-B1b: G1-T2 tier=4 Unresolved" "tier=4 log for G1-T2 not found within 30s"
fi

# ---------------------------------------------------------------------------
# === SNS-B1c: Gate blocks -- G2 NOT evaluated (negative assertion) ===
# Both G1 tenants are Unresolved -> gate blocks -> G2 tier logs must be absent.
# Wait 10s to accumulate several SnapshotJob cycles (1s interval = 10 cycles),
# then check for absence of G2 tier logs in the last 15s window.
# ---------------------------------------------------------------------------

log_info "SNS-B1c: Waiting 10s to accumulate SnapshotJob cycles while gate is blocked..."
sleep 10

log_info "SNS-B1c: Checking G2 tier logs absent (gate-block assertion)..."
PODS=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true
G2_FOUND=0
for POD in $PODS; do
    G2_LOGS=$(kubectl logs "$POD" -n simetra --since=15s 2>/dev/null \
        | grep "e2e-tenant-G2.*tier=" || echo "") || true
    if [ -n "$G2_LOGS" ]; then
        G2_FOUND=1
        log_info "SNS-B1c: UNEXPECTED G2 tier log in pod ${POD}: ${G2_LOGS}"
        break
    fi
done

if [ "$G2_FOUND" -eq 0 ]; then
    record_pass "SNS-B1c: G2 not evaluated (gate blocked — both G1 Unresolved)" "G2 tier logs absent in 15s window"
else
    record_fail "SNS-B1c: G2 not evaluated (gate blocked — both G1 Unresolved)" "G2 tier log found — gate did NOT block"
fi

# ---------------------------------------------------------------------------
# Cleanup: reset OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "SNS-B1: Resetting OID overrides..."
reset_oid_overrides

log_info "SNS-B1: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "SNS-B1: Failed to restore tenant ConfigMap"
else
    log_warn "SNS-B1: Original tenant ConfigMap snapshot not found — skipping restore"
fi
