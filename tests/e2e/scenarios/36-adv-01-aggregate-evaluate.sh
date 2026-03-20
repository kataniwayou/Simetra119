# Scenario 36: ADV-01 Aggregate evaluate -- synthetic pipeline feeds threshold check
# Uses tenant-cfg04-aggregate.yaml (e2e-tenant-agg, Priority 1, Evaluate=e2e_total_util, TimeSeriesSize=3, Max:80)
#
# agg_breach scenario: .4.5=50 + .4.6=50 = sum 100 > Max:80 -> violated
# Resolved metrics (.4.2=2, .4.3=2) in-range -> tier-2 passes -> tier-4 fires
#
# Timing: TimeSeriesSize=3, poll 10s -> ~30s fill + SnapshotJob 15s = ~75s min

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply aggregate fixture
# ---------------------------------------------------------------------------

log_info "ADV-01: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "ADV-01: Applying tenant-cfg04-aggregate.yaml (e2e-tenant-agg, Evaluate=e2e_total_util, Max:80)..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg04-aggregate.yaml" > /dev/null 2>&1 || true

log_info "ADV-01: Waiting for tenant vector reload (timeout 60s)..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "ADV-01: Tenant vector reload confirmed"
else
    log_warn "ADV-01: Tenant vector reload log not found within 60s — proceeding anyway"
fi

# ---------------------------------------------------------------------------
# Breach phase: set agg_breach scenario and capture initial baseline
# ---------------------------------------------------------------------------

log_info "ADV-01: Setting simulator to agg_breach scenario (.4.5=50 + .4.6=50 = sum 100 > Max:80)..."
sim_set_scenario agg_breach

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-agg"')
log_info "ADV-01: Initial sent baseline=${BEFORE_SENT}"

# ---------------------------------------------------------------------------
# Sub-scenario 36a: tier=4 log fires for e2e-tenant-agg
# TimeSeriesSize=3 requires ~30s fill + SnapshotJob 15s = ~75s min; 90s timeout accommodates safely.
# ---------------------------------------------------------------------------

log_info "ADV-01: Polling for e2e-tenant-agg tier=4 log (timeout 90s, since 60s)..."
if poll_until_log 90 5 "e2e-tenant-agg.*tier=4 — commands enqueued" 60; then
    record_pass "ADV-01: Aggregate tier=4 detected" "log=tier4_agg_found"
else
    record_fail "ADV-01: Aggregate tier=4 detected" "tier4 log for e2e-tenant-agg not found within 90s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 36b: sent counter incremented (command dispatch confirmed)
# Poll for counter — SNMP SET round-trip + OTel export + Prometheus scrape takes time.
# ---------------------------------------------------------------------------

if poll_until 45 5 "snmp_command_dispatched_total" 'device_name="e2e-tenant-agg"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-agg"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "ADV-01: Sent counter after tier=4: before=${BEFORE_SENT} after=${AFTER_SENT} delta=${DELTA_SENT}"
    record_pass "ADV-01: Command sent counter incremented" \
        "sent_delta=${DELTA_SENT} $(get_evidence "snmp_command_dispatched_total" 'device_name="e2e-tenant-agg"')"
else
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-agg"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "ADV-01: Sent counter after tier=4: before=${BEFORE_SENT} after=${AFTER_SENT} delta=${DELTA_SENT}"
    record_fail "ADV-01: Command sent counter incremented" \
        "sent_delta=${DELTA_SENT} expected > 0 after 45s polling $(get_evidence "snmp_command_dispatched_total" 'device_name="e2e-tenant-agg"')"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 36c: source=synthetic label visible in Prometheus for e2e_total_util
# Wait 30s after tier=4 confirmed to allow OTel export + Prometheus scrape cycle.
# ---------------------------------------------------------------------------

log_info "ADV-01: Sleeping 30s to allow OTel export + Prometheus scrape for source=synthetic label..."
sleep 30

SYNTHETIC_RESULT=$(query_prometheus 'snmp_gauge{resolved_name="e2e_total_util",source="synthetic"}')
SYNTHETIC_COUNT=$(echo "$SYNTHETIC_RESULT" | jq -r '.data.result | length')
log_info "ADV-01: source=synthetic result count=${SYNTHETIC_COUNT}"

if [ "$SYNTHETIC_COUNT" -gt 0 ]; then
    record_pass "ADV-01: source=synthetic in Prometheus" \
        "count=${SYNTHETIC_COUNT} metric=e2e_total_util source=synthetic"
else
    record_fail "ADV-01: source=synthetic in Prometheus" \
        "count=0 — metric e2e_total_util with source=synthetic not found in Prometheus"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 36d: Recovery -- switch to healthy, expect tier=3 log
# healthy scenario: .4.5=0, .4.6=0 -> sum=0 < Max:80 -> in-range -> tier=3 (healthy evaluation)
# ---------------------------------------------------------------------------

log_info "ADV-01: Switching to healthy scenario for recovery test..."
sim_set_scenario healthy

log_info "ADV-01: Polling for e2e-tenant-agg tier=3 recovery log (timeout 90s, since 30s)..."
if poll_until_log 90 5 "e2e-tenant-agg.*tier=3" 30; then
    record_pass "ADV-01: Recovery tier=3 after healthy switch" "log=tier3_healthy_found"
else
    record_fail "ADV-01: Recovery tier=3 after healthy switch" "tier3 log not found within 90s"
fi

# ---------------------------------------------------------------------------
# Cleanup: reset simulator, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "ADV-01: Resetting simulator scenario..."
reset_scenario

log_info "ADV-01: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "ADV-01: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "ADV-01: Original tenant ConfigMap snapshot not found — skipping restore"
fi
