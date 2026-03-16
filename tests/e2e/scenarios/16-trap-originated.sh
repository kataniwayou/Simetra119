# Scenario 16: Trap-originated metrics appear with correct labels
# E2E-SIM sends traps every 30s with varbind .999.1.1.0 (Gauge32=42)
# These should appear as snmp_gauge{source="trap",device_name="E2E-SIM"}
SCENARIO_NAME="Trap-originated metric reaches Prometheus with correct labels"
QUERY='snmp_gauge{device_name="E2E-SIM",source="trap",resolved_name="e2e_gauge_test"}'

# E2E-SIM traps every 30s -- data may not be present yet, poll up to 45s
log_info "Polling for trap-originated snmp_gauge from E2E-SIM (up to 45s)..."
DEADLINE=$(( $(date +%s) + 45 ))
RESULT=""
COUNT=0
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    RESULT=$(query_prometheus "$QUERY") || true
    COUNT=$(echo "$RESULT" | jq -r '.data.result | length' 2>/dev/null) || COUNT=0
    if [ "$COUNT" -gt 0 ]; then
        break
    fi
    sleep 3
done

if [ "$COUNT" -gt 0 ]; then
    DEVICE=$(echo "$RESULT" | jq -r '.data.result[0].metric.device_name')
    RESOLVED_NAME=$(echo "$RESULT" | jq -r '.data.result[0].metric.resolved_name')
    OID=$(echo "$RESULT" | jq -r '.data.result[0].metric.oid')
    SNMP_TYPE=$(echo "$RESULT" | jq -r '.data.result[0].metric.snmp_type')
    SOURCE=$(echo "$RESULT" | jq -r '.data.result[0].metric.source')
    VALUE=$(echo "$RESULT" | jq -r '.data.result[0].value[1]')

    EVIDENCE="device_name=${DEVICE} resolved_name=${RESOLVED_NAME} oid=${OID} snmp_type=${SNMP_TYPE} source=${SOURCE} value=${VALUE}"

    if [ "$DEVICE" = "E2E-SIM" ] && \
       [ "$OID" = "1.3.6.1.4.1.47477.999.1.1.0" ] && \
       [ "$SOURCE" = "trap" ] && \
       [ "$SNMP_TYPE" = "gauge32" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "label mismatch: $EVIDENCE"
    fi
else
    record_fail "$SCENARIO_NAME" "no trap-originated snmp_gauge found for E2E-SIM within 45s timeout"
fi
