# Scenario 71: MCV-03 — handled equals published for E2E-SIM (all polled OIDs are mapped)
# Since all E2E-SIM polled OIDs exist in oid_metric_map, every published event
# reaches OtelMetricHandler and increments handled. Delta should be equal.
SCENARIO_NAME="MCV-03: handled delta equals published delta for E2E-SIM"
PUB_METRIC="snmp_event_published_total"
HDL_METRIC="snmp_event_handled_total"
FILTER='device_name="E2E-SIM"'

PUB_BEFORE=$(snapshot_counter "$PUB_METRIC" "$FILTER")
HDL_BEFORE=$(snapshot_counter "$HDL_METRIC" "$FILTER")

# Wait for published to move (confirms poll activity)
poll_until 45 "$POLL_INTERVAL" "$PUB_METRIC" "$FILTER" "$PUB_BEFORE" || true
# Extra wait for OTel export to flush both counters consistently
sleep 20

PUB_AFTER=$(snapshot_counter "$PUB_METRIC" "$FILTER")
HDL_AFTER=$(snapshot_counter "$HDL_METRIC" "$FILTER")
PUB_DELTA=$((PUB_AFTER - PUB_BEFORE))
HDL_DELTA=$((HDL_AFTER - HDL_BEFORE))

EVIDENCE="published_delta=$PUB_DELTA handled_delta=$HDL_DELTA pub_before=$PUB_BEFORE pub_after=$PUB_AFTER hdl_before=$HDL_BEFORE hdl_after=$HDL_AFTER"
assert_delta_eq "$PUB_DELTA" "$HDL_DELTA" "$SCENARIO_NAME" "$EVIDENCE"
