# Scenario 45: SNS-05 Healthy -- tier=3 when all metrics are in-range (no action)
# Uses tenant-cfg05-four-tenant-snapshot.yaml
# T1 OIDs: 4.1 = evaluate (Min:10), 4.2 = res1 (Min:1), 4.3 = res2 (Min:1)
#
# Tier=3 fires when:
#   - Resolved holders are NOT all violated (tier=2 gate passes)
#   - Evaluate holder is NOT violated (>= Min:10)
#   => All metrics in-range: tenant is Healthy, no commands dispatched
#
# After priming, T1 OIDs are already in healthy state:
#   - eval=10 (>= Min:10, not violated)
#   - res1=1 (>= Min:1, not violated)
#   - res2=1 (>= Min:1, not violated)
# No additional sim_set_oid calls needed after priming.
#
# Sub-assertions:
#   45a: tier=3 healthy log with G1-T1 scope
#   45b: snmp_command_dispatched_total does NOT increment (negative assertion, delta=0)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply 4-tenant snapshot fixture
# ---------------------------------------------------------------------------

log_info "SNS-05: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "SNS-05: Applying tenant-cfg05-four-tenant-snapshot.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg05-four-tenant-snapshot.yaml" > /dev/null 2>&1 || true

log_info "SNS-05: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "SNS-05: Tenant vector reload confirmed"
else
    log_warn "SNS-05: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime all 4 tenants with in-range values to pass readiness grace
# T1 eval=10 >= Min:10 (not violated), res1=1 >= Min:1 (not violated), res2=1 (not violated)
# After sleep 8s, T1 is ready and already in healthy state — no further OID changes needed
# T1: 4.1/4.2/4.3, T2: 5.1/5.2/5.3, T3: 6.1/6.2/6.3, T4: 7.1/7.2/7.3
# ---------------------------------------------------------------------------

log_info "SNS-05: Priming all 4 tenants for readiness grace (T1 primed to healthy state)..."
sim_set_oid "4.1" "10"   # T1 eval in-range (>= Min:10) -- healthy
sim_set_oid "4.2" "1"    # T1 res1 in-range (>= Min:1)  -- healthy
sim_set_oid "4.3" "1"    # T1 res2 in-range (>= Min:1)  -- healthy
sim_set_oid "5.1" "10"   # T2 eval
sim_set_oid "5.2" "1"    # T2 res1
sim_set_oid "5.3" "1"    # T2 res2
sim_set_oid "6.1" "10"   # T3 eval
sim_set_oid "6.2" "1"    # T3 res1
sim_set_oid "6.3" "1"    # T3 res2
sim_set_oid "7.1" "10"   # T4 eval
sim_set_oid "7.2" "1"    # T4 res1
sim_set_oid "7.3" "1"    # T4 res2

log_info "SNS-05: Waiting 8s for readiness grace..."
sleep 8

# ---------------------------------------------------------------------------
# Capture sent counter baseline AFTER priming sleep
# T1 is now ready and in healthy state. Baseline reflects any priming-phase commands.
# Delta over observation window must be 0 (no evaluate violation, no commands).
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-G1-T1"')
log_info "SNS-05: Baseline sent=${BEFORE_SENT} (after priming sleep)"

# ---------------------------------------------------------------------------
# Sub-scenario 45a: tier=3 healthy log
# T1 OIDs are in-range: tier=2 gate passes (not all resolved violated),
# tier=4 gate passes (evaluate not violated) => tier=3 Healthy logged
# ---------------------------------------------------------------------------

log_info "SNS-05: Polling for tier=3 healthy log (30s timeout)..."
if poll_until_log 30 1 "e2e-tenant-G1-T1.*tier=3" 15; then
    record_pass "SNS-05A: G1-T1 tier=3 healthy (all metrics in-range)" "log=tier3_healthy_found"
else
    record_fail "SNS-05A: G1-T1 tier=3 healthy (all metrics in-range)" "tier=3 log for G1-T1 not found within 30s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 45b: No command dispatch at tier=3 (negative assertion)
# Healthy state means no tier=4, no commands enqueued.
# Sleep 10s then compare counter. Delta must be 0.
# ---------------------------------------------------------------------------

log_info "SNS-05: Waiting 10s to confirm no command dispatch at tier=3..."
sleep 10

AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-G1-T1"')
DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
log_info "SNS-05: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"

if [ "$DELTA_SENT" -eq 0 ]; then
    record_pass "SNS-05B: No commands dispatched at tier=3 (healthy state, no action)" "sent_delta=${DELTA_SENT}"
else
    record_fail "SNS-05B: No commands dispatched at tier=3 (healthy state, no action)" "sent_delta=${DELTA_SENT} expected 0"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "SNS-05: Clearing OID overrides..."
reset_oid_overrides

log_info "SNS-05: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "SNS-05: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "SNS-05: Original tenant ConfigMap snapshot not found — skipping restore"
fi
