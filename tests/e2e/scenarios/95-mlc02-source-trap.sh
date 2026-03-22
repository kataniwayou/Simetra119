# Scenario 95: MLC-02 — source=trap label on snmp_gauge
# Verifies that snmp_gauge for e2e_gauge_test carries source="trap" from the trap pipeline.
# E2E-SIM sends traps every 30s; poll up to 45s to ensure data is present.
SCENARIO_NAME="MLC-02: source=trap label on snmp_gauge (e2e_gauge_test)"

log_info "Polling for trap-originated snmp_gauge from E2E-SIM (up to 45s)..."
DEADLINE=$(( $(date +%s) + 45 ))
RESULT=""
COUNT=0
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    RESULT=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_gauge_test",source="trap"}') || true
    COUNT=$(echo "$RESULT" | jq -r '.data.result | length' 2>/dev/null) || COUNT=0
    [ "$COUNT" -gt 0 ] && break
    sleep 3
done

if [ "$COUNT" -gt 0 ]; then
    ACTUAL_SOURCE=$(echo "$RESULT" | jq -r '.data.result[0].metric.source')
    EVIDENCE="source=${ACTUAL_SOURCE} resolved_name=e2e_gauge_test result_count=${COUNT}"
    if [ "$ACTUAL_SOURCE" = "trap" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "expected source=trap got source=${ACTUAL_SOURCE}. ${EVIDENCE}"
    fi
else
    record_fail "$SCENARIO_NAME" "no snmp_gauge{source=\"trap\"} found for E2E-SIM e2e_gauge_test within 45s timeout"
fi
