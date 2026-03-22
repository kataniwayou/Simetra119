# Scenario 94: MLC-01 — source=poll label on snmp_gauge
# Verifies that snmp_gauge for e2e_gauge_test carries source="poll" from the poll pipeline.
SCENARIO_NAME="MLC-01: source=poll label on snmp_gauge (e2e_gauge_test)"

RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_gauge_test",source="poll"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -gt 0 ]; then
    ACTUAL_SOURCE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.source')
    EVIDENCE="source=${ACTUAL_SOURCE} resolved_name=e2e_gauge_test result_count=${RESULT_COUNT}"
    if [ "$ACTUAL_SOURCE" = "poll" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "expected source=poll got source=${ACTUAL_SOURCE}. ${EVIDENCE}"
    fi
else
    record_fail "$SCENARIO_NAME" "no snmp_gauge{source=\"poll\"} series found for E2E-SIM e2e_gauge_test"
fi
