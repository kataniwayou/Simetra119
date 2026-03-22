# Scenario 73: MCV-05 — rejected counter behavior during normal operation
# IMPORTANT FINDING: snmp.event.rejected fires ONLY on ValidationBehavior failures:
#   1. OID string fails regex ^\d+(\.\d+){1,}$
#   2. DeviceName is null
# Unmapped OIDs (not in oidmaps.json) do NOT increment rejected -- they resolve
# to MetricName="Unknown" and are handled normally by OtelMetricHandler.
# The 2 unmapped OIDs (.999.2.x) are served by the simulator but NOT in the poll
# config, so they never enter the pipeline in normal operation.
#
# This scenario verifies: rejected stays 0 during normal operation while the
# pipeline is actively publishing and handling events.
SCENARIO_NAME="MCV-05: rejected stays 0 during normal operation (no validation failures)"
PUB_METRIC="snmp_event_published_total"
REJ_METRIC="snmp_event_rejected_total"
FILTER='device_name="E2E-SIM"'

PUB_BEFORE=$(snapshot_counter "$PUB_METRIC" "$FILTER")
REJ_BEFORE=$(snapshot_counter "$REJ_METRIC" "$FILTER")

# Wait for pipeline activity (published must move to prove pipeline is active)
poll_until 45 "$POLL_INTERVAL" "$PUB_METRIC" "$FILTER" "$PUB_BEFORE" || true
sleep 20

PUB_AFTER=$(snapshot_counter "$PUB_METRIC" "$FILTER")
REJ_AFTER=$(snapshot_counter "$REJ_METRIC" "$FILTER")

PUB_DELTA=$((PUB_AFTER - PUB_BEFORE))
REJ_DELTA=$((REJ_AFTER - REJ_BEFORE))

EVIDENCE="pub_delta=$PUB_DELTA rej_delta=$REJ_DELTA pub_before=$PUB_BEFORE pub_after=$PUB_AFTER rej_before=$REJ_BEFORE rej_after=$REJ_AFTER"

# Pipeline must be active (published moved) AND rejected must stay 0
if [ "$PUB_DELTA" -gt 0 ]; then
    assert_delta_eq "$REJ_DELTA" 0 "$SCENARIO_NAME" "$EVIDENCE"
else
    record_fail "$SCENARIO_NAME" "Pipeline inactive -- cannot verify rejected. $EVIDENCE"
fi
