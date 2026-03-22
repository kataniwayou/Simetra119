# Scenario 96: MLC-03 source=command label on snmp_gauge for SET response
# Uses tenant-cfg06-pss-single.yaml (e2e-pss-tenant: IntervalSeconds=1, SuppressionWindowSeconds=10)
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# Command response OID: .999.4.4.0 => resolved_name="e2e_command_response", snmp_type="integer32"
#
# Tier=4 fires when:
#   - Resolved holders are NOT all violated (tier=2 gate passes)
#   - Evaluate holder IS violated (< Min:10)
#
# After dispatch, CommandWorkerService writes SET response varbind with source=Command.
# OtelMetricHandler records it as snmp_gauge{source="command"}.
#
# Sub-assertions:
#   96: MLC-03: source=command label on snmp_gauge (e2e_command_response)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply single-tenant PSS fixture
# ---------------------------------------------------------------------------

log_info "MLC-03: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "MLC-03: Applying tenant-cfg06-pss-single.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-single.yaml" > /dev/null 2>&1 || true

log_info "MLC-03: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "MLC-03: Tenant vector reload confirmed"
else
    log_warn "MLC-03: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime T2 OIDs with in-range values to pass readiness grace
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# ---------------------------------------------------------------------------

log_info "MLC-03: Priming T2 OIDs with in-range values for readiness grace..."
sim_set_oid "5.1" "10"   # T2 eval in-range (>= Min:10)
sim_set_oid "5.2" "1"    # T2 res1 in-range (>= Min:1)
sim_set_oid "5.3" "1"    # T2 res2 in-range (>= Min:1)

log_info "MLC-03: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Capture dispatched counter baseline BEFORE setting evaluate to violated
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"')
log_info "MLC-03: Baseline dispatched=${BEFORE_SENT}"

# ---------------------------------------------------------------------------
# Set T2 evaluate OID to violated (< Min:10 = value 0)
# T2 res1 and res2 stay at 1 from priming (in-range, NOT all violated)
# => tier=2 gate passes, tier=4 fires (commands dispatched, response varbinds recorded)
# ---------------------------------------------------------------------------

log_info "MLC-03: Setting T2 evaluate to violated (eval=0, res unchanged at 1)..."
sim_set_oid "5.1" "0"    # T2 eval violated (< Min:10)

# ---------------------------------------------------------------------------
# Wait for dispatch confirmation: poll dispatched counter until it increments
# ---------------------------------------------------------------------------

log_info "MLC-03: Polling for dispatched counter increment (30s timeout)..."
if poll_until 30 2 "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"' "$BEFORE_SENT"; then
    log_info "MLC-03: Dispatch confirmed"
else
    log_warn "MLC-03: Dispatch counter not incremented within 30s; proceeding to label check"
fi

# ---------------------------------------------------------------------------
# MLC-03: Assert source="command" on snmp_gauge for e2e_command_response
# Poll for snmp_gauge with device_name="E2E-SIM", resolved_name="e2e_command_response",
# source="command" up to 30s with 3s interval.
# device_name on snmp_gauge is "E2E-SIM" (from community string), NOT the tenant name.
# ---------------------------------------------------------------------------

SCENARIO_NAME="MLC-03: source=command label on snmp_gauge (e2e_command_response)"

log_info "MLC-03: Polling for snmp_gauge{source=\"command\", resolved_name=\"e2e_command_response\"} (30s timeout)..."

DEADLINE=$(( $(date +%s) + 30 ))
MLC03_FOUND=false

while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    MLC03_RESULT=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_command_response",source="command"}') || true
    MLC03_COUNT=$(echo "$MLC03_RESULT" | jq -r '.data.result | length' 2>/dev/null || echo "0")
    if [ "$MLC03_COUNT" -gt 0 ]; then
        MLC03_FOUND=true
        break
    fi
    sleep 3
done

if [ "$MLC03_FOUND" = "true" ]; then
    MLC03_SOURCE=$(echo "$MLC03_RESULT" | jq -r '.data.result[0].metric.source // "missing"')
    log_info "MLC-03: Found series with source=${MLC03_SOURCE}"
    if [ "$MLC03_SOURCE" = "command" ]; then
        record_pass "$SCENARIO_NAME" \
            "snmp_gauge{device_name=\"E2E-SIM\",resolved_name=\"e2e_command_response\",source=\"command\"} found; source=${MLC03_SOURCE}"
    else
        record_fail "$SCENARIO_NAME" \
            "series found but source=${MLC03_SOURCE} expected=command"
    fi
else
    log_info "MLC-03: Series not found after 30s; querying for evidence..."
    MLC03_EVIDENCE=$(query_prometheus 'snmp_gauge{resolved_name="e2e_command_response"}') || true
    MLC03_EVIDENCE_COUNT=$(echo "$MLC03_EVIDENCE" | jq -r '.data.result | length' 2>/dev/null || echo "0")
    record_fail "$SCENARIO_NAME" \
        "snmp_gauge{source=\"command\",resolved_name=\"e2e_command_response\"} not found after 30s; all e2e_command_response series count=${MLC03_EVIDENCE_COUNT}"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "MLC-03: Clearing OID overrides..."
reset_oid_overrides

log_info "MLC-03: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "MLC-03: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "MLC-03: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
