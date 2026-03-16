# Scenario 19: OID removal causes resolved_name="Unknown" (MUT-02)
# Removes .999.1.1.0 from oidmaps ConfigMap so the polled OID has no mapping,
# verifies it appears as resolved_name="Unknown" in Prometheus, then restores.
SCENARIO_NAME="OID removal causes resolved_name=Unknown in Prometheus"

# Snapshot current ConfigMaps for safe restoration
snapshot_configmaps

# Apply mutated oidmaps with .999.1.1.0 removed
log_info "Applying oidmaps ConfigMap with .999.1.1.0 removed..."
kubectl apply -f "$SCRIPT_DIR/fixtures/oid-removed-configmap.yaml" -n simetra

# Poll for Unknown metric with the specific OID to appear (60s deadline, 3s interval)
log_info "Waiting for .999.1.1.0 to appear as Unknown in Prometheus (up to 60s)..."
DEADLINE=$(( $(date +%s) + 60 ))
FOUND=0
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    RESULT=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="Unknown",oid="1.3.6.1.4.1.47477.999.1.1.0"}') || true
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
       [ "$RESOLVED_NAME" = "Unknown" ] && \
       [ "$OID" = "1.3.6.1.4.1.47477.999.1.1.0" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "label mismatch: $EVIDENCE"
    fi
else
    record_fail "$SCENARIO_NAME" "resolved_name=Unknown for OID .999.1.1.0 not found within 60s timeout"
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
