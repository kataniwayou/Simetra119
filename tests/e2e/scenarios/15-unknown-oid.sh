# Scenario 15: Unmapped OIDs classified as resolved_name="Unknown"
# Adds unmapped OIDs (.999.2.1.0 and .999.2.2.0) to E2E-SIM poll config,
# waits for them to appear as Unknown in Prometheus, then restores original
# ConfigMap.
SCENARIO_NAME="Unknown OID classification via ConfigMap mutation"

# Snapshot current ConfigMaps for safe restoration
snapshot_configmaps

# Apply mutated ConfigMap with unmapped OIDs added to E2E-SIM
log_info "Applying E2E-SIM unmapped OID ConfigMap..."
kubectl apply -f "$SCRIPT_DIR/fixtures/e2e-sim-unmapped-configmap.yaml" -n simetra

# Wait for DeviceWatcherService to detect change, poll new OIDs, and OTel export
# ConfigMap change detection ~5s + poll interval 10s + OTel export 15s = ~30s
log_info "Waiting for unmapped OIDs to appear as Unknown in Prometheus (up to 60s)..."
DEADLINE=$(( $(date +%s) + 60 ))
GAUGE_FOUND=0
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    RESULT=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="Unknown"}') || true
    COUNT=$(echo "$RESULT" | jq -r '.data.result | length' 2>/dev/null) || COUNT=0
    if [ "$COUNT" -gt 0 ]; then
        GAUGE_FOUND=1
        break
    fi
    sleep 3
done

# --- Verify gauge-type unknown OID (.999.2.1.0) ---
GAUGE_SCENARIO="Unknown gauge OID .999.2.1.0 classified correctly"

if [ "$GAUGE_FOUND" -eq 1 ]; then
    ENTRY=$(echo "$RESULT" | jq -r '.data.result[] | select(.metric.oid == "1.3.6.1.4.1.47477.999.2.1.0")')

    if [ -n "$ENTRY" ]; then
        DEVICE=$(echo "$ENTRY" | jq -r '.metric.device_name')
        RESOLVED_NAME=$(echo "$ENTRY" | jq -r '.metric.resolved_name')
        OID=$(echo "$ENTRY" | jq -r '.metric.oid')
        SNMP_TYPE=$(echo "$ENTRY" | jq -r '.metric.snmp_type')

        EVIDENCE="device_name=${DEVICE} resolved_name=${RESOLVED_NAME} oid=${OID} snmp_type=${SNMP_TYPE}"

        if [ "$DEVICE" = "E2E-SIM" ] && \
           [ "$RESOLVED_NAME" = "Unknown" ] && \
           [ "$SNMP_TYPE" = "gauge32" ]; then
            record_pass "$GAUGE_SCENARIO" "$EVIDENCE"
        else
            record_fail "$GAUGE_SCENARIO" "label mismatch: $EVIDENCE"
        fi
    else
        record_fail "$GAUGE_SCENARIO" "OID 1.3.6.1.4.1.47477.999.2.1.0 not found in Unknown gauge results"
    fi
else
    record_fail "$GAUGE_SCENARIO" "no snmp_gauge{resolved_name=Unknown} found for E2E-SIM within timeout"
fi

# --- Verify string-type unknown OID (.999.2.2.0) ---
INFO_SCENARIO="Unknown info OID .999.2.2.0 classified correctly"

log_info "Checking snmp_info for unknown OctetString OID..."
INFO_RESULT=$(query_prometheus 'snmp_info{device_name="E2E-SIM",resolved_name="Unknown"}') || true
INFO_COUNT=$(echo "$INFO_RESULT" | jq -r '.data.result | length' 2>/dev/null) || INFO_COUNT=0

if [ "$INFO_COUNT" -gt 0 ]; then
    INFO_ENTRY=$(echo "$INFO_RESULT" | jq -r '.data.result[] | select(.metric.oid == "1.3.6.1.4.1.47477.999.2.2.0")')

    if [ -n "$INFO_ENTRY" ]; then
        DEVICE=$(echo "$INFO_ENTRY" | jq -r '.metric.device_name')
        RESOLVED_NAME=$(echo "$INFO_ENTRY" | jq -r '.metric.resolved_name')
        OID=$(echo "$INFO_ENTRY" | jq -r '.metric.oid')
        SNMP_TYPE=$(echo "$INFO_ENTRY" | jq -r '.metric.snmp_type')

        EVIDENCE="device_name=${DEVICE} resolved_name=${RESOLVED_NAME} oid=${OID} snmp_type=${SNMP_TYPE}"

        if [ "$DEVICE" = "E2E-SIM" ] && \
           [ "$RESOLVED_NAME" = "Unknown" ] && \
           [ "$SNMP_TYPE" = "octetstring" ]; then
            record_pass "$INFO_SCENARIO" "$EVIDENCE"
        else
            record_fail "$INFO_SCENARIO" "label mismatch: $EVIDENCE"
        fi
    else
        record_fail "$INFO_SCENARIO" "OID 1.3.6.1.4.1.47477.999.2.2.0 not found in Unknown info results"
    fi
else
    record_fail "$INFO_SCENARIO" "no snmp_info{resolved_name=Unknown} found for E2E-SIM within timeout"
fi

# Restore original ConfigMaps
log_info "Restoring original ConfigMaps..."
restore_configmaps
log_info "ConfigMaps restored -- cluster back to original state"
