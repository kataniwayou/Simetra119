# Scenario 20: Adding OID mapping resolves previously Unknown OID (MUT-03)
# Step 1: Apply unmapped device config so .999.2.1.0 is polled (appears as Unknown)
# Step 2: Apply oidmaps with .999.2.1.0 mapped to e2e_unmapped_gauge
# Verifies the OID transitions from Unknown to the correct resolved_name.
SCENARIO_NAME="Adding OID mapping resolves Unknown to e2e_unmapped_gauge"

# Snapshot current ConfigMaps for safe restoration
snapshot_configmaps

# Step 1: Ensure unmapped OIDs are being polled (devices ConfigMap with .999.2.x OIDs)
log_info "Step 1: Applying E2E-SIM unmapped device config to poll .999.2.1.0..."
kubectl apply -f "$SCRIPT_DIR/fixtures/e2e-sim-unmapped-configmap.yaml" -n simetra

# Wait for the unmapped OID to appear as Unknown
log_info "Waiting for .999.2.1.0 to appear as Unknown in Prometheus (up to 60s)..."
DEADLINE=$(( $(date +%s) + 60 ))
UNKNOWN_FOUND=0
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    RESULT=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="Unknown",oid="1.3.6.1.4.1.47477.999.2.1.0"}') || true
    COUNT=$(echo "$RESULT" | jq -r '.data.result | length' 2>/dev/null) || COUNT=0
    if [ "$COUNT" -gt 0 ]; then
        UNKNOWN_FOUND=1
        break
    fi
    sleep 3
done

if [ "$UNKNOWN_FOUND" -eq 0 ]; then
    record_fail "$SCENARIO_NAME" "prerequisite failed: .999.2.1.0 never appeared as Unknown within 60s"
    # Restore and exit early
    log_info "Restoring original ConfigMaps..."
    restore_configmaps
    log_info "ConfigMaps restored -- cluster back to original state"
    return 0 2>/dev/null || true
fi

log_info "Confirmed .999.2.1.0 appears as Unknown -- proceeding to Step 2"

# Step 2: Apply oidmaps with the new mapping for .999.2.1.0
log_info "Step 2: Applying oidmaps with .999.2.1.0 mapped to e2e_unmapped_gauge..."
kubectl apply -f "$SCRIPT_DIR/fixtures/oid-added-configmap.yaml" -n simetra

# Poll for the mapped metric to appear (60s deadline, 3s interval)
log_info "Waiting for e2e_unmapped_gauge to appear in Prometheus (up to 60s)..."
DEADLINE=$(( $(date +%s) + 60 ))
FOUND=0
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    RESULT=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_unmapped_gauge"}') || true
    COUNT=$(echo "$RESULT" | jq -r '.data.result | length' 2>/dev/null) || COUNT=0
    if [ "$COUNT" -gt 0 ]; then
        FOUND=1
        break
    fi
    sleep 3
done

if [ "$FOUND" -eq 1 ]; then
    DEVICE=$(echo "$RESULT" | jq -r '.data.result[0].metric.device_name')
    RESOLVED_NAME=$(echo "$RESULT" | jq -r '.data.result[0].metric.resolved_name')
    OID=$(echo "$RESULT" | jq -r '.data.result[0].metric.oid')

    EVIDENCE="device_name=${DEVICE} resolved_name=${RESOLVED_NAME} oid=${OID}"

    if [ "$DEVICE" = "E2E-SIM" ] && \
       [ "$RESOLVED_NAME" = "e2e_unmapped_gauge" ] && \
       [ "$OID" = "1.3.6.1.4.1.47477.999.2.1.0" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "label mismatch: $EVIDENCE"
    fi
else
    record_fail "$SCENARIO_NAME" "e2e_unmapped_gauge not found for E2E-SIM within 60s timeout"
fi

# Restore original ConfigMaps (both devices and oidmaps)
log_info "Restoring original ConfigMaps..."
restore_configmaps
log_info "ConfigMaps restored -- cluster back to original state"
