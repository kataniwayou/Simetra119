# Scenario 29: STS-01 Healthy baseline -- tier=3 no-action, zero command counters
# Validates the Tier 3 (healthy no-action) branch of the 4-tier evaluation tree.
# With all Evaluate metrics in-range, SnapshotJob must log tier=3 and dispatch zero commands.

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
# Set healthy scenario: all Evaluate metrics in-range
# ---------------------------------------------------------------------------

sim_set_scenario healthy

# ---------------------------------------------------------------------------
# Baseline: snapshot counters BEFORE the assertion window
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
BEFORE_SUPP=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-tenant-A"')
log_info "Baseline sent=${BEFORE_SENT} suppressed=${BEFORE_SUPP}"

# ---------------------------------------------------------------------------
# Sub-scenario 29a: tier=3 log assertion
# Wait for SnapshotJob to log the tier=3 healthy branch (up to 90s).
# ---------------------------------------------------------------------------

SCENARIO_NAME="STS-01: Healthy tier=3 log detected"

log_info "Polling for tier=3 log (timeout 90s, since 60s)..."
if poll_until_log 90 5 "tier=3 — not all evaluate metrics violated" 60; then
    record_pass "$SCENARIO_NAME" "log=tier3_found"
else
    record_fail "$SCENARIO_NAME" "log=tier3_not_found — poll_until_log timed out after 90s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 29b: command counter delta assertion
# Both sent and suppressed deltas must be zero for a truly healthy cycle.
# ---------------------------------------------------------------------------

SCENARIO_NAME="STS-01: Zero command counters on healthy cycle"

AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
AFTER_SUPP=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-tenant-A"')

DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
DELTA_SUPP=$((AFTER_SUPP - BEFORE_SUPP))

log_info "After: sent=${AFTER_SENT} suppressed=${AFTER_SUPP} delta_sent=${DELTA_SENT} delta_supp=${DELTA_SUPP}"

if [ "$DELTA_SENT" -eq 0 ] && [ "$DELTA_SUPP" -eq 0 ]; then
    record_pass "$SCENARIO_NAME" "log=tier3_found sent_delta=${DELTA_SENT} suppressed_delta=${DELTA_SUPP}"
else
    record_fail "$SCENARIO_NAME" "log=tier3_found sent_delta=${DELTA_SENT} suppressed_delta=${DELTA_SUPP} — expected both 0"
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
