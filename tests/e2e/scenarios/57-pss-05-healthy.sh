# Scenario 57: PSS-05 Healthy -- tier=3 when all metrics are in-range (no action)
# Uses tenant-cfg06-pss-single.yaml (e2e-pss-tenant: IntervalSeconds=1, GraceMultiplier=2.0)
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
#
# Tier=3 fires when:
#   - Resolved holders are NOT all violated (tier=2 gate passes)
#   - Evaluate holder is NOT violated (>= Min:10)
#   => All metrics in-range: tenant is Healthy, no commands dispatched
#
# After priming, T2 OIDs are already in healthy state:
#   - eval=10 (>= Min:10, not violated)
#   - res1=1 (>= Min:1, not violated)
#   - res2=1 (>= Min:1, not violated)
# No additional sim_set_oid calls needed after priming.
#
# Sub-assertions:
#   57a: tier=3 healthy log with e2e-pss-tenant scope
#   57b: snmp_command_dispatched_total does NOT increment (negative assertion, delta=0)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply single-tenant PSS fixture
# ---------------------------------------------------------------------------

log_info "PSS-05: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "PSS-05: Applying tenant-cfg06-pss-single.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-single.yaml" > /dev/null 2>&1 || true

log_info "PSS-05: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "PSS-05: Tenant vector reload confirmed"
else
    log_warn "PSS-05: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime T2 OIDs with in-range values to pass readiness grace
# T2 eval=10 >= Min:10 (not violated), res1=1 >= Min:1 (not violated), res2=1 (not violated)
# After sleep 8s, tenant is ready and already in healthy state -- no further OID changes needed
# ---------------------------------------------------------------------------

log_info "PSS-05: Priming T2 OIDs with in-range values (tenant primed to healthy state)..."
sim_set_oid "5.1" "10"   # T2 eval in-range (>= Min:10) -- healthy
sim_set_oid "5.2" "1"    # T2 res1 in-range (>= Min:1)  -- healthy
sim_set_oid "5.3" "1"    # T2 res2 in-range (>= Min:1)  -- healthy

log_info "PSS-05: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Capture sent counter baseline AFTER priming sleep
# Tenant is now ready and in healthy state. Baseline reflects any priming-phase commands.
# Delta over observation window must be 0 (no evaluate violation, no commands).
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
log_info "PSS-05: Baseline sent=${BEFORE_SENT} (after priming sleep)"

# ---------------------------------------------------------------------------
# Sub-scenario 57a: tier=3 healthy log
# T2 OIDs are in-range: tier=2 gate passes (not all resolved violated),
# tier=4 gate passes (evaluate not violated) => tier=3 Healthy logged
# ---------------------------------------------------------------------------

log_info "PSS-05: Polling for tier=3 healthy log (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-tenant.*tier=3" 15; then
    record_pass "PSS-05A: e2e-pss-tenant tier=3 healthy (all metrics in-range)" "log=tier3_healthy_found"
else
    record_fail "PSS-05A: e2e-pss-tenant tier=3 healthy (all metrics in-range)" "tier=3 log for e2e-pss-tenant not found within 30s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 57b: No command dispatch at tier=3 (negative assertion)
# Healthy state means no tier=4, no commands enqueued.
# Sleep 10s then compare counter. Delta must be 0.
# ---------------------------------------------------------------------------

log_info "PSS-05: Waiting 10s to confirm no command dispatch at tier=3..."
sleep 10

AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
log_info "PSS-05: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"

if [ "$DELTA_SENT" -eq 0 ]; then
    record_pass "PSS-05B: No commands dispatched at tier=3 (healthy state, no action)" "sent_delta=${DELTA_SENT}"
else
    record_fail "PSS-05B: No commands dispatched at tier=3 (healthy state, no action)" "sent_delta=${DELTA_SENT} expected 0"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "PSS-05: Clearing OID overrides..."
reset_oid_overrides

log_info "PSS-05: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "PSS-05: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "PSS-05: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
