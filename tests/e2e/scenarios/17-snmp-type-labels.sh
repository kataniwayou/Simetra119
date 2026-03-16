# Scenario 17: All 5 gauge snmp_type values verified via E2E-SIM
# Checks gauge32, integer32, counter32, counter64, timeticks

SNMP_TYPE_CHECKS=(
    "e2e_gauge_test:gauge32"
    "e2e_integer_test:integer32"
    "e2e_counter32_test:counter32"
    "e2e_counter64_test:counter64"
    "e2e_timeticks_test:timeticks"
)

for CHECK in "${SNMP_TYPE_CHECKS[@]}"; do
    RESOLVED_NAME="${CHECK%%:*}"
    EXPECTED_TYPE="${CHECK##*:}"
    SCENARIO_NAME="snmp_type=${EXPECTED_TYPE} for ${RESOLVED_NAME}"

    RESPONSE=$(query_prometheus "snmp_gauge{device_name=\"E2E-SIM\",resolved_name=\"${RESOLVED_NAME}\"}")
    RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

    if [ "$RESULT_COUNT" -eq 0 ]; then
        record_fail "$SCENARIO_NAME" "no snmp_gauge series found for E2E-SIM ${RESOLVED_NAME}"
    else
        ACTUAL_TYPE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.snmp_type')
        VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].value[1]')
        EVIDENCE="resolved_name=${RESOLVED_NAME} expected_type=${EXPECTED_TYPE} actual_type=${ACTUAL_TYPE} value=${VALUE}"

        if [ "$ACTUAL_TYPE" = "$EXPECTED_TYPE" ] && \
           echo "$VALUE" | grep -qE '^[0-9]+(\.[0-9]+)?$'; then
            record_pass "$SCENARIO_NAME" "$EVIDENCE"
        else
            record_fail "$SCENARIO_NAME" "type mismatch: $EVIDENCE"
        fi
    fi
done
