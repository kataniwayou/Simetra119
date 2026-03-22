# Scenario 92: MVC-07 -- snmp_info IpAddress exact value (e2e_ip_test)
# Note: MEDIUM confidence on exact string format -- INFO_VALUE is logged in EVIDENCE
# so any format difference (e.g. "IpAddress: 10.0.0.1") will appear in failure output.
SCENARIO_NAME="MVC-07: snmp_info IpAddress exact value (e2e_ip_test)"

RESPONSE=$(query_prometheus 'snmp_info{device_name="E2E-SIM",resolved_name="e2e_ip_test"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -eq 0 ]; then
    record_fail "$SCENARIO_NAME" "no snmp_info series found for e2e_ip_test"
else
    INFO_VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.value')
    PROM_VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].value[1]')
    PROM_VALUE_INT=$(echo "$PROM_VALUE" | cut -d. -f1)
    EVIDENCE="value_label=${INFO_VALUE} prom_value=${PROM_VALUE}"

    if [ "$INFO_VALUE" = "10.0.0.1" ] && [ "$PROM_VALUE_INT" = "1" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "expected 10.0.0.1/1, got ${EVIDENCE}"
    fi
fi
