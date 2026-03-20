# Scenario 56: PSS-04 Unresolved (Commands) -- tier=4 commands enqueued when evaluate is violated
# Uses tenant-cfg06-pss-single.yaml (e2e-pss-tenant: IntervalSeconds=1, GraceMultiplier=2.0)
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
#
# Tier=4 fires when:
#   - Resolved holders are NOT all violated (tier=2 gate passes)
#   - Evaluate holder IS violated (< Min:10)
#
# To produce tier=4: set evaluate=0 (violated), leave resolved=1 (not violated).
# SnapshotJob dispatches commands and logs "tier=4 -- commands enqueued".
#
# Sub-assertions:
#   56a: tier=4 "commands enqueued" log with e2e-pss-tenant scope
#   56b: snmp_command_dispatched_total counter increment (command dispatch confirmed)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply single-tenant PSS fixture
# ---------------------------------------------------------------------------

log_info "PSS-04: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "PSS-04: Applying tenant-cfg06-pss-single.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-single.yaml" > /dev/null 2>&1 || true

log_info "PSS-04: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "PSS-04: Tenant vector reload confirmed"
else
    log_warn "PSS-04: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime T2 OIDs with in-range values to pass readiness grace
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# ---------------------------------------------------------------------------

log_info "PSS-04: Priming T2 OIDs with in-range values for readiness grace..."
sim_set_oid "5.1" "10"   # T2 eval in-range (>= Min:10)
sim_set_oid "5.2" "1"    # T2 res1 in-range (>= Min:1)
sim_set_oid "5.3" "1"    # T2 res2 in-range (>= Min:1)

log_info "PSS-04: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Capture sent counter baseline BEFORE setting evaluate to violated
# Delta after tier=4 fires must be > 0 (commands dispatched)
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"')
log_info "PSS-04: Baseline sent=${BEFORE_SENT}"

# ---------------------------------------------------------------------------
# Set T2 evaluate OID to violated (< Min:10 = value 0)
# T2 res1 and res2 stay at 1 from priming (in-range, NOT all violated)
# => tier=2 gate passes, tier=4 fires (commands enqueued)
# ---------------------------------------------------------------------------

log_info "PSS-04: Setting T2 evaluate to violated (eval=0, res unchanged at 1)..."
sim_set_oid "5.1" "0"    # T2 eval violated (< Min:10)

# ---------------------------------------------------------------------------
# Sub-scenario 56a: tier=4 commands enqueued log
# SnapshotJob: resolved in-range (tier=2 gate passes) + evaluate violated -> tier=4
# ---------------------------------------------------------------------------

log_info "PSS-04: Polling for tier=4 commands enqueued log (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-tenant.*tier=4 — commands enqueued\|e2e-pss-tenant.*tier=4 -- commands enqueued" 15; then
    record_pass "PSS-04A: e2e-pss-tenant tier=4 commands enqueued (unresolved)" "log=tier4_commands_found"
else
    record_fail "PSS-04A: e2e-pss-tenant tier=4 commands enqueued (unresolved)" "tier=4 commands enqueued log not found within 30s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 56b: snmp_command_dispatched_total counter increment
# Command dispatch confirmed via Prometheus counter
# ---------------------------------------------------------------------------

log_info "PSS-04: Polling for sent counter increment (30s timeout)..."
if poll_until 30 2 "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "PSS-04: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_pass "PSS-04B: Sent counter incremented after evaluate violated" "sent_delta=${DELTA_SENT}"
else
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "PSS-04: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_fail "PSS-04B: Sent counter incremented after evaluate violated" "sent_delta=${DELTA_SENT} expected > 0 after 30s polling"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "PSS-04: Clearing OID overrides..."
reset_oid_overrides

log_info "PSS-04: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "PSS-04: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "PSS-04: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
