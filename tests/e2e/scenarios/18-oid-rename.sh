# Scenario 18: OID rename propagates to Prometheus (MUT-01)
# Renames .999.1.1.0 from e2e_gauge_test to e2e_renamed_gauge in oidmaps
# ConfigMap, verifies the new resolved_name appears in Prometheus, then restores.
SCENARIO_NAME="OID rename propagates new resolved_name to Prometheus"

# Snapshot current ConfigMaps for safe restoration
snapshot_configmaps

# Apply mutated oidmaps with .999.1.1.0 renamed to e2e_renamed_gauge
log_info "Applying renamed oidmaps ConfigMap..."
kubectl apply -f "$SCRIPT_DIR/fixtures/oid-renamed-configmap.yaml" -n simetra

# Poll for the renamed metric to appear in Prometheus (60s deadline, 3s interval)
log_info "Waiting for e2e_renamed_gauge to appear in Prometheus (up to 60s)..."
DEADLINE=$(( $(date +%s) + 60 ))
FOUND=0
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    RESULT=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_renamed_gauge"}') || true
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
       [ "$RESOLVED_NAME" = "e2e_renamed_gauge" ] && \
       [ "$OID" = "1.3.6.1.4.1.47477.999.1.1.0" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "label mismatch: $EVIDENCE"
    fi
else
    record_fail "$SCENARIO_NAME" "e2e_renamed_gauge not found for E2E-SIM within 60s timeout"
fi

# Restore original ConfigMaps
log_info "Restoring original ConfigMaps..."
restore_configmaps

# Verify restoration: e2e_gauge_test should reappear
log_info "Waiting for e2e_gauge_test to reappear after restore (up to 60s)..."
DEADLINE=$(( $(date +%s) + 60 ))
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    RESTORE_RESULT=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_gauge_test"}') || true
    RESTORE_COUNT=$(echo "$RESTORE_RESULT" | jq -r '.data.result | length' 2>/dev/null) || RESTORE_COUNT=0
    if [ "$RESTORE_COUNT" -gt 0 ]; then
        log_info "Restoration confirmed: e2e_gauge_test reappeared in Prometheus"
        break
    fi
    sleep 3
done
