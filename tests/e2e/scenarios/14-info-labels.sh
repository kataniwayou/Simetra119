# Scenario 14: snmp_info labels for E2E-SIM
# Verifies value label exists (non-empty), snmp_type, device_name, and numeric value=1

# --- Sub-scenario 14a: e2e_info_test (octetstring) ---
SCENARIO_NAME="snmp_info labels for E2E-SIM (e2e_info_test)"

RESPONSE=$(query_prometheus 'snmp_info{device_name="E2E-SIM",resolved_name="e2e_info_test"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -eq 0 ]; then
    record_fail "$SCENARIO_NAME" "no snmp_info series found for E2E-SIM e2e_info_test"
else
    DEVICE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.device_name')
    SNMP_TYPE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.snmp_type')
    INFO_VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.value')
    PROM_VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].value[1]')

    EVIDENCE="device_name=${DEVICE} snmp_type=${SNMP_TYPE} value_label=${INFO_VALUE} prom_value=${PROM_VALUE}"

    if [ "$DEVICE" = "E2E-SIM" ] && \
       [ "$SNMP_TYPE" = "octetstring" ] && \
       [ -n "$INFO_VALUE" ] && [ "$INFO_VALUE" != "null" ] && \
       [ "$PROM_VALUE" = "1" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "label mismatch: $EVIDENCE"
    fi
fi

# --- Sub-scenario 14b: e2e_ip_test (ipaddress) ---
SCENARIO_NAME="snmp_info labels for E2E-SIM (e2e_ip_test)"

RESPONSE=$(query_prometheus 'snmp_info{device_name="E2E-SIM",resolved_name="e2e_ip_test"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -eq 0 ]; then
    record_fail "$SCENARIO_NAME" "no snmp_info series found for E2E-SIM e2e_ip_test"
else
    DEVICE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.device_name')
    SNMP_TYPE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.snmp_type')
    INFO_VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.value')
    PROM_VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].value[1]')

    EVIDENCE="device_name=${DEVICE} snmp_type=${SNMP_TYPE} value_label=${INFO_VALUE} prom_value=${PROM_VALUE}"

    if [ "$DEVICE" = "E2E-SIM" ] && \
       [ "$SNMP_TYPE" = "ipaddress" ] && \
       [ -n "$INFO_VALUE" ] && [ "$INFO_VALUE" != "null" ] && \
       [ "$PROM_VALUE" = "1" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "label mismatch: $EVIDENCE"
    fi
fi
