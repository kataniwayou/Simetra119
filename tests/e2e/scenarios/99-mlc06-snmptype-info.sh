# Scenario 99: MLC-06 — snmp_type labels correct for snmp_info types
# Verifies octetstring and ipaddress types on snmp_info carry correct snmp_type labels.
SCENARIO_NAME="MLC-06: snmp_type labels correct for snmp_info types"

PASS_TYPES=0
FAIL_DETAILS=""

# Check octetstring
R=$(query_prometheus 'snmp_info{device_name="E2E-SIM",resolved_name="e2e_info_test"}')
C=$(echo "$R" | jq -r '.data.result | length')
if [ "$C" -gt 0 ]; then
    T=$(echo "$R" | jq -r '.data.result[0].metric.snmp_type')
    if [ "$T" = "octetstring" ]; then
        PASS_TYPES=$((PASS_TYPES + 1))
        EVIDENCE_INFO="e2e_info_test=octetstring"
    else
        FAIL_DETAILS="${FAIL_DETAILS} e2e_info_test:expected=octetstring,got=${T}"
    fi
else
    FAIL_DETAILS="${FAIL_DETAILS} e2e_info_test:no_series"
fi

# Check ipaddress
R=$(query_prometheus 'snmp_info{device_name="E2E-SIM",resolved_name="e2e_ip_test"}')
C=$(echo "$R" | jq -r '.data.result | length')
if [ "$C" -gt 0 ]; then
    T=$(echo "$R" | jq -r '.data.result[0].metric.snmp_type')
    if [ "$T" = "ipaddress" ]; then
        PASS_TYPES=$((PASS_TYPES + 1))
        EVIDENCE_IP="e2e_ip_test=ipaddress"
    else
        FAIL_DETAILS="${FAIL_DETAILS} e2e_ip_test:expected=ipaddress,got=${T}"
    fi
else
    FAIL_DETAILS="${FAIL_DETAILS} e2e_ip_test:no_series"
fi

if [ "$PASS_TYPES" -eq 2 ]; then
    EVIDENCE="${EVIDENCE_INFO:-} ${EVIDENCE_IP:-}"
    record_pass "$SCENARIO_NAME" "both snmp_info snmp_type labels correct: ${EVIDENCE}"
else
    record_fail "$SCENARIO_NAME" "snmp_type mismatch (${PASS_TYPES}/2 correct):${FAIL_DETAILS}"
fi
