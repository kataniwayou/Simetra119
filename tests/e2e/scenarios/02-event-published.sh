# Scenario 02: event_published increments from poll activity
# Every polled OID enters the pipeline as a published event
SCENARIO_NAME="event_published increments from poll activity"
METRIC="snmp_event_published_total"
FILTER='device_name="OBP-01"'

BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
poll_until "$POLL_TIMEOUT" "$POLL_INTERVAL" "$METRIC" "$FILTER" "$BEFORE" || true
AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE=$(get_evidence "$METRIC" "$FILTER")
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "before=$BEFORE after=$AFTER delta=$DELTA | $EVIDENCE"
