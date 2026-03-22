# Scenario 88: MVC-03 -- snmp_gauge Counter32 exact value (e2e_counter32_test)
SCENARIO_NAME="MVC-03: snmp_gauge Counter32 exact value (e2e_counter32_test)"

RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_counter32_test"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -eq 0 ]; then
    record_fail "$SCENARIO_NAME" "no snmp_gauge series found for e2e_counter32_test"
else
    VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].value[1]')
    VALUE_INT=$(echo "$VALUE" | cut -d. -f1)
    EVIDENCE="resolved_name=e2e_counter32_test value=${VALUE}"

    if [ "$VALUE_INT" = "5000" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "expected 5000 got ${VALUE_INT}. ${EVIDENCE}"
    fi
fi
