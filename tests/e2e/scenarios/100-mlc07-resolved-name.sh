# Scenario 100: MLC-07 — resolved_name matches oidmaps.json for e2e_gauge_test
# Cross-references OID 1.3.6.1.4.1.47477.999.1.1.0 to its resolved_name label.
# Queries by OID label to verify the pipeline's OID-to-name mapping is correct.
SCENARIO_NAME="MLC-07: resolved_name matches oidmaps.json for e2e_gauge_test"

RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",oid="1.3.6.1.4.1.47477.999.1.1.0"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -gt 0 ]; then
    ACTUAL_NAME=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.resolved_name')
    ACTUAL_OID=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.oid')
    EVIDENCE="oid=${ACTUAL_OID} resolved_name=${ACTUAL_NAME}"
    if [ "$ACTUAL_NAME" = "e2e_gauge_test" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "expected resolved_name=e2e_gauge_test got resolved_name=${ACTUAL_NAME}. ${EVIDENCE}"
    fi
else
    record_fail "$SCENARIO_NAME" "no snmp_gauge series found for oid=1.3.6.1.4.1.47477.999.1.1.0 device=E2E-SIM"
fi
