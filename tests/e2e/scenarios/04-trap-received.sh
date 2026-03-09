# Scenario 04: trap_received increments from E2E-SIM traps
# E2E-SIM sends valid traps every 30s, use longer timeout
SCENARIO_NAME="trap_received increments from E2E-SIM traps"
METRIC="snmp_trap_received_total"
FILTER='device_name="E2E-SIM"'

BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
poll_until 45 "$POLL_INTERVAL" "$METRIC" "$FILTER" "$BEFORE" || true
AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE=$(get_evidence "$METRIC" "$FILTER")
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "before=$BEFORE after=$AFTER delta=$DELTA | $EVIDENCE"
