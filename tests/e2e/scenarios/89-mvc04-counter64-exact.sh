# Scenario 89: MVC-04 -- snmp_gauge Counter64 exact value (e2e_counter64_test)
SCENARIO_NAME="MVC-04: snmp_gauge Counter64 exact value (e2e_counter64_test)"

RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_counter64_test"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -eq 0 ]; then
    record_fail "$SCENARIO_NAME" "no snmp_gauge series found for e2e_counter64_test"
else
    VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].value[1]')
    VALUE_INT=$(echo "$VALUE" | cut -d. -f1)
    EVIDENCE="resolved_name=e2e_counter64_test value=${VALUE}"

    if [ "$VALUE_INT" = "1000000" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "expected 1000000 got ${VALUE_INT}. ${EVIDENCE}"
    fi
fi
