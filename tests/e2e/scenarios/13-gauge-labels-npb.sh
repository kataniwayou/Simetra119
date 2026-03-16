# Scenario 13: snmp_gauge labels for NPB-01
# Verifies device_name, resolved_name, snmp_type labels and numeric value
SCENARIO_NAME="snmp_gauge labels for NPB-01 (npb_uptime)"

RESPONSE=$(query_prometheus 'snmp_gauge{device_name="NPB-01",resolved_name="npb_uptime"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -eq 0 ]; then
    record_fail "$SCENARIO_NAME" "no snmp_gauge series found for NPB-01 npb_uptime"
else
    DEVICE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.device_name')
    RESOLVED_NAME=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.resolved_name')
    SNMP_TYPE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.snmp_type')
    VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].value[1]')

    EVIDENCE="device_name=${DEVICE} resolved_name=${RESOLVED_NAME} snmp_type=${SNMP_TYPE} value=${VALUE}"

    if [ "$DEVICE" = "NPB-01" ] && \
       [ "$RESOLVED_NAME" = "npb_uptime" ] && \
       [ "$SNMP_TYPE" = "timeticks" ] && \
       echo "$VALUE" | grep -qE '^[0-9]+(\.[0-9]+)?$'; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "label mismatch: $EVIDENCE"
    fi
fi
