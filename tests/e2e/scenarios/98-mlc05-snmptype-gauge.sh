# Scenario 98: MLC-05 — snmp_type labels correct for all 5 snmp_gauge types
# Verifies gauge32, integer32, counter32, counter64, timeticks all carry correct snmp_type labels.
SCENARIO_NAME="MLC-05: snmp_type labels correct for all 5 snmp_gauge types"

PASS_TYPES=0
FAIL_DETAILS=""

# Check gauge32
R=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_gauge_test"}')
C=$(echo "$R" | jq -r '.data.result | length')
if [ "$C" -gt 0 ]; then
    T=$(echo "$R" | jq -r '.data.result[0].metric.snmp_type')
    if [ "$T" = "gauge32" ]; then
        PASS_TYPES=$((PASS_TYPES + 1))
        EVIDENCE_GAUGE="e2e_gauge_test=gauge32"
    else
        FAIL_DETAILS="${FAIL_DETAILS} e2e_gauge_test:expected=gauge32,got=${T}"
    fi
else
    FAIL_DETAILS="${FAIL_DETAILS} e2e_gauge_test:no_series"
fi

# Check integer32
R=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_integer_test"}')
C=$(echo "$R" | jq -r '.data.result | length')
if [ "$C" -gt 0 ]; then
    T=$(echo "$R" | jq -r '.data.result[0].metric.snmp_type')
    if [ "$T" = "integer32" ]; then
        PASS_TYPES=$((PASS_TYPES + 1))
        EVIDENCE_INTEGER="e2e_integer_test=integer32"
    else
        FAIL_DETAILS="${FAIL_DETAILS} e2e_integer_test:expected=integer32,got=${T}"
    fi
else
    FAIL_DETAILS="${FAIL_DETAILS} e2e_integer_test:no_series"
fi

# Check counter32
R=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_counter32_test"}')
C=$(echo "$R" | jq -r '.data.result | length')
if [ "$C" -gt 0 ]; then
    T=$(echo "$R" | jq -r '.data.result[0].metric.snmp_type')
    if [ "$T" = "counter32" ]; then
        PASS_TYPES=$((PASS_TYPES + 1))
        EVIDENCE_COUNTER32="e2e_counter32_test=counter32"
    else
        FAIL_DETAILS="${FAIL_DETAILS} e2e_counter32_test:expected=counter32,got=${T}"
    fi
else
    FAIL_DETAILS="${FAIL_DETAILS} e2e_counter32_test:no_series"
fi

# Check counter64
R=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_counter64_test"}')
C=$(echo "$R" | jq -r '.data.result | length')
if [ "$C" -gt 0 ]; then
    T=$(echo "$R" | jq -r '.data.result[0].metric.snmp_type')
    if [ "$T" = "counter64" ]; then
        PASS_TYPES=$((PASS_TYPES + 1))
        EVIDENCE_COUNTER64="e2e_counter64_test=counter64"
    else
        FAIL_DETAILS="${FAIL_DETAILS} e2e_counter64_test:expected=counter64,got=${T}"
    fi
else
    FAIL_DETAILS="${FAIL_DETAILS} e2e_counter64_test:no_series"
fi

# Check timeticks
R=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_timeticks_test"}')
C=$(echo "$R" | jq -r '.data.result | length')
if [ "$C" -gt 0 ]; then
    T=$(echo "$R" | jq -r '.data.result[0].metric.snmp_type')
    if [ "$T" = "timeticks" ]; then
        PASS_TYPES=$((PASS_TYPES + 1))
        EVIDENCE_TIMETICKS="e2e_timeticks_test=timeticks"
    else
        FAIL_DETAILS="${FAIL_DETAILS} e2e_timeticks_test:expected=timeticks,got=${T}"
    fi
else
    FAIL_DETAILS="${FAIL_DETAILS} e2e_timeticks_test:no_series"
fi

if [ "$PASS_TYPES" -eq 5 ]; then
    EVIDENCE="${EVIDENCE_GAUGE:-} ${EVIDENCE_INTEGER:-} ${EVIDENCE_COUNTER32:-} ${EVIDENCE_COUNTER64:-} ${EVIDENCE_TIMETICKS:-}"
    record_pass "$SCENARIO_NAME" "all 5 snmp_type labels correct: ${EVIDENCE}"
else
    record_fail "$SCENARIO_NAME" "snmp_type mismatch (${PASS_TYPES}/5 correct):${FAIL_DETAILS}"
fi
