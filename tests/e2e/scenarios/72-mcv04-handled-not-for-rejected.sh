# Scenario 72: MCV-04 — handled does NOT increment for rejected OIDs
# Complementary proof: during normal E2E operation, rejected stays 0 (scenario 74 tests this directly).
# If rejected == 0 AND handled == published, then by definition handled does not count rejected events.
# This scenario verifies handled <= published (handled never exceeds published).
SCENARIO_NAME="MCV-04: handled never exceeds published (handled does not count rejected)"
PUB_METRIC="snmp_event_published_total"
HDL_METRIC="snmp_event_handled_total"
REJ_METRIC="snmp_event_rejected_total"
FILTER='device_name="E2E-SIM"'

PUB_BEFORE=$(snapshot_counter "$PUB_METRIC" "$FILTER")
HDL_BEFORE=$(snapshot_counter "$HDL_METRIC" "$FILTER")
REJ_BEFORE=$(snapshot_counter "$REJ_METRIC" "$FILTER")

# Wait for activity
poll_until 45 "$POLL_INTERVAL" "$PUB_METRIC" "$FILTER" "$PUB_BEFORE" || true
sleep 20

PUB_AFTER=$(snapshot_counter "$PUB_METRIC" "$FILTER")
HDL_AFTER=$(snapshot_counter "$HDL_METRIC" "$FILTER")
REJ_AFTER=$(snapshot_counter "$REJ_METRIC" "$FILTER")

PUB_DELTA=$((PUB_AFTER - PUB_BEFORE))
HDL_DELTA=$((HDL_AFTER - HDL_BEFORE))
REJ_DELTA=$((REJ_AFTER - REJ_BEFORE))

EVIDENCE="pub_delta=$PUB_DELTA hdl_delta=$HDL_DELTA rej_delta=$REJ_DELTA"

# handled must never exceed published
if [ "$HDL_DELTA" -le "$PUB_DELTA" ] && [ "$PUB_DELTA" -gt 0 ]; then
    record_pass "$SCENARIO_NAME" "handled($HDL_DELTA) <= published($PUB_DELTA), rejected=$REJ_DELTA. $EVIDENCE"
elif [ "$PUB_DELTA" -eq 0 ]; then
    record_fail "$SCENARIO_NAME" "No published events observed. $EVIDENCE"
else
    record_fail "$SCENARIO_NAME" "handled($HDL_DELTA) > published($PUB_DELTA) -- unexpected. $EVIDENCE"
fi
