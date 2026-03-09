# Scenario 01: poll_executed increments for OBP-01
# OBP-01 polls every 10s, so delta should be 1+ within 30s
SCENARIO_NAME="poll_executed increments for OBP-01"
METRIC="snmp_poll_executed_total"
FILTER='device_name="OBP-01"'

BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
poll_until "$POLL_TIMEOUT" "$POLL_INTERVAL" "$METRIC" "$FILTER" "$BEFORE" || true
AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE=$(get_evidence "$METRIC" "$FILTER")
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "before=$BEFORE after=$AFTER delta=$DELTA | $EVIDENCE"
