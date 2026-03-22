# Scenario 91: MVC-06 -- snmp_info OctetString exact value (e2e_info_test)
SCENARIO_NAME="MVC-06: snmp_info OctetString exact value (e2e_info_test)"

RESPONSE=$(query_prometheus 'snmp_info{device_name="E2E-SIM",resolved_name="e2e_info_test"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -eq 0 ]; then
    record_fail "$SCENARIO_NAME" "no snmp_info series found for e2e_info_test"
else
    INFO_VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.value')
    PROM_VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].value[1]')
    PROM_VALUE_INT=$(echo "$PROM_VALUE" | cut -d. -f1)
    EVIDENCE="value_label=${INFO_VALUE} prom_value=${PROM_VALUE}"

    if [ "$INFO_VALUE" = "E2E-TEST-VALUE" ] && [ "$PROM_VALUE_INT" = "1" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "expected E2E-TEST-VALUE/1, got ${EVIDENCE}"
    fi
fi
