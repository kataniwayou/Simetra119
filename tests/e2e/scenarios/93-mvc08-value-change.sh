# Scenario 93: MVC-08 -- snmp_gauge value updates after simulator change
SCENARIO_NAME="MVC-08: snmp_gauge value updates after simulator change (42->99)"

# Override Gauge32 OID (.999.1.1) to 99
sim_set_oid "1.1" "99"

# Poll Prometheus until value reflects 99 (40s deadline, 3s interval)
DEADLINE=$(( $(date +%s) + 40 ))
FOUND=0
FINAL_VALUE=""
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    RESULT=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_gauge_test"}') || true
    VAL=$(echo "$RESULT" | jq -r '.data.result[0].value[1]' 2>/dev/null) || VAL=""
    VAL_INT=$(echo "$VAL" | cut -d. -f1)
    if [ "$VAL_INT" = "99" ]; then
        FOUND=1
        FINAL_VALUE="$VAL"
        break
    fi
    FINAL_VALUE="$VAL"
    sleep 3
done

# Cleanup: clear override so subsequent scenarios see value=42
reset_oid_overrides

# Assert
if [ "$FOUND" -eq 1 ]; then
    record_pass "$SCENARIO_NAME" "value updated to 99 within poll cycle. final_value=${FINAL_VALUE}"
else
    record_fail "$SCENARIO_NAME" "value did not update to 99 within 40s. last_value=${FINAL_VALUE}"
fi
