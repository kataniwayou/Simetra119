# Scenario 06: poll_unreachable increments for fake unreachable device
# Adds FAKE-UNREACHABLE device (IP 10.255.255.254) via ConfigMap, waits for
# poll failures to increment the unreachable counter, then leaves the device
# in place for scenario 07 (recovery test).
#
# Idempotency: The DeviceUnreachabilityTracker is a singleton that persists
# across device add/remove cycles. If FAKE-UNREACHABLE was left in unreachable
# state from a previous run, the transition counter won't fire again. To fix
# this, we first add FAKE-UNREACHABLE at a reachable IP to force a recovery
# event (resetting _isUnreachable=false), then switch to the unreachable IP.
SCENARIO_NAME="poll_unreachable increments for FAKE-UNREACHABLE device"
METRIC="snmp_poll_unreachable_total"
FILTER='device_name="FAKE-UNREACHABLE"'

# Save original ConfigMap for restoration in scenario 07
ORIGINAL_CM="$SCRIPT_DIR/fixtures/.original-devices-configmap.yaml"
save_configmap "simetra-devices" "simetra" "$ORIGINAL_CM"

# --- Pre-recovery step: ensure tracker is in clean (not-unreachable) state ---
# Build a ConfigMap with FAKE-UNREACHABLE pointing to E2E simulator (reachable)
CURRENT_JSON=$(kubectl get configmap simetra-devices -n simetra -o jsonpath='{.data.devices\.json}')
REACHABLE_JSON=$(echo "$CURRENT_JSON" | jq '. + [{
    "CommunityString": "Simetra.FAKE-UNREACHABLE",
    "IpAddress": "e2e-simulator.simetra.svc.cluster.local",
    "Port": 161,
    "Polls": [{"IntervalSeconds": 10, "Metrics": [{"MetricName": "e2e_gauge_test"}]}]
}]')

RECOVERY_FILE=$(mktemp)
cat > "$RECOVERY_FILE" <<CMEOF
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-devices
  namespace: simetra
data:
  devices.json: |
$(echo "$REACHABLE_JSON" | sed 's/^/    /')
CMEOF

log_info "Pre-recovery: adding FAKE-UNREACHABLE at reachable IP to reset tracker..."
kubectl apply -f "$RECOVERY_FILE" -n simetra
rm -f "$RECOVERY_FILE"

# Wait for 1 successful poll to trigger RecordSuccess (resets _isUnreachable)
log_info "Waiting 20s for recovery poll + tracker reset..."
sleep 20

# --- Now proceed with the actual unreachable test ---
BEFORE=$(snapshot_counter "$METRIC" "$FILTER")

# Apply ConfigMap with fake unreachable device (IP: 10.255.255.254)
log_info "Applying fake device ConfigMap (unreachable IP)..."
kubectl apply -f "$SCRIPT_DIR/fixtures/fake-device-configmap.yaml" -n simetra

# Wait for DeviceWatcherService to detect change and create poll job (~5s)
# Then wait for 3 consecutive poll failures (3 x 10s interval with 8s timeout = ~30s)
# Plus OTel export latency (~15s)
# Total: ~50-60s -- use 120s timeout to be safe
log_info "Waiting for FAKE-UNREACHABLE to become unreachable (up to 120s)..."
poll_until 120 5 "$METRIC" "$FILTER" "$BEFORE" || true

AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE=$(get_evidence "$METRIC" "$FILTER")
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "before=$BEFORE after=$AFTER delta=$DELTA | $EVIDENCE"
