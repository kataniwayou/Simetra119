# Scenario 05: trap_auth_failed increments from bad community traps
# E2E-SIM sends bad-community traps every 45s -- use 60s timeout
# The trap_auth_failed counter uses device_name="unknown" because "BadCommunity"
# doesn't match the Simetra.* convention, so no device name can be extracted.
SCENARIO_NAME="trap_auth_failed increments from bad community traps"
METRIC="snmp_trap_auth_failed_total"
FILTER=""

BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
poll_until 60 "$POLL_INTERVAL" "$METRIC" "$FILTER" "$BEFORE" || true
AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE=$(get_evidence "$METRIC" "$FILTER")
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "before=$BEFORE after=$AFTER delta=$DELTA | $EVIDENCE"
