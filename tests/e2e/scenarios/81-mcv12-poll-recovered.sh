# Scenario 81: MCV-12 -- poll.recovered increments when device becomes reachable
# Patches FAKE-UNREACHABLE device to point to E2E simulator, waits for recovery
# counter to increment, then restores original ConfigMap (removing FAKE-UNREACHABLE).
# Depends on scenario 80 having left FAKE-UNREACHABLE in unreachable state.
SCENARIO_NAME="MCV-12: poll.recovered increments when device becomes reachable"
METRIC="snmp_poll_recovered_total"
FILTER='device_name="FAKE-UNREACHABLE"'

# Snapshot before recovery
BEFORE=$(snapshot_counter "$METRIC" "$FILTER")

# Patch the ConfigMap: change FAKE-UNREACHABLE IP to E2E simulator + set CommunityString
log_info "Patching FAKE-UNREACHABLE device to point to E2E simulator..."

# Get current devices.json, patch FAKE-UNREACHABLE entry with jq, and re-apply
CURRENT_JSON=$(kubectl get configmap simetra-devices -n simetra -o jsonpath='{.data.devices\.json}')
PATCHED_JSON=$(echo "$CURRENT_JSON" | jq '
    map(if .CommunityString == "Simetra.FAKE-UNREACHABLE" then
        .IpAddress = "e2e-simulator.simetra.svc.cluster.local"
    else . end)')

# Create a temporary ConfigMap YAML with patched JSON
PATCH_FILE=$(mktemp)
cat > "$PATCH_FILE" <<CMEOF
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-devices
  namespace: simetra
data:
  devices.json: |
$(echo "$PATCHED_JSON" | sed 's/^/    /')
CMEOF

kubectl apply -f "$PATCH_FILE" -n simetra
rm -f "$PATCH_FILE"

# Wait for DeviceWatcherService to reload + first successful poll + OTel export
# Recovery happens on the FIRST successful poll after unreachable state
log_info "Waiting for FAKE-UNREACHABLE to recover (up to 90s)..."
poll_until 90 5 "$METRIC" "$FILTER" "$BEFORE" || true

AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE=$(get_evidence "$METRIC" "$FILTER")
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "before=$BEFORE after=$AFTER delta=$DELTA | $EVIDENCE"

# Restore original ConfigMap (removes FAKE-UNREACHABLE device entirely)
ORIGINAL_CM="$SCRIPT_DIR/fixtures/.original-devices-configmap.yaml"
if [ -f "$ORIGINAL_CM" ]; then
    log_info "Restoring original devices ConfigMap..."
    restore_configmap "$ORIGINAL_CM"
    rm -f "$ORIGINAL_CM"
fi
