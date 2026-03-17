# Scenario 33: STS-05 Staleness detection -- tier=1 stale, no command counters
# Validates that after the simulator returns NoSuchInstance (stale scenario), the grace window
# expires and SnapshotJob logs tier=1 stale, with zero command activity throughout.
#
# Grace window: IntervalSeconds(10) * GraceMultiplier(2.0) = 20s
# After 20s of stale data + one SnapshotJob cycle (15s), tier=1 log appears.
#
# Priming step: switch to healthy scenario first and wait 20s to populate fresh poll
# timestamps. Without this, slots may be null (never polled) and HasStaleness returns false
# for null slots -- the stale scenario would not trigger tier=1.

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

log_info "STS-05: Waiting 20s for at least one poll cycle to populate fresh timestamps..."
sleep 20

# ---------------------------------------------------------------------------
# Switch to stale: simulator now returns NoSuchInstance for .4.1 and .4.2
# Poll failures cause existing timestamps to age out past the grace window
# ---------------------------------------------------------------------------

log_info "STS-05: Baselining command counters BEFORE switching to stale..."
BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
BEFORE_SUPP=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-tenant-A"')
log_info "STS-05 baseline sent=${BEFORE_SENT} suppressed=${BEFORE_SUPP}"

log_info "STS-05: Switching to stale scenario..."
sim_set_scenario stale

# ---------------------------------------------------------------------------
# Wait for staleness: grace window (20s) must elapse before tier=1 log appears
# Use generous timeout (90s) to accommodate timing variance
# ---------------------------------------------------------------------------

log_info "STS-05: Waiting for tier=1 stale log (90s timeout, 5s interval)..."
STALE_LOG_FOUND=0
if poll_until_log 90 5 "tier=1 stale" 60; then
    STALE_LOG_FOUND=1
fi

# ---------------------------------------------------------------------------
# Assertions
# ---------------------------------------------------------------------------

# Sub-scenario 33a: tier=1 stale log detected
SCENARIO_NAME="STS-05: Staleness tier=1 stale log detected"
if [ "$STALE_LOG_FOUND" -eq 1 ]; then
    record_pass "$SCENARIO_NAME" "log=tier1_stale_found"
else
    record_fail "$SCENARIO_NAME" "tier=1 stale log not found within 90s after stale scenario switch"
fi

AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
AFTER_SUPP=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-tenant-A"')
DELTA_SENT=$(( AFTER_SENT - BEFORE_SENT ))
DELTA_SUPP=$(( AFTER_SUPP - BEFORE_SUPP ))
log_info "STS-05 deltas: sent=${DELTA_SENT} suppressed=${DELTA_SUPP}"

# Sub-scenario 33b: no command_sent counter increments while stale
SCENARIO_NAME="STS-05: No commands sent while data is stale"
if [ "$DELTA_SENT" -eq 0 ]; then
    record_pass "$SCENARIO_NAME" \
        "sent_delta=${DELTA_SENT} expected=0 $(get_evidence "snmp_command_sent_total" 'device_name="E2E-SIM"')"
else
    record_fail "$SCENARIO_NAME" \
        "unexpected sent_delta=${DELTA_SENT} expected=0; commands fired during staleness"
fi

# Sub-scenario 33c: no command_suppressed counter increments while stale
SCENARIO_NAME="STS-05: No suppressions while data is stale"
if [ "$DELTA_SUPP" -eq 0 ]; then
    record_pass "$SCENARIO_NAME" \
        "suppressed_delta=${DELTA_SUPP} expected=0 $(get_evidence "snmp_command_suppressed_total" 'device_name="e2e-tenant-A"')"
else
    record_fail "$SCENARIO_NAME" \
        "unexpected suppressed_delta=${DELTA_SUPP} expected=0; suppression events fired during staleness"
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
