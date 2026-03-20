# Scenario 30: STS-02 Evaluate violated -- tier=4 command dispatch, sent counter increments
# Validates the Tier 4 (command dispatch) branch of the 4-tier evaluation tree.
# command_trigger scenario sets Evaluate metric above threshold while Resolved metrics stay in-range,
# so SnapshotJob must log tier=4 and dispatch at least one command (sent counter > 0).

# ---------------------------------------------------------------------------
# Setup: Save current tenants ConfigMap, apply single-tenant fixture
# ---------------------------------------------------------------------------

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "Applying tenant-cfg01-single.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg01-single.yaml" || true

log_info "Waiting for tenant vector reload (timeout 60s)..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "Tenant vector reload confirmed"
else
    log_warn "Tenant vector reload log not found within 60s — proceeding anyway"
fi

# ---------------------------------------------------------------------------
# Set command_trigger scenario: Evaluate metric violated, Resolved metrics in-range
# e2e_port_utilization=90 (> Max:80 → violated), e2e_channel_state=2, e2e_bypass_status=2 (>= Min:1 → in-range)
# TimeSeriesSize=3 requires 3 poll cycles (~30s) to fill the series before tier=4 fires.
# ---------------------------------------------------------------------------

sim_set_scenario command_trigger

# ---------------------------------------------------------------------------
# Baseline: snapshot command_sent counter BEFORE the assertion window
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A"')
log_info "Baseline sent=${BEFORE_SENT}"

# ---------------------------------------------------------------------------
# Sub-scenario 30a: tier=4 log assertion
# TimeSeriesSize=3 requires ~30s to fill; 90s timeout accommodates 3 poll cycles.
# ---------------------------------------------------------------------------

SCENARIO_NAME="STS-02: Evaluate violated tier=4 log detected"

log_info "Polling for tier=4 log (timeout 90s, since 60s)..."
if poll_until_log 90 5 "tier=4 — commands enqueued" 60; then
    record_pass "$SCENARIO_NAME" "log=tier4_found"
else
    record_fail "$SCENARIO_NAME" "log=tier4_not_found — poll_until_log timed out after 90s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 30b: command_sent delta > 0
# At least one SNMP SET command must have been dispatched.
# ---------------------------------------------------------------------------

SCENARIO_NAME="STS-02: Command sent counter incremented"

# Poll for counter — SNMP SET round-trip + OTel export + Prometheus scrape takes time.
if poll_until 45 5 "snmp_command_dispatched_total" 'device_name="e2e-tenant-A"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_pass "$SCENARIO_NAME" "sent_delta=${DELTA_SENT}"
else
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_fail "$SCENARIO_NAME" "sent_delta=${DELTA_SENT} after 45s polling"
fi

# ---------------------------------------------------------------------------
# Cleanup: reset simulator, restore original ConfigMap
# ---------------------------------------------------------------------------

reset_scenario

log_info "Restoring original tenantvector ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "Failed to restore tenantvector ConfigMap from snapshot"
else
    log_warn "Original tenantvector snapshot not found — skipping restore"
fi
