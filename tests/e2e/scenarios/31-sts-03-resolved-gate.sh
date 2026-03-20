# Scenario 31: STS-03 Resolved gate -- tier=2 Violated, zero command counters
# Validates the Tier 2 (Violated gate) branch of the 4-tier evaluation tree.
# Default scenario has all Resolved metrics at 0 (< Min:1 → violated), so SnapshotJob
# must log tier=2 Violated, dispatch zero commands, and never reach tier=4.

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
# Set default scenario explicitly: all OIDs return 0
# e2e_channel_state=0 and e2e_bypass_status=0 (both < Min:1 → Resolved violated → Violated)
# ---------------------------------------------------------------------------

sim_set_scenario default

# ---------------------------------------------------------------------------
# Baseline: snapshot counters BEFORE the assertion window
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
BEFORE_SUPP=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-tenant-A"')
log_info "Baseline sent=${BEFORE_SENT} suppressed=${BEFORE_SUPP}"

# ---------------------------------------------------------------------------
# Sub-scenario 31a: tier=2 Violated log assertion
# ---------------------------------------------------------------------------

SCENARIO_NAME="STS-03: Resolved gate tier=2 Violated log detected"

log_info "Polling for tier=2 Violated log (timeout 90s, since 60s)..."
if poll_until_log 90 5 "tier=2 — all resolved violated, no commands" 60; then
    record_pass "$SCENARIO_NAME" "log=tier2_violated_found"
else
    record_fail "$SCENARIO_NAME" "log=tier2_violated_not_found — poll_until_log timed out after 90s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 31b: command counter delta assertion
# Both sent and suppressed deltas must be zero — Violated gate blocks all commands.
# ---------------------------------------------------------------------------

SCENARIO_NAME="STS-03: Zero command counters while Violated"

AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
AFTER_SUPP=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-tenant-A"')

DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
DELTA_SUPP=$((AFTER_SUPP - BEFORE_SUPP))

log_info "After: sent=${AFTER_SENT} suppressed=${AFTER_SUPP} delta_sent=${DELTA_SENT} delta_supp=${DELTA_SUPP}"

if [ "$DELTA_SENT" -eq 0 ] && [ "$DELTA_SUPP" -eq 0 ]; then
    record_pass "$SCENARIO_NAME" "log=tier2_found sent_delta=${DELTA_SENT} suppressed_delta=${DELTA_SUPP}"
else
    record_fail "$SCENARIO_NAME" "log=tier2_found sent_delta=${DELTA_SENT} suppressed_delta=${DELTA_SUPP} — expected both 0"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 31c: negative tier=4 assertion
# When Violated gate is active, tier=4 must NOT appear in recent logs.
# Short window (60s) grep — no polling needed, just check recent pod logs.
# ---------------------------------------------------------------------------

# Wait for any previous tier=4 logs to age out of the check window.
# Phase 60: readiness grace window (20s) delays evaluation after tenant reload,
# so previous tier=4 logs persist longer. 25s ensures both grace window and
# SnapshotJob cycle are accounted for.
sleep 25

SCENARIO_NAME="STS-03: No tier=4 log while resolved gate active"

PODS=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true

FOUND_TIER4=0
for POD in $PODS; do
    POD_LOGS=$(kubectl logs "$POD" -n simetra --since=15s 2>/dev/null) || true
    # Scope to e2e-tenant-A to avoid matching tier=4 logs from previous test scenarios
    if echo "$POD_LOGS" | grep "e2e-tenant-A.*tier=4" > /dev/null 2>&1; then
        FOUND_TIER4=1
        break
    fi
done

if [ "$FOUND_TIER4" -eq 0 ]; then
    record_pass "$SCENARIO_NAME" "log=tier4_absent confirmed"
else
    record_fail "$SCENARIO_NAME" "Unexpected tier=4 log found while resolved gate (tier=2) is active"
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
