# Scenario 62: PSS-14 All G1 Resolved -- gate passes, G2 evaluated
# Uses tenant-cfg08-pss-four-tenant.yaml (G1: e2e-pss-g1-t1 + e2e-pss-g1-t2 Priority=1,
#                                          G2: e2e-pss-g2-t3 + e2e-pss-g2-t4 Priority=2)
#
# Gate rule: ALL G1 tenants must be Resolved or Healthy for gate to pass.
# This scenario: T1 res1=0, res2=0 (violated) -> Resolved (tier=2)
#                T2 res1=0, res2=0 (violated) -> Resolved (tier=2)
#                -> ALL G1 Resolved -> gate PASSES -> G2 tenants evaluated
#
# Per-OID values:
#   T1 (4.x): eval=10 (in-range >= Min:10), res1=0, res2=0 (both violated < Min:1) -> Resolved
#   T2 (5.x): eval=10 (in-range >= Min:10), res1=0, res2=0 (both violated < Min:1) -> Resolved
#   T3 (6.x): eval=10, res1=1, res2=1 (in-range from priming) -> Healthy
#   T4 (7.x): eval=10, res1=1, res2=1 (in-range from priming) -> Healthy
#
# NOTE: Fixture apply/restore is managed by run-stage3.sh (not this scenario).
#       This scenario re-primes all OIDs to healthy before manipulating state.

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Re-prime all 4 tenants to healthy state (cross-scenario isolation)
# ---------------------------------------------------------------------------

log_info "PSS-14: Re-priming all 4 tenants to healthy state..."
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

log_info "PSS-14: Waiting 8s for readiness grace (TimeSeriesSize=3, IntervalSeconds=1, GraceMultiplier=2 -> grace=6s)..."
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
# === PSS-14a: G1-T1 reaches tier=2 Resolved ===
# ---------------------------------------------------------------------------

log_info "PSS-14a: Polling for e2e-pss-g1-t1 tier=2 Resolved log..."
if poll_until_log 30 1 "e2e-pss-g1-t1.*tier=2" 15; then
    record_pass "PSS-14a: G1-T1 tier=2 Resolved" "log=tier2_G1T1_found"
else
    record_fail "PSS-14a: G1-T1 tier=2 Resolved" "tier=2 log for e2e-pss-g1-t1 not found within 30s"
fi

# ---------------------------------------------------------------------------
# === PSS-14b: G1-T2 reaches tier=2 Resolved ===
# ---------------------------------------------------------------------------

log_info "PSS-14b: Polling for e2e-pss-g1-t2 tier=2 Resolved log..."
if poll_until_log 30 1 "e2e-pss-g1-t2.*tier=2" 15; then
    record_pass "PSS-14b: G1-T2 tier=2 Resolved" "log=tier2_G1T2_found"
else
    record_fail "PSS-14b: G1-T2 tier=2 Resolved" "tier=2 log for e2e-pss-g1-t2 not found within 30s"
fi

# ---------------------------------------------------------------------------
# === PSS-14c: Gate passes -- G2-T3 shows tier=3 Healthy ===
# G2-T3 OIDs are in-range from priming -> expect tier=3 (stronger proof).
# Asserting tier=3 specifically (not just tier=) proves gate passed AND G2 is Healthy.
# ---------------------------------------------------------------------------

log_info "PSS-14c: Polling for e2e-pss-g2-t3 tier=3 log (gate-pass assertion)..."
if poll_until_log 30 1 "e2e-pss-g2-t3.*tier=3" 15; then
    record_pass "PSS-14c: G2-T3 tier=3 (gate passed -- all G1 Resolved)" "log=tier3_G2T3_found"
else
    record_fail "PSS-14c: G2-T3 tier=3 (gate passed -- all G1 Resolved)" "tier=3 log for e2e-pss-g2-t3 not found within 30s -- gate may have blocked"
fi

# ---------------------------------------------------------------------------
# === PSS-14d: Gate passes -- G2-T4 shows tier=3 Healthy ===
# ---------------------------------------------------------------------------

log_info "PSS-14d: Polling for e2e-pss-g2-t4 tier=3 log (gate-pass assertion)..."
if poll_until_log 30 1 "e2e-pss-g2-t4.*tier=3" 15; then
    record_pass "PSS-14d: G2-T4 tier=3 (gate passed -- all G1 Resolved)" "log=tier3_G2T4_found"
else
    record_fail "PSS-14d: G2-T4 tier=3 (gate passed -- all G1 Resolved)" "tier=3 log for e2e-pss-g2-t4 not found within 30s -- gate may have blocked"
fi
