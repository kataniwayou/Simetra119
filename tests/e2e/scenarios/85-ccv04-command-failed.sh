# Scenario 85: CCV-04 Command Failed -- command.failed increments on SET timeout
# Uses tenant-cfg10-ccv-timeout.yaml (e2e-ccv-timeout, CommandName: e2e_set_bypass)
# Command target IP: 10.255.255.254 (FAKE-UNREACHABLE) -- valid in DeviceRegistry but unreachable
# SET timeout: SnapshotJob.IntervalSeconds(1) * TimeoutMultiplier(0.8) = 0.8s
#
# Sequence:
#   1. Add FAKE-UNREACHABLE device to DeviceRegistry via devices ConfigMap
#   2. Apply tenant fixture pointing commands to 10.255.255.254:161
#   3. SnapshotJob reaches tier=4, enqueues command -> dispatched increments
#   4. CommandWorkerService SET to 10.255.255.254 times out -> failed increments
#
# Sub-assertions:
#   85a: CCV-04A: command.dispatched increments (dispatch precedes timeout failure)
#   85b: CCV-04B: command.failed increments on SET timeout to unreachable device
#
# Timeout label: device_name="FAKE-UNREACHABLE" (from DeviceRegistry, NOT IP:port)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save BOTH ConfigMaps before any modification
# ---------------------------------------------------------------------------

log_info "CCV-04: Saving original devices ConfigMap..."
save_configmap "simetra-devices" "simetra" "$FIXTURES_DIR/.original-devices-configmap.yaml" || true

log_info "CCV-04: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

# ---------------------------------------------------------------------------
# Add FAKE-UNREACHABLE device to DeviceRegistry via devices ConfigMap
# TryGetByIpPort(10.255.255.254, 161) must succeed before applying the tenant
# fixture, otherwise TenantVectorWatcher skips the Commands entry.
# device.Name = "FAKE-UNREACHABLE" (derived from CommunityString "Simetra.FAKE-UNREACHABLE")
# ---------------------------------------------------------------------------

log_info "CCV-04: Adding FAKE-UNREACHABLE device to DeviceRegistry..."
CURRENT_DEVICES=$(kubectl get configmap simetra-devices -n simetra -o jsonpath='{.data.devices\.json}')
UPDATED_DEVICES=$(echo "$CURRENT_DEVICES" | jq '. + [{
    "CommunityString": "Simetra.FAKE-UNREACHABLE",
    "IpAddress": "10.255.255.254",
    "Port": 161,
    "Polls": [{"IntervalSeconds": 10, "Metrics": [{"MetricName": "e2e_gauge_test"}]}]
}]')

DEVICE_CM_FILE=$(mktemp)
cat > "$DEVICE_CM_FILE" <<CMEOF
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-devices
  namespace: simetra
data:
  devices.json: |
$(echo "$UPDATED_DEVICES" | sed 's/^/    /')
CMEOF

kubectl apply -f "$DEVICE_CM_FILE" -n simetra
rm -f "$DEVICE_CM_FILE"

log_info "CCV-04: Waiting 15s for DeviceWatcher to register FAKE-UNREACHABLE..."
sleep 15

# ---------------------------------------------------------------------------
# Apply tenant fixture (valid CommandName e2e_set_bypass + unreachable command IP)
# ---------------------------------------------------------------------------

log_info "CCV-04: Applying tenant-cfg10-ccv-timeout.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg10-ccv-timeout.yaml" > /dev/null 2>&1 || true

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
# dispatched: device_name=tenant name (SnapshotJob uses tenant.Id)
# failed:     device_name="FAKE-UNREACHABLE" (timeout path uses device.Name, not IP:port)
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-ccv-timeout"')
BEFORE_FAILED=$(snapshot_counter "snmp_command_failed_total" 'device_name="FAKE-UNREACHABLE"')
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
poll_until 30 2 "snmp_command_dispatched_total" 'device_name="e2e-ccv-timeout"' "$BEFORE_SENT" || true

# ---------------------------------------------------------------------------
# Wait for CommandWorkerService timeout + OTel export
# SET timeout: 0.8s (IntervalSeconds=1 * TimeoutMultiplier=0.8)
# OTel export latency: up to ~15s
# ---------------------------------------------------------------------------

log_info "CCV-04: Waiting 15s for CommandWorkerService timeout (0.8s) and OTel export..."
sleep 15

# ---------------------------------------------------------------------------
# Snapshot both AFTER values
# ---------------------------------------------------------------------------

AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-ccv-timeout"')
AFTER_FAILED=$(snapshot_counter "snmp_command_failed_total" 'device_name="FAKE-UNREACHABLE"')
DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
DELTA_FAILED=$((AFTER_FAILED - BEFORE_FAILED))
log_info "CCV-04: After: dispatched=${AFTER_SENT} delta_dispatched=${DELTA_SENT} failed=${AFTER_FAILED} delta_failed=${DELTA_FAILED}"

# ---------------------------------------------------------------------------
# Sub-assertion 85a: CCV-04A dispatched increments (dispatch precedes timeout failure)
# ---------------------------------------------------------------------------

if [ "$DELTA_SENT" -ge 1 ]; then
    record_pass "CCV-04A: command.dispatched increments (dispatch precedes timeout failure)" \
        "dispatched_delta=${DELTA_SENT} dispatched_before=${BEFORE_SENT} dispatched_after=${AFTER_SENT}"
else
    record_fail "CCV-04A: command.dispatched increments (dispatch precedes timeout failure)" \
        "dispatched_delta=${DELTA_SENT} expected >= 1. dispatched_before=${BEFORE_SENT} dispatched_after=${AFTER_SENT}"
fi

# ---------------------------------------------------------------------------
# Sub-assertion 85b: CCV-04B command.failed increments on SET timeout
# CommandWorkerService line 152-159: catches OperationCanceledException (timeout),
# calls IncrementCommandFailed(device.Name) where device.Name="FAKE-UNREACHABLE"
# ---------------------------------------------------------------------------

if [ "$DELTA_FAILED" -ge 1 ]; then
    record_pass "CCV-04B: command.failed increments on SET timeout to unreachable device" \
        "failed_delta=${DELTA_FAILED} failed_before=${BEFORE_FAILED} failed_after=${AFTER_FAILED} (SET to 10.255.255.254 timed out after 0.8s)"
else
    record_fail "CCV-04B: command.failed increments on SET timeout to unreachable device" \
        "failed_delta=${DELTA_FAILED} expected >= 1. failed_before=${BEFORE_FAILED} failed_after=${AFTER_FAILED} (SET to 10.255.255.254 should timeout)"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore BOTH ConfigMaps
# Restore tenants first so tenant validation can use device registry
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

log_info "CCV-04: Restoring original devices ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-devices-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-devices-configmap.yaml" || \
        log_warn "CCV-04: Failed to restore devices ConfigMap from snapshot"
else
    log_warn "CCV-04: Original devices ConfigMap snapshot not found -- skipping restore"
fi
