# Scenario 44: SNS-04 Unresolved (Commands) -- tier=4 commands enqueued when evaluate is violated
# Uses tenant-cfg05-four-tenant-snapshot.yaml
# T1 OIDs: 4.1 = evaluate (Min:10), 4.2 = res1 (Min:1), 4.3 = res2 (Min:1)
#
# Tier=4 fires when:
#   - Resolved holders are NOT all violated (tier=2 gate passes)
#   - Evaluate holder IS violated (< Min:10)
#
# To produce tier=4: set evaluate=0 (violated), leave resolved=1 (not violated).
# SnapshotJob dispatches commands and logs "tier=4 -- commands enqueued".
#
# Sub-assertions:
#   44a: tier=4 "commands enqueued" log with G1-T1 scope
#   44b: snmp_command_sent_total counter increment (command dispatch confirmed)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply 4-tenant snapshot fixture
# ---------------------------------------------------------------------------

log_info "SNS-04: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "SNS-04: Applying tenant-cfg05-four-tenant-snapshot.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg05-four-tenant-snapshot.yaml" > /dev/null 2>&1 || true

log_info "SNS-04: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "SNS-04: Tenant vector reload confirmed"
else
    log_warn "SNS-04: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime all 4 tenants with in-range values to pass readiness grace
# T1: 4.1/4.2/4.3, T2: 5.1/5.2/5.3, T3: 6.1/6.2/6.3, T4: 7.1/7.2/7.3
# ---------------------------------------------------------------------------

log_info "SNS-04: Priming all 4 tenants for readiness grace..."
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

log_info "SNS-04: Waiting 8s for readiness grace..."
sleep 8

# ---------------------------------------------------------------------------
# Capture sent counter baseline BEFORE setting evaluate to violated
# Delta after tier=4 fires must be > 0 (commands dispatched)
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
log_info "SNS-04: Baseline sent=${BEFORE_SENT}"

# ---------------------------------------------------------------------------
# Set T1 evaluate OID to violated (< Min:10 = value 0)
# T1 res1 and res2 stay at 1 from priming (in-range, NOT all violated)
# => tier=2 gate passes, tier=4 fires (commands enqueued)
# ---------------------------------------------------------------------------

log_info "SNS-04: Setting T1 evaluate to violated (eval=0, res unchanged at 1)..."
sim_set_oid "4.1" "0"    # T1 eval violated (< Min:10)

# ---------------------------------------------------------------------------
# Sub-scenario 44a: tier=4 commands enqueued log
# SnapshotJob: resolved in-range (tier=2 gate passes) + evaluate violated -> tier=4
# ---------------------------------------------------------------------------

log_info "SNS-04: Polling for tier=4 commands enqueued log (30s timeout)..."
if poll_until_log 30 1 "e2e-tenant-G1-T1.*tier=4 — commands enqueued\|e2e-tenant-G1-T1.*tier=4 -- commands enqueued\|e2e-tenant-G1-T1.*tier=4" 15; then
    record_pass "SNS-04A: G1-T1 tier=4 commands enqueued (unresolved)" "log=tier4_commands_found"
else
    record_fail "SNS-04A: G1-T1 tier=4 commands enqueued (unresolved)" "tier=4 commands enqueued log not found within 30s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 44b: snmp_command_sent_total counter increment
# Command dispatch confirmed via Prometheus counter
# ---------------------------------------------------------------------------

log_info "SNS-04: Polling for sent counter increment (30s timeout)..."
if poll_until 30 2 "snmp_command_sent_total" 'device_name="E2E-SIM"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "SNS-04: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_pass "SNS-04B: Sent counter incremented after evaluate violated" "sent_delta=${DELTA_SENT}"
else
    AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "SNS-04: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_fail "SNS-04B: Sent counter incremented after evaluate violated" "sent_delta=${DELTA_SENT} expected > 0 after 30s polling"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "SNS-04: Clearing OID overrides..."
reset_oid_overrides

log_info "SNS-04: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "SNS-04: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "SNS-04: Original tenant ConfigMap snapshot not found — skipping restore"
fi
