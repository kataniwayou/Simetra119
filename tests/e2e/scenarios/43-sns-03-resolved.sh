# Scenario 43: SNS-03 All Resolved Violated -- tier=2 stops evaluation (no commands)
# Uses tenant-cfg05-four-tenant-snapshot.yaml
# T1 OIDs: 4.1 = evaluate (Min:10), 4.2 = res1 (Min:1), 4.3 = res2 (Min:1)
#
# Tier=2 fires when ALL resolved holders are violated (both < Min:1 = value 0).
# No commands are enqueued at tier=2 — evaluation stops early.
#
# Sub-assertions:
#   43a: tier=2 log "all resolved violated" with G1-T1 scope
#   43b: snmp_command_dispatched_total does NOT increment (negative assertion, delta=0)
#   43c (partial resolved violation): one=0, one=1 does NOT produce tier=2 but continues to tier=3
#
# For 43c: only ONE resolved OID is violated (res1=0). res2 remains 1 (not violated).
# AreAllResolvedViolated requires BOTH to be violated; partial violation proceeds to tier=3.
# With eval=10 (in-range), the tenant reaches tier=3 healthy.

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply 4-tenant snapshot fixture
# ---------------------------------------------------------------------------

log_info "SNS-03: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "SNS-03: Applying tenant-cfg05-four-tenant-snapshot.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg05-four-tenant-snapshot.yaml" > /dev/null 2>&1 || true

log_info "SNS-03: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "SNS-03: Tenant vector reload confirmed"
else
    log_warn "SNS-03: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime all 4 tenants with in-range values to pass readiness grace
# T1: 4.1/4.2/4.3, T2: 5.1/5.2/5.3, T3: 6.1/6.2/6.3, T4: 7.1/7.2/7.3
# ---------------------------------------------------------------------------

log_info "SNS-03: Priming all 4 tenants for readiness grace..."
sim_set_oid "4.1" "10"   # T1 eval
sim_set_oid "4.2" "1"    # T1 res1
sim_set_oid "4.3" "1"    # T1 res2
sim_set_oid "5.1" "10"   # T2 eval
sim_set_oid "5.2" "1"    # T2 res1
sim_set_oid "5.3" "1"    # T2 res2
sim_set_oid "6.1" "10"   # T3 eval
sim_set_oid "6.2" "1"    # T3 res1
sim_set_oid "6.3" "1"    # T3 res2
sim_set_oid "7.1" "10"   # T4 eval
sim_set_oid "7.2" "1"    # T4 res1
sim_set_oid "7.3" "1"    # T4 res2

log_info "SNS-03: Waiting 8s for readiness grace..."
sleep 8

# ---------------------------------------------------------------------------
# Capture sent counter baseline BEFORE setting resolved OIDs to violated
# Delta after tier=2 fires must be 0 (no commands at tier=2)
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-G1-T1"')
log_info "SNS-03: Baseline sent=${BEFORE_SENT}"

# ---------------------------------------------------------------------------
# Set T1 resolved OIDs to violated (both < Min:1 = value 0)
# T1 eval stays at 10 (in-range) — tier=2 fires before tier=3 check
# ---------------------------------------------------------------------------

log_info "SNS-03: Setting T1 resolved OIDs to violated (both=0)..."
sim_set_oid "4.2" "0"    # T1 res1 violated (< Min:1)
sim_set_oid "4.3" "0"    # T1 res2 violated (< Min:1)

# ---------------------------------------------------------------------------
# Sub-scenario 43a: tier=2 "all resolved violated" log
# SnapshotJob: AreAllResolvedViolated=true -> logs tier=2 and returns early
# ---------------------------------------------------------------------------

log_info "SNS-03: Polling for tier=2 all resolved violated log (30s timeout)..."
if poll_until_log 30 1 "e2e-tenant-G1-T1.*tier=2 — all resolved violated\|e2e-tenant-G1-T1.*tier=2 -- all resolved violated\|e2e-tenant-G1-T1.*tier=2" 15; then
    record_pass "SNS-03A: G1-T1 tier=2 all resolved violated" "log=tier2_resolved_violated_found"
