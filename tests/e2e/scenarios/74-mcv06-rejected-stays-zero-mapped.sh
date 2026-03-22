# Scenario 74: MCV-06 — rejected does NOT increment for mapped OIDs
# All E2E-SIM polled OIDs are mapped in oid_metric_map. Mapped OIDs pass
# ValidationBehavior (valid format, non-null DeviceName) and reach OtelMetricHandler.
# They should NEVER cause a rejected increment.
SCENARIO_NAME="MCV-06: rejected stays 0 while mapped OIDs are handled"
HDL_METRIC="snmp_event_handled_total"
REJ_METRIC="snmp_event_rejected_total"
FILTER='device_name="E2E-SIM"'

HDL_BEFORE=$(snapshot_counter "$HDL_METRIC" "$FILTER")
REJ_BEFORE=$(snapshot_counter "$REJ_METRIC" "$FILTER")

# Wait for handled to move (proves mapped OIDs are flowing through)
poll_until 45 "$POLL_INTERVAL" "$HDL_METRIC" "$FILTER" "$HDL_BEFORE" || true
sleep 20

HDL_AFTER=$(snapshot_counter "$HDL_METRIC" "$FILTER")
REJ_AFTER=$(snapshot_counter "$REJ_METRIC" "$FILTER")

HDL_DELTA=$((HDL_AFTER - HDL_BEFORE))
REJ_DELTA=$((REJ_AFTER - REJ_BEFORE))

EVIDENCE="hdl_delta=$HDL_DELTA rej_delta=$REJ_DELTA hdl_before=$HDL_BEFORE hdl_after=$HDL_AFTER rej_before=$REJ_BEFORE rej_after=$REJ_AFTER"

# Mapped OIDs must be handled (hdl_delta > 0) AND rejected must stay 0
if [ "$HDL_DELTA" -gt 0 ]; then
    assert_delta_eq "$REJ_DELTA" 0 "$SCENARIO_NAME" "$EVIDENCE"
else
    record_fail "$SCENARIO_NAME" "No handled events -- cannot verify rejection behavior. $EVIDENCE"
fi
