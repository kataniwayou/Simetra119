# Scenario 06: poll_unreachable increments for fake unreachable device
# Adds FAKE-UNREACHABLE device (IP 10.255.255.254) via ConfigMap, waits for
# poll failures to increment the unreachable counter, then leaves the device
# in place for scenario 07 (recovery test).
SCENARIO_NAME="poll_unreachable increments for FAKE-UNREACHABLE device"
METRIC="snmp_poll_unreachable_total"
FILTER='device_name="FAKE-UNREACHABLE"'

# Save original ConfigMap for restoration in scenario 07
ORIGINAL_CM="$SCRIPT_DIR/fixtures/.original-devices-configmap.yaml"
save_configmap "simetra-devices" "simetra" "$ORIGINAL_CM"

# Snapshot before adding fake device
BEFORE=$(snapshot_counter "$METRIC" "$FILTER")

# Apply ConfigMap with fake unreachable device (IP: 10.255.255.254)
log_info "Applying fake device ConfigMap..."
kubectl apply -f "$SCRIPT_DIR/fixtures/fake-device-configmap.yaml" -n simetra

# Wait for DeviceWatcherService to detect change and create poll job (~5s)
# Then wait for 3 consecutive poll failures (3 x 10s interval with 8s timeout = ~30s)
# Plus OTel export latency (~15s)
# Total: ~50-60s -- use 90s timeout to be safe
log_info "Waiting for FAKE-UNREACHABLE to become unreachable (up to 90s)..."
poll_until 90 5 "$METRIC" "$FILTER" "$BEFORE" || true

AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE=$(get_evidence "$METRIC" "$FILTER")
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "before=$BEFORE after=$AFTER delta=$DELTA | $EVIDENCE"
