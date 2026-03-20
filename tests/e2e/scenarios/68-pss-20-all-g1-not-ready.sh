# Scenario 68: PSS-20 All G1 Not Ready -- gate blocks, G2 not evaluated
# Uses tenant-cfg08-pss-four-tenant.yaml (G1: e2e-pss-g1-t1 + e2e-pss-g1-t2 Priority=1,
#                                          G2: e2e-pss-g2-t3 + e2e-pss-g2-t4 Priority=2)
#
# Gate rule: blocks if ANY G1 tenant is Unresolved (Not Ready is an Unresolved result).
# This scenario: G1 tenants receive no data -> IsReady = false (empty series) -> Not Ready
# -> ANY G1 not ready -> gate BLOCKS -> G2 tenants (T3, T4) are NOT evaluated.
#
# Timing note: ReadinessGrace = TimeSeriesSize(3) * IntervalSeconds(1) * GraceMultiplier(2) = 6s
# After 6s, IsReady = (ReadSeries().Length > 0) || (UtcNow >= ConstructedAt + 6s).
# We assert the "not ready" log quickly, BEFORE the 6s grace window expires.
# No grace sleep needed -- must assert "not ready" before grace expires.
#
# Per-OID values:
#   T1 (4.x): no sim_set_oid calls -> empty series -> Not Ready
#   T2 (5.x): no sim_set_oid calls -> empty series -> Not Ready
#   T3 (6.x): eval=10, res1=1, res2=1 -> Healthy (primed; would be evaluated if gate passed)
#   T4 (7.x): eval=10, res1=1, res2=1 -> Healthy (primed; would be evaluated if gate passed)
#
# SPECIAL: This scenario re-applies the 4-tenant fixture to force fresh tenant holders
# with empty G1 series (reset_oid_overrides alone is insufficient -- prior scenario
# may have left G1 holders with populated series). Re-applying the configmap forces
# a fresh TenantVectorWatcher reload with new holders starting from empty.
#
# Dual proof pattern:
#   PSS-20a/b: G1 not-ready assertions (short 5s timeout, within grace window)
#   PSS-20c:   G2 log absence (sleep 5, --since=10s, G2_FOUND check -- short window)
#   PSS-20d:   G2 metric non-increment (snapshot_counter delta == 0 for T3 and T4)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: Reset OID overrides, re-apply fixture for fresh tenant holders
# ---------------------------------------------------------------------------

log_info "PSS-20: Resetting OID overrides to clear any prior scenario state..."
reset_oid_overrides

log_info "PSS-20: Re-applying 4-tenant fixture to force fresh tenant vector reload (new holders with empty G1 series)..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg08-pss-four-tenant.yaml" > /dev/null 2>&1 || true

log_info "PSS-20: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "PSS-20: Tenant vector reload confirmed"
else
    log_warn "PSS-20: Tenant vector reload log not found within 60s -- proceeding anyway"
fi

# ---------------------------------------------------------------------------
# Prime ONLY G2 tenants (T3, T4) -- G1 must stay empty for Not Ready assertion
# DO NOT prime G1 (4.x, 5.x) -- their holders stay empty -> Not Ready
# No grace sleep -- must assert "not ready" BEFORE the 6s grace window expires
# ---------------------------------------------------------------------------

log_info "PSS-20: Priming G2 tenants (T3, T4) only -- G1 stays empty (Not Ready)..."
sim_set_oid "6.1" "10"    # T3 eval
sim_set_oid "6.2" "1"     # T3 res1
sim_set_oid "6.3" "1"     # T3 res2
sim_set_oid "7.1" "10"    # T4 eval
sim_set_oid "7.2" "1"     # T4 res1
sim_set_oid "7.3" "1"     # T4 res2

# Capture G2 BEFORE snapshots immediately after G2 priming
BEFORE_T3=$(snapshot_counter "snmp_poll_executed_total" 'device_name="e2e-pss-g2-t3"')
BEFORE_T4=$(snapshot_counter "snmp_poll_executed_total" 'device_name="e2e-pss-g2-t4"')
log_info "PSS-20: G2 BEFORE snapshots -- T3=$BEFORE_T3, T4=$BEFORE_T4"

