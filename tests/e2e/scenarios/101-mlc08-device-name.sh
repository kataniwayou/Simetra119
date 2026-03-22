# Scenario 101: MLC-08 — device_name=E2E-SIM from community string Simetra.E2E-SIM
# Verifies that the community string is correctly parsed to derive the device_name label.
SCENARIO_NAME="MLC-08: device_name=E2E-SIM from community Simetra.E2E-SIM"

RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_gauge_test"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -gt 0 ]; then
    ACTUAL_DEVICE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.device_name')
    EVIDENCE="device_name=${ACTUAL_DEVICE} resolved_name=e2e_gauge_test"
    if [ "$ACTUAL_DEVICE" = "E2E-SIM" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "expected device_name=E2E-SIM got device_name=${ACTUAL_DEVICE}. ${EVIDENCE}"
    fi
else
    record_fail "$SCENARIO_NAME" "no snmp_gauge{device_name=\"E2E-SIM\"} series found for e2e_gauge_test"
fi
