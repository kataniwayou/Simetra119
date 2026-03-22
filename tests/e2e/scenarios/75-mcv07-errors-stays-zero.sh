# Scenario 75: MCV-07 — errors stays 0 during normal E2E run
# snmp.event.errors only increments when ExceptionBehavior catches an unhandled
# exception from downstream pipeline behaviors. In normal operation: 0 errors.
SCENARIO_NAME="MCV-07: errors stays 0 during normal E2E run"
PUB_METRIC="snmp_event_published_total"
ERR_METRIC="snmp_event_errors_total"
FILTER='device_name="E2E-SIM"'

PUB_BEFORE=$(snapshot_counter "$PUB_METRIC" "$FILTER")
ERR_BEFORE=$(snapshot_counter "$ERR_METRIC" "$FILTER")

# Wait for pipeline activity
poll_until 45 "$POLL_INTERVAL" "$PUB_METRIC" "$FILTER" "$PUB_BEFORE" || true
sleep 20

PUB_AFTER=$(snapshot_counter "$PUB_METRIC" "$FILTER")
ERR_AFTER=$(snapshot_counter "$ERR_METRIC" "$FILTER")

PUB_DELTA=$((PUB_AFTER - PUB_BEFORE))
ERR_DELTA=$((ERR_AFTER - ERR_BEFORE))

EVIDENCE="pub_delta=$PUB_DELTA err_delta=$ERR_DELTA pub_before=$PUB_BEFORE pub_after=$PUB_AFTER err_before=$ERR_BEFORE err_after=$ERR_AFTER"

# Pipeline must be active AND errors must stay 0
if [ "$PUB_DELTA" -gt 0 ]; then
    assert_delta_eq "$ERR_DELTA" 0 "$SCENARIO_NAME" "$EVIDENCE"
else
    record_fail "$SCENARIO_NAME" "Pipeline inactive -- cannot verify errors. $EVIDENCE"
fi
