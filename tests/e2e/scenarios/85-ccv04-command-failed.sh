# Scenario 85: CCV-04 Command Failed -- command.failed increments when CommandName is not in oid_command_map
# Uses tenant-cfg09-ccv-failed.yaml (e2e-ccv-failed, CommandName: e2e_set_unknown -- NOT in simetra-oid-command-map)
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
#
# Sequence:
#   1. SnapshotJob reaches tier=4 (evaluate violated, resolved in-range)
#   2. SnapshotJob enqueues command -> snmp.command.dispatched increments (device_name=tenant name)
#   3. CommandWorkerService dequeues, calls ResolveCommandOid("e2e_set_unknown") -> returns null
#   4. CommandWorkerService increments snmp.command.failed (device_name="e2e-simulator.simetra.svc.cluster.local:161")
#
# Sub-assertions:
#   85a: CCV-04A: command.dispatched increments for unmapped command (dispatch precedes failure)
#   85b: CCV-04B: command.failed increments for unmapped CommandName (OID not found)
#
# Note: snmp_command_failed_total uses device_name=IP:port (not tenant name) for the OID-not-found path.
# Empty label filter is used to avoid label brittleness with the IP:port string.

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply CCV-04 fixture
# ---------------------------------------------------------------------------

log_info "CCV-04: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "CCV-04: Applying tenant-cfg09-ccv-failed.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg09-ccv-failed.yaml" > /dev/null 2>&1 || true

log_info "CCV-04: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "CCV-04: Tenant vector reload confirmed"
else
    log_warn "CCV-04: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime T2 OIDs with in-range values to pass readiness grace
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# ---------------------------------------------------------------------------

log_info "CCV-04: Priming T2 OIDs with in-range values for readiness grace..."
sim_set_oid "5.1" "10"   # T2 eval in-range (>= Min:10)
sim_set_oid "5.2" "1"    # T2 res1 in-range (>= Min:1)
sim_set_oid "5.3" "1"    # T2 res2 in-range (>= Min:1)

log_info "CCV-04: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Capture both counter baselines BEFORE triggering tier=4
# dispatched: device_name=tenant name (SnapshotJob tag)
# failed:     empty filter (device_name=IP:port from CommandWorkerService, avoid label brittleness)
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-ccv-failed"')
BEFORE_FAILED=$(snapshot_counter "snmp_command_failed_total" '')
log_info "CCV-04: Baseline dispatched=${BEFORE_SENT} failed=${BEFORE_FAILED}"

# ---------------------------------------------------------------------------
# Set T2 evaluate OID to violated (< Min:10 = value 0)
# T2 res1 and res2 stay at 1 from priming (in-range, NOT all violated)
# => tier=2 gate passes, tier=4 fires (commands enqueued)
# ---------------------------------------------------------------------------

log_info "CCV-04: Setting T2 evaluate to violated (eval=0, resolved unchanged at 1)..."
sim_set_oid "5.1" "0"    # T2 eval violated (< Min:10)

# ---------------------------------------------------------------------------
# Poll for dispatched increment (SnapshotJob enqueues command -> tier=4 confirmed)
# dispatched fires synchronously in SnapshotJob when TryWrite succeeds
# ---------------------------------------------------------------------------

log_info "CCV-04: Polling for dispatched counter increment (30s timeout)..."
poll_until 30 2 "snmp_command_dispatched_total" 'device_name="e2e-ccv-failed"' "$BEFORE_SENT" || true

# ---------------------------------------------------------------------------
# Wait for CommandWorkerService to drain the queue and process the command
# failed fires asynchronously after CommandWorkerService dequeues and calls ResolveCommandOid
# ---------------------------------------------------------------------------

log_info "CCV-04: Waiting 10s for CommandWorkerService async drain..."
sleep 10

# ---------------------------------------------------------------------------
# Snapshot both AFTER values
# ---------------------------------------------------------------------------

AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-ccv-failed"')
AFTER_FAILED=$(snapshot_counter "snmp_command_failed_total" '')
DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
DELTA_FAILED=$((AFTER_FAILED - BEFORE_FAILED))
log_info "CCV-04: After: dispatched=${AFTER_SENT} delta_dispatched=${DELTA_SENT} failed=${AFTER_FAILED} delta_failed=${DELTA_FAILED}"

# ---------------------------------------------------------------------------
# Sub-assertion 85a: CCV-04A dispatched increments (dispatch precedes failure)
# ---------------------------------------------------------------------------

if [ "$DELTA_SENT" -ge 1 ]; then
    record_pass "CCV-04A: command.dispatched increments for unmapped command (dispatch precedes failure)" \
        "dispatched_delta=${DELTA_SENT} dispatched_before=${BEFORE_SENT} dispatched_after=${AFTER_SENT}"
else
    record_fail "CCV-04A: command.dispatched increments for unmapped command (dispatch precedes failure)" \
        "dispatched_delta=${DELTA_SENT} expected >= 1. dispatched_before=${BEFORE_SENT} dispatched_after=${AFTER_SENT}"
fi

# ---------------------------------------------------------------------------
# Sub-assertion 85b: CCV-04B command.failed increments (OID not found in command map)
# CommandName e2e_set_unknown is not in simetra-oid-command-map
# CommandWorkerService line 107: ResolveCommandOid returns null -> IncrementCommandFailed
# ---------------------------------------------------------------------------

if [ "$DELTA_FAILED" -ge 1 ]; then
    record_pass "CCV-04B: command.failed increments for unmapped CommandName (OID not found)" \
        "failed_delta=${DELTA_FAILED} failed_before=${BEFORE_FAILED} failed_after=${AFTER_FAILED} (CommandName e2e_set_unknown not in oid_command_map)"
else
    record_fail "CCV-04B: command.failed increments for unmapped CommandName (OID not found)" \
        "failed_delta=${DELTA_FAILED} expected >= 1. failed_before=${BEFORE_FAILED} failed_after=${AFTER_FAILED} (CommandName e2e_set_unknown not in oid_command_map)"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "CCV-04: Clearing OID overrides..."
reset_oid_overrides

log_info "CCV-04: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "CCV-04: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "CCV-04: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
