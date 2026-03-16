# Scenario 11: snmp_gauge labels for E2E-SIM
# Verifies resolved_name, device_name, oid, snmp_type labels and numeric value
SCENARIO_NAME="snmp_gauge labels for E2E-SIM (e2e_gauge_test)"

RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_gauge_test"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -eq 0 ]; then
    record_fail "$SCENARIO_NAME" "no snmp_gauge series found for E2E-SIM e2e_gauge_test"
else
    DEVICE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.device_name')
    RESOLVED_NAME=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.resolved_name')
    OID=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.oid')
    SNMP_TYPE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.snmp_type')
    VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].value[1]')

    EVIDENCE="device_name=${DEVICE} resolved_name=${RESOLVED_NAME} oid=${OID} snmp_type=${SNMP_TYPE} value=${VALUE}"

    if [ "$DEVICE" = "E2E-SIM" ] && \
       [ "$RESOLVED_NAME" = "e2e_gauge_test" ] && \
       [ "$OID" = "1.3.6.1.4.1.47477.999.1.1.0" ] && \
       [ "$SNMP_TYPE" = "gauge32" ] && \
       echo "$VALUE" | grep -qE '^[0-9]+(\.[0-9]+)?$'; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "label mismatch: $EVIDENCE"
    fi
fi
