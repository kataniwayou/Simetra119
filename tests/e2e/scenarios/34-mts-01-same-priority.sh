# Scenario 34: MTS-01 Same-priority independence -- both P1 tenants produce independent tier=4 log lines and command counters
# Validates that two tenants at Priority 1 (e2e-tenant-A and e2e-tenant-B) are evaluated
# independently in the same SnapshotJob cycle, each emitting their own tier=4 log line and
# each contributing to the snmp_command_sent_total counter.
#
# Fixture: tenant-cfg02-two-same-prio.yaml (both tenants Priority 1, SuppressionWindowSeconds=10)
# Scenario: command_trigger (.4.1=90 > Max:80 → both tenants reach tier=4)
# Suppression keys are per-tenant (prefixed with tenant ID) so both send on first cycle.

# ---------------------------------------------------------------------------
# Setup: Save current tenants ConfigMap, apply two-same-priority fixture
# ---------------------------------------------------------------------------

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

log_info "MTS-01: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "MTS-01: Applying tenant-cfg02-two-same-prio.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg02-two-same-prio.yaml" > /dev/null 2>&1 || true

log_info "MTS-01: Waiting for tenant vector reload (timeout 60s)..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "MTS-01: Tenant vector reload confirmed"
else
    log_warn "MTS-01: Tenant vector reload log not found within 60s — proceeding anyway"
fi

# ---------------------------------------------------------------------------
# Set command_trigger scenario: e2e_port_utilization=90 (> Max:80 → violated)
# e2e_channel_state=2 and e2e_bypass_status=2 (>= Min:1.0 → in-range)
# Both tenants watch the same OIDs with identical thresholds → both reach tier=4
# TimeSeriesSize=3 requires ~30s to fill; 90s timeout accommodates 3 poll cycles.
# ---------------------------------------------------------------------------

sim_set_scenario command_trigger

# ---------------------------------------------------------------------------
# Baseline: snapshot command_sent counter BEFORE the assertion window
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
log_info "MTS-01: Baseline sent=${BEFORE_SENT}"

# ---------------------------------------------------------------------------
# Sub-scenario 34a: e2e-tenant-A tier=4 log assertion
# First tenant to confirm — use full 90s timeout to allow series to fill.
# ---------------------------------------------------------------------------

log_info "MTS-01: Polling for e2e-tenant-A tier=4 log (timeout 90s, since 60s)..."
if poll_until_log 90 5 "e2e-tenant-A.*tier=4 — commands enqueued" 60; then
    record_pass "MTS-01: e2e-tenant-A tier=4 log detected" "log=tier4_tenantA_found"
else
    record_fail "MTS-01: e2e-tenant-A tier=4 log detected" "tier4 log for e2e-tenant-A not found within 90s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 34b: e2e-tenant-B tier=4 log assertion
# Shorter timeout — scenario already active and evaluation is ongoing.
# ---------------------------------------------------------------------------

log_info "MTS-01: Polling for e2e-tenant-B tier=4 log (timeout 60s, since 30s)..."
if poll_until_log 60 5 "e2e-tenant-B.*tier=4 — commands enqueued" 30; then
    record_pass "MTS-01: e2e-tenant-B tier=4 log detected" "log=tier4_tenantB_found"
else
    record_fail "MTS-01: e2e-tenant-B tier=4 log detected" "tier4 log for e2e-tenant-B not found within 60s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 34c: command sent counter delta >= 2
# Both tenants command the same device (e2e-simulator) with distinct suppression keys.
# Each sends exactly one command on the first unsuppressed cycle → delta must be >= 2.
# assert_delta_gt "$DELTA" 1 means delta > 1, i.e., delta >= 2.
# ---------------------------------------------------------------------------

# Poll for counter — need delta >= 2, so poll until at least baseline+2 (i.e. baseline+1 exceeded).
# poll_until checks > baseline, so use baseline+1 to ensure delta >= 2.
NEED_AT_LEAST=$((BEFORE_SENT + 1))
if poll_until 45 5 "snmp_command_sent_total" 'device_name="E2E-SIM"' "$NEED_AT_LEAST"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA=$((AFTER_SENT - BEFORE_SENT))
    log_info "MTS-01: After: sent=${AFTER_SENT} delta=${DELTA}"
    assert_delta_gt "$DELTA" 1 "MTS-01: Both tenants commanded — sent delta >= 2" \
        "sent_delta=${DELTA}"
else
    AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA=$((AFTER_SENT - BEFORE_SENT))
    log_info "MTS-01: After: sent=${AFTER_SENT} delta=${DELTA}"
    record_fail "MTS-01: Both tenants commanded — sent delta >= 2" \
        "sent_delta=${DELTA} after 45s polling — expected >= 2"
fi

# ---------------------------------------------------------------------------
# Cleanup: reset simulator, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "MTS-01: Resetting simulator scenario..."
reset_scenario

log_info "MTS-01: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "MTS-01: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "MTS-01: Original tenant ConfigMap snapshot not found — skipping restore"
fi