# ---------------------------------------------------------------------------
# === PSS-20a: G1-T1 is "not ready" (within grace window) ===
# Short timeout (5s) -- must catch the "not ready" log before grace expires.
# ---------------------------------------------------------------------------

log_info "PSS-20a: Polling for e2e-pss-g1-t1 not ready log (short timeout 5s, within grace window)..."
if poll_until_log 5 1 "e2e-pss-g1-t1.*not ready" 5; then
    record_pass "PSS-20a: G1-T1 not ready (in grace window)" "log=G1T1_not_ready_found"
else
    record_fail "PSS-20a: G1-T1 not ready (in grace window)" "not ready log for e2e-pss-g1-t1 not found within 5s"
fi

# ---------------------------------------------------------------------------
# === PSS-20b: G1-T2 is "not ready" (within grace window) ===
# ---------------------------------------------------------------------------

log_info "PSS-20b: Polling for e2e-pss-g1-t2 not ready log (short timeout 5s, within grace window)..."
if poll_until_log 5 1 "e2e-pss-g1-t2.*not ready" 5; then
    record_pass "PSS-20b: G1-T2 not ready (in grace window)" "log=G1T2_not_ready_found"
else
    record_fail "PSS-20b: G1-T2 not ready (in grace window)" "not ready log for e2e-pss-g1-t2 not found within 5s"
fi

# ---------------------------------------------------------------------------
# === PSS-20c/d: Gate blocks -- G2 NOT evaluated (dual proof) ===
# G1 not ready -> gate blocks -> G2 must have no tier logs.
# Use shorter window (sleep 5, --since=10s) to stay within the not-ready window.
# Capture AFTER snapshots, then assert delta == 0.
# ---------------------------------------------------------------------------

log_info "PSS-20c: Waiting 5s to accumulate SnapshotJob cycles while gate is blocked (short window)..."
sleep 5

log_info "PSS-20c: Checking G2 tier logs absent while G1 not ready (--since=10s)..."
PODS=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true
G2_FOUND=0
for POD in $PODS; do
    G2_LOGS=$(kubectl logs "$POD" -n simetra --since=10s 2>/dev/null \
        | grep "e2e-pss-g2.*tier=" || echo "") || true
    if [ -n "$G2_LOGS" ]; then
        G2_FOUND=1
        log_info "PSS-20c: UNEXPECTED G2 tier log in pod ${POD}: ${G2_LOGS}"
        break
    fi
done

if [ "$G2_FOUND" -eq 0 ]; then
    record_pass "PSS-20c: G2 not evaluated -- log absence (gate blocked -- G1 not ready)" "G2 tier logs absent in 10s window"
else
    record_fail "PSS-20c: G2 not evaluated -- log absence (gate blocked -- G1 not ready)" "G2 tier log found -- gate did NOT block"
fi

# ---------------------------------------------------------------------------
# === PSS-20d: G2 metric non-increment ===
# ---------------------------------------------------------------------------

AFTER_T3=$(snapshot_counter "snmp_poll_executed_total" 'device_name="e2e-pss-g2-t3"')
AFTER_T4=$(snapshot_counter "snmp_poll_executed_total" 'device_name="e2e-pss-g2-t4"')
DELTA_T3=$((AFTER_T3 - BEFORE_T3))
DELTA_T4=$((AFTER_T4 - BEFORE_T4))
log_info "PSS-20d: G2 AFTER snapshots -- T3=$AFTER_T3 (delta=$DELTA_T3), T4=$AFTER_T4 (delta=$DELTA_T4)"

if [ "$DELTA_T3" -eq 0 ] && [ "$DELTA_T4" -eq 0 ]; then
    record_pass "PSS-20d: G2 not evaluated -- metric non-increment (T3 delta=$DELTA_T3, T4 delta=$DELTA_T4)" "snmp_poll_executed_total unchanged for G2 tenants"
else
    record_fail "PSS-20d: G2 not evaluated -- metric non-increment (T3 delta=$DELTA_T3, T4 delta=$DELTA_T4)" "G2 poll counter incremented -- gate did NOT block"
fi