else
    record_fail "SNS-03A: G1-T1 tier=2 all resolved violated" "tier=2 log for G1-T1 not found within 30s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 43b: No command dispatch at tier=2 (negative assertion)
# Tier=2 returns early -- no tier=4 evaluation, no commands enqueued.
# Sleep 10s then compare counter. Delta must be 0.
# ---------------------------------------------------------------------------

log_info "SNS-03: Waiting 10s to confirm no command dispatch at tier=2..."
sleep 10

AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-G1-T1"')
DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
log_info "SNS-03: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"

if [ "$DELTA_SENT" -eq 0 ]; then
    record_pass "SNS-03B: No commands dispatched at tier=2 (resolved gate stops evaluation)" "sent_delta=${DELTA_SENT}"
else
    record_fail "SNS-03B: No commands dispatched at tier=2 (resolved gate stops evaluation)" "sent_delta=${DELTA_SENT} expected 0"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 43c: Partial resolved violation (one=0, one=1)
# AreAllResolvedViolated requires ALL violated; one=0 one=1 is NOT "all violated".
# Evaluation continues past tier=2 to tier=3.
# With eval=10 (in-range from priming), T1 reaches tier=3 healthy.
# ---------------------------------------------------------------------------

log_info "SNS-03C: Testing partial resolved violation — one=0, one=1 should NOT trigger tier=2..."

# Reset OID overrides and re-prime T1 to flush tier=2 log state
reset_oid_overrides
sim_set_oid "4.1" "10"   # T1 eval in-range
sim_set_oid "4.2" "1"    # T1 res1 in-range
sim_set_oid "4.3" "1"    # T1 res2 in-range

log_info "SNS-03C: Waiting 3s for healthy cycles to flush prior tier=2 logs..."
sleep 3

# Set partial violation: res1=0 (violated), res2=1 (not violated)
log_info "SNS-03C: Setting partial violation: res1=0, res2=1..."
sim_set_oid "4.2" "0"    # T1 res1 violated (< Min:1)
# T1 res2 remains 1 from sim_set_oid above (not violated)

log_info "SNS-03C: Polling for tier=3 healthy log with partial resolved violation (30s timeout)..."
if poll_until_log 30 1 "e2e-tenant-G1-T1.*tier=3" 10; then
    record_pass "SNS-03C: Partial resolved violation reaches tier=3 (not tier=2)" "log=tier3_found_with_partial_violation"
else
    record_fail "SNS-03C: Partial resolved violation reaches tier=3 (not tier=2)" "tier=3 log not found within 30s for partial violation"
fi

# Verify tier=2 is ABSENT in the same recent window (partial violation must NOT trigger tier=2)
log_info "SNS-03C: Checking tier=2 is absent in --since=10s window..."
PODS=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true
TIER2_FOUND=0
for POD in $PODS; do
    TIER2_LOGS=$(kubectl logs "$POD" -n simetra --since=10s 2>/dev/null \
        | grep "e2e-tenant-G1-T1.*tier=2" || echo "") || true
    if [ -n "$TIER2_LOGS" ]; then
        TIER2_FOUND=1
        log_info "SNS-03C: UNEXPECTED tier=2 log found in pod ${POD}: ${TIER2_LOGS}"
        break
    fi
done

if [ "$TIER2_FOUND" -eq 0 ]; then
    record_pass "SNS-03C: tier=2 absent with partial resolved violation" "tier=2 log absent in 10s window (partial violation correctly skips tier=2)"
else
    record_fail "SNS-03C: tier=2 absent with partial resolved violation" "tier=2 log found unexpectedly — partial violation should NOT fire tier=2"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "SNS-03: Clearing OID overrides..."
reset_oid_overrides

log_info "SNS-03: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "SNS-03: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "SNS-03: Original tenant ConfigMap snapshot not found — skipping restore"
fi
