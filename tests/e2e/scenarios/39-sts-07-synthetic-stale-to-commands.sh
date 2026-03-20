# Scenario 39: STS-07 Synthetic staleness to commands
# Uses tenant-cfg04-aggregate.yaml (e2e-tenant-agg, Evaluate=e2e_total_util synthetic aggregate)
#
# Validates that a synthetic-sourced metric (aggregated from poll group OIDs) triggers
# staleness → command dispatch when underlying polls stop arriving.
#
# Sequence:
#   1. Apply aggregate fixture (e2e-tenant-agg, e2e_total_util as Evaluate, Max:80)
#   2. Prime with agg_breach scenario to populate synthetic aggregate values
#   3. Switch to stale scenario (source OIDs return NoSuchInstance → aggregate can't compute)
#   4. Synthetic holder timestamp ages out → tier=1 stale detected
#   5. Stale path skips to tier=4, commands enqueued
#
# Timing: prime 20s + grace window 20s + SnapshotJob cycle 15s = ~55s minimum
#
# Sub-assertions:
#   39a: tier=1 stale log with "skipping to commands" text
#   39b: tier=4 commands enqueued log for e2e-tenant-agg
#   39c: snmp_command_dispatched_total counter increments

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: Save current tenants ConfigMap, apply aggregate fixture
# ---------------------------------------------------------------------------

log_info "STS-07: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "STS-07: Applying tenant-cfg04-aggregate.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg04-aggregate.yaml" > /dev/null 2>&1 || true

log_info "STS-07: Waiting for tenant vector reload (timeout 60s)..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "STS-07: Tenant vector reload confirmed"
else
    log_warn "STS-07: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Priming: set agg_breach scenario to populate synthetic aggregate values
# Without recent agg_breach data, the synthetic holder has no timestamps to age out
# and HasStaleness returns false — the stale scenario won't trigger tier=1
# ---------------------------------------------------------------------------

log_info "STS-07: Priming with agg_breach scenario to populate synthetic aggregate values..."
sim_set_scenario agg_breach

log_info "STS-07: Waiting 20s for poll cycles + aggregate computation to populate timestamps..."
sleep 20

# ---------------------------------------------------------------------------
# Baseline + stale switch
# Capture sent counter BEFORE switching to stale so delta proves new dispatches
# ---------------------------------------------------------------------------

log_info "STS-07: Baselining command counters BEFORE switching to stale..."
BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-agg"')
log_info "STS-07: Baseline sent=${BEFORE_SENT}"

log_info "STS-07: Switching to stale scenario (source OIDs return NoSuchInstance)..."
sim_set_scenario stale

# ---------------------------------------------------------------------------
# Sub-scenario 39a: tier=1 stale log with "skipping to commands" text
# Grace window (GraceMultiplier=2.0 * IntervalSeconds from poll group) must elapse
# before SnapshotJob logs tier=1 stale — skipping to commands for e2e-tenant-agg
# ---------------------------------------------------------------------------

SCENARIO_NAME="STS-07: Synthetic staleness tier=1 stale log"

log_info "STS-07: Polling for tier=1 stale + skipping to commands log (90s timeout)..."
if poll_until_log 90 5 "e2e-tenant-agg.*tier=1 stale" 60; then
    record_pass "$SCENARIO_NAME" "log=tier1_synthetic_stale_found"
else
    record_fail "$SCENARIO_NAME" "tier=1 stale log for e2e-tenant-agg not found within 90s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 39b: tier=4 commands enqueued log
# After tier=1 stale is detected for synthetic metric, SnapshotJob skips to
# tier=4 and enqueues commands — scope to e2e-tenant-agg to avoid false positives
# ---------------------------------------------------------------------------

SCENARIO_NAME="STS-07: Synthetic staleness triggers tier=4 commands"

log_info "STS-07: Polling for tier=4 commands enqueued log (90s timeout)..."
if poll_until_log 90 5 "e2e-tenant-agg.*tier=4 — commands enqueued" 60; then
    record_pass "$SCENARIO_NAME" "log=tier4_synthetic_stale_commands_found"
else
    record_fail "$SCENARIO_NAME" "tier=4 commands enqueued log for e2e-tenant-agg not found within 90s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 39c: snmp_command_dispatched_total counter increments
# SNMP SET round-trip + OTel export + Prometheus scrape takes time — use poll_until
# ---------------------------------------------------------------------------

SCENARIO_NAME="STS-07: Command sent counter after synthetic staleness"

log_info "STS-07: Polling for command sent counter increment (45s timeout)..."
if poll_until 45 5 "snmp_command_dispatched_total" 'device_name="e2e-tenant-agg"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-agg"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "STS-07: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_pass "$SCENARIO_NAME" "sent_delta=${DELTA_SENT}"
else
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-agg"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "STS-07: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_fail "$SCENARIO_NAME" "sent_delta=${DELTA_SENT} expected > 0 after 45s polling"
fi

# ---------------------------------------------------------------------------
# Cleanup: reset simulator, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "STS-07: Resetting simulator scenario..."
reset_scenario

log_info "STS-07: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "STS-07: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "STS-07: Original tenant ConfigMap snapshot not found; skipping restore"
fi
