# Scenario 97: MLC-04 — source=synthetic label on snmp_gauge
# Verifies that snmp_gauge for e2e_total_util carries source="synthetic" from aggregate computation.
# Synthetic metrics use oid="0.0" sentinel; do NOT filter by a real OID.
SCENARIO_NAME="MLC-04: source=synthetic label on snmp_gauge (e2e_total_util)"

RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_total_util",source="synthetic"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -gt 0 ]; then
    ACTUAL_SOURCE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.source')
    EVIDENCE="source=${ACTUAL_SOURCE} resolved_name=e2e_total_util result_count=${RESULT_COUNT}"
    if [ "$ACTUAL_SOURCE" = "synthetic" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "expected source=synthetic got source=${ACTUAL_SOURCE}. ${EVIDENCE}"
    fi
else
    record_fail "$SCENARIO_NAME" "no snmp_gauge{source=\"synthetic\"} series found for E2E-SIM e2e_total_util"
fi
