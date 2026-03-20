# Scenario 65: PSS-17 All G1 Unresolved -- gate blocks, G2 not evaluated
# Uses tenant-cfg08-pss-four-tenant.yaml (G1: e2e-pss-g1-t1 + e2e-pss-g1-t2 Priority=1,
#                                          G2: e2e-pss-g2-t3 + e2e-pss-g2-t4 Priority=2)
#
# Gate rule: blocks if ANY G1 tenant is Unresolved.
# This scenario: T1 eval=0 (< Min:10), res in-range -> Unresolved (tier=4)
#                T2 eval=0 (< Min:10), res in-range -> Unresolved (tier=4)
# -> ALL G1 Unresolved -> gate BLOCKS -> G2 tenants (T3, T4) are NOT evaluated.
#
# Per-OID values after priming then violation:
#   T1 (4.x): eval=0 (violated < Min:10), res1=1, res2=1 (in-range) -> Unresolved
#   T2 (5.x): eval=0 (violated < Min:10), res1=1, res2=1 (in-range) -> Unresolved
#   T3 (6.x): eval=10, res1=1, res2=1 -> Healthy (would be evaluated if gate passed)
#   T4 (7.x): eval=10, res1=1, res2=1 -> Healthy (would be evaluated if gate passed)
#
# Dual proof pattern:
#   PSS-17a/b: G1 positive assertion (tier=4 logs)
#   PSS-17c:   G2 log absence (sleep 10, --since=15s, G2_FOUND check)
#   PSS-17d:   G2 metric non-increment (snapshot_counter delta == 0 for T3 and T4)
#
# NOTE: Fixture apply/restore is managed by run-stage3.sh (not this scenario).
#       This scenario re-primes all OIDs to healthy before manipulating state.

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Re-prime all 4 tenants to healthy state (cross-scenario isolation)
# ---------------------------------------------------------------------------

log_info "PSS-17: Re-priming all 4 tenants to healthy state..."
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

log_info "PSS-17: Waiting 8s for readiness grace (TimeSeriesSize=3, IntervalSeconds=1, GraceMultiplier=2 -> grace=6s)..."
sleep 8

# ---------------------------------------------------------------------------
# Set both G1 tenants to Unresolved: eval violated, resolved stays in-range
# ---------------------------------------------------------------------------

# T1: eval violated -> tier=4 Unresolved (res1=1, res2=1 stay from priming)
sim_set_oid "4.1" "0"     # T1 eval violated (< Min:10)

# T2: eval violated -> tier=4 Unresolved (res1=1, res2=1 stay from priming)
sim_set_oid "5.1" "0"     # T2 eval violated (< Min:10)

# ---------------------------------------------------------------------------
# === PSS-17a: G1-T1 reaches tier=4 Unresolved ===
# ---------------------------------------------------------------------------

log_info "PSS-17a: Polling for e2e-pss-g1-t1 tier=4 Unresolved log..."
if poll_until_log 30 1 "e2e-pss-g1-t1.*tier=4" 15; then
    record_pass "PSS-17a: G1-T1 tier=4 Unresolved" "log=tier4_G1T1_found"
else
    record_fail "PSS-17a: G1-T1 tier=4 Unresolved" "tier=4 log for e2e-pss-g1-t1 not found within 30s"
fi

# ---------------------------------------------------------------------------
# === PSS-17b: G1-T2 reaches tier=4 Unresolved ===
# ---------------------------------------------------------------------------

log_info "PSS-17b: Polling for e2e-pss-g1-t2 tier=4 Unresolved log..."
if poll_until_log 30 1 "e2e-pss-g1-t2.*tier=4" 15; then
    record_pass "PSS-17b: G1-T2 tier=4 Unresolved" "log=tier4_G1T2_found"
else
    record_fail "PSS-17b: G1-T2 tier=4 Unresolved" "tier=4 log for e2e-pss-g1-t2 not found within 30s"
fi

# ---------------------------------------------------------------------------
# === PSS-17c/d: Gate blocks -- G2 NOT evaluated (dual proof) ===
# Both G1 tenants are Unresolved -> gate blocks -> G2 must have no tier logs
# AND G2 metric counters must not increment.
# Capture BEFORE snapshots, wait 10s, check log absence, then capture AFTER snapshots.
# ---------------------------------------------------------------------------

# Capture G2 BEFORE snapshots (right after G1 positive assertions confirmed)
BEFORE_T3=$(snapshot_counter "snmp_poll_executed_total" 'device_name="e2e-pss-g2-t3"')
BEFORE_T4=$(snapshot_counter "snmp_poll_executed_total" 'device_name="e2e-pss-g2-t4"')
log_info "PSS-17c/d: G2 BEFORE snapshots -- T3=$BEFORE_T3, T4=$BEFORE_T4"

log_info "PSS-17c: Waiting 10s to accumulate SnapshotJob cycles while gate is blocked..."
sleep 10

log_info "PSS-17c: Checking G2 tier logs absent (gate-block assertion -- all G1 Unresolved)..."
PODS=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true
G2_FOUND=0
for POD in $PODS; do
    G2_LOGS=$(kubectl logs "$POD" -n simetra --since=15s 2>/dev/null \
        | grep "e2e-pss-g2.*tier=" || echo "") || true
    if [ -n "$G2_LOGS" ]; then
        G2_FOUND=1
        log_info "PSS-17c: UNEXPECTED G2 tier log in pod ${POD}: ${G2_LOGS}"
        break
    fi
done

if [ "$G2_FOUND" -eq 0 ]; then
    record_pass "PSS-17c: G2 not evaluated -- log absence (gate blocked -- all G1 Unresolved)" "G2 tier logs absent in 15s window"
else
    record_fail "PSS-17c: G2 not evaluated -- log absence (gate blocked -- all G1 Unresolved)" "G2 tier log found -- gate did NOT block"
fi

# ---------------------------------------------------------------------------
# === PSS-17d: G2 metric non-increment ===
# ---------------------------------------------------------------------------

AFTER_T3=$(snapshot_counter "snmp_poll_executed_total" 'device_name="e2e-pss-g2-t3"')
AFTER_T4=$(snapshot_counter "snmp_poll_executed_total" 'device_name="e2e-pss-g2-t4"')
DELTA_T3=$((AFTER_T3 - BEFORE_T3))
DELTA_T4=$((AFTER_T4 - BEFORE_T4))
log_info "PSS-17d: G2 AFTER snapshots -- T3=$AFTER_T3 (delta=$DELTA_T3), T4=$AFTER_T4 (delta=$DELTA_T4)"

if [ "$DELTA_T3" -eq 0 ] && [ "$DELTA_T4" -eq 0 ]; then
    record_pass "PSS-17d: G2 not evaluated -- metric non-increment (T3 delta=$DELTA_T3, T4 delta=$DELTA_T4)" "snmp_poll_executed_total unchanged for G2 tenants"
else
    record_fail "PSS-17d: G2 not evaluated -- metric non-increment (T3 delta=$DELTA_T3, T4 delta=$DELTA_T4)" "G2 poll counter incremented -- gate did NOT block"
fi
