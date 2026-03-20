# Scenario 33: STS-05 Staleness detection -- tier=1 stale triggers command dispatch
# Validates that after the simulator returns NoSuchInstance (stale scenario), the grace window
# expires, SnapshotJob logs tier=1 stale, and commands ARE dispatched (tier=4 skip-to-commands).
#
# Quick-076 changed staleness behavior: stale data now skips to tier=4 command dispatch
# instead of returning early with no commands.
#
# Grace window: IntervalSeconds(10) * GraceMultiplier(2.0) = 20s
# After 20s of stale data + one SnapshotJob cycle (15s), tier=1 log appears.
#
# Priming step: switch to healthy scenario first and wait 20s to populate fresh poll
# timestamps. This satisfies the readiness grace window for all holders and populates
# recent poll timestamps so the stale scenario can trigger tier=1.

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: apply standard single-tenant fixture and wait for reload
# ---------------------------------------------------------------------------

log_info "STS-05: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "STS-05: Applying tenant-cfg01-single.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg01-single.yaml" > /dev/null 2>&1 || true

log_info "STS-05: Waiting for tenant vector reload..."
poll_until_log 60 5 "Tenant vector reload complete" 30 || \
    log_warn "STS-05: Tenant vector reload not detected within 60s; proceeding"

# ---------------------------------------------------------------------------
# Priming: set healthy scenario and wait for fresh poll data to populate
# Without recent poll timestamps, HasStaleness returns false for null slots
# and the stale scenario will not trigger tier=1
# ---------------------------------------------------------------------------

log_info "STS-05: Priming with healthy scenario to populate fresh poll timestamps..."
sim_set_scenario healthy

log_info "STS-05: Waiting 20s to satisfy readiness grace window and populate fresh poll timestamps..."
sleep 20

# ---------------------------------------------------------------------------
# Baseline sent counter BEFORE switching to stale
# ---------------------------------------------------------------------------

log_info "STS-05: Baselining command counters BEFORE switching to stale..."
BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A"')
log_info "STS-05 baseline sent=${BEFORE_SENT}"

# ---------------------------------------------------------------------------
# Switch to stale: simulator now returns NoSuchInstance for .4.1 and .4.2
# Poll failures cause existing timestamps to age out past the grace window
# ---------------------------------------------------------------------------

log_info "STS-05: Switching to stale scenario..."
sim_set_scenario stale

# ---------------------------------------------------------------------------
# Sub-scenario 33a: tier=1 stale log detected
# Use generous timeout (90s) to accommodate timing variance
# ---------------------------------------------------------------------------

SCENARIO_NAME="STS-05: Staleness tier=1 stale log detected"
log_info "STS-05: Waiting for tier=1 stale log (90s timeout, 5s interval)..."
if poll_until_log 90 5 "tier=1 stale" 60; then
    record_pass "$SCENARIO_NAME" "log=tier1_stale_found"
else
    record_fail "$SCENARIO_NAME" "tier=1 stale log not found within 90s after stale scenario switch"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 33b: commands ARE sent when stale (tier=4 skip-to-commands)
# Poll for counter increment -- same pattern as STS-02 sub-scenario 30b.
# ---------------------------------------------------------------------------

SCENARIO_NAME="STS-05: Commands dispatched when data is stale"
log_info "STS-05: Polling for command sent counter to increment (45s timeout)..."
if poll_until 45 5 "snmp_command_dispatched_total" 'device_name="e2e-tenant-A"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "STS-05: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_pass "$SCENARIO_NAME" "sent_delta=${DELTA_SENT}"
else
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "STS-05: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_fail "$SCENARIO_NAME" "sent_delta=${DELTA_SENT} expected > 0 after 45s polling"
fi

# ---------------------------------------------------------------------------
# Cleanup
# ---------------------------------------------------------------------------

log_info "STS-05: Resetting simulator scenario (polls start succeeding again)..."
reset_scenario

log_info "STS-05: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "STS-05: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "STS-05: Original tenant ConfigMap snapshot not found; skipping restore"
fi
