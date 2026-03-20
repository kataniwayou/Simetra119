# Scenario 38: STS-06 Staleness to commands -- tier=1 stale skips to tier=4 command dispatch
# Uses tenant-cfg01-single.yaml (e2e-tenant-A, Priority=1, SuppressionWindowSeconds=10)
#
# Quick-076 changed staleness behavior: stale data now skips directly to tier=4 command
# dispatch instead of returning early with no commands.
#
# Sequence:
#   1. Prime with healthy scenario to populate fresh poll timestamps
#   2. Switch to stale scenario (NoSuchInstance for .4.1 and .4.2)
#   3. Grace window (20s) expires, tier=1 stale detected
#   4. Stale path skips to tier=4, commands enqueued
#
# Timing: prime 20s + grace window 20s + SnapshotJob cycle 15s = ~55s minimum
#
# Sub-assertions:
#   38a: tier=1 stale log with "skipping to commands" text
#   38b: tier=4 commands enqueued log
#   38c: snmp_command_dispatched_total counter increments (command dispatch confirmed)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: Save current tenants ConfigMap, apply single-tenant fixture
# ---------------------------------------------------------------------------

log_info "STS-06: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "STS-06: Applying tenant-cfg01-single.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg01-single.yaml" > /dev/null 2>&1 || true

log_info "STS-06: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "STS-06: Tenant vector reload confirmed"
else
    log_warn "STS-06: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Priming: set healthy scenario and wait for fresh poll data to populate
# Without recent poll timestamps, HasStaleness returns false for null slots
# and the stale scenario will not trigger tier=1
# ---------------------------------------------------------------------------

log_info "STS-06: Priming with healthy scenario to populate fresh poll timestamps..."
sim_set_scenario healthy

log_info "STS-06: Waiting 20s for poll cycles to populate fresh timestamps..."
sleep 20

# ---------------------------------------------------------------------------
# Baseline + stale switch
# Capture sent counter BEFORE switching to stale so delta proves new dispatches
# ---------------------------------------------------------------------------

log_info "STS-06: Baselining command counters BEFORE switching to stale..."
BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A"')
log_info "STS-06: Baseline sent=${BEFORE_SENT}"

log_info "STS-06: Switching to stale scenario..."
sim_set_scenario stale

# ---------------------------------------------------------------------------
# Sub-scenario 38a: tier=1 stale log with "skipping to commands" text
# Grace window (GraceMultiplier=2.0 * IntervalSeconds=10 = 20s) must elapse
# before SnapshotJob logs tier=1 stale — skipping to commands
# ---------------------------------------------------------------------------

SCENARIO_NAME="STS-06: Staleness tier=1 stale log with skip-to-commands"

log_info "STS-06: Polling for tier=1 stale + skipping to commands log (90s timeout)..."
if poll_until_log 90 5 "tier=1 stale — skipping to commands" 60; then
    record_pass "$SCENARIO_NAME" "log=tier1_stale_skip_found"
else
    record_fail "$SCENARIO_NAME" "tier=1 stale skipping-to-commands log not found within 90s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 38b: tier=4 commands enqueued log
# After tier=1 stale is detected, SnapshotJob skips to tier=4 and enqueues commands
# Scope to e2e-tenant-A to avoid matching tier=4 from other tenants in prior scenarios
# ---------------------------------------------------------------------------

SCENARIO_NAME="STS-06: Staleness triggers tier=4 commands enqueued"

log_info "STS-06: Polling for tier=4 commands enqueued log (90s timeout)..."
if poll_until_log 90 5 "e2e-tenant-A.*tier=4 — commands enqueued" 60; then
    record_pass "$SCENARIO_NAME" "log=tier4_stale_commands_found"
else
    record_fail "$SCENARIO_NAME" "tier=4 commands enqueued log not found within 90s after staleness"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 38c: snmp_command_dispatched_total counter increments
# SNMP SET round-trip + OTel export + Prometheus scrape takes time — use poll_until
# ---------------------------------------------------------------------------

SCENARIO_NAME="STS-06: Command sent counter incremented after staleness"

log_info "STS-06: Polling for command sent counter increment (45s timeout)..."
if poll_until 45 5 "snmp_command_dispatched_total" 'device_name="e2e-tenant-A"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "STS-06: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_pass "$SCENARIO_NAME" "sent_delta=${DELTA_SENT}"
else
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "STS-06: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_fail "$SCENARIO_NAME" "sent_delta=${DELTA_SENT} expected > 0 after 45s polling"
fi

# ---------------------------------------------------------------------------
# Cleanup: reset simulator, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "STS-06: Resetting simulator scenario..."
reset_scenario

log_info "STS-06: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "STS-06: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "STS-06: Original tenant ConfigMap snapshot not found; skipping restore"
fi
