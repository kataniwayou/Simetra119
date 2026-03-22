# Scenario 105: MNP-04 — trap.dropped stays 0 during normal E2E run
# snmp.trap.dropped only increments when TrapChannel's BoundedChannelOptions(FullMode=DropOldest)
# drops an item. Under normal E2E load the channel never fills: dropped delta must be 0.
# Pattern: snapshot dropped before, confirm pipeline activity via trap.received, snapshot after,
# assert delta == 0 only if activity was observed (non-vacuous proof).
SCENARIO_NAME="MNP-04: trap.dropped stays 0 during normal E2E run"
ACTIVITY_METRIC="snmp_trap_received_total"
ACTIVITY_FILTER='device_name="E2E-SIM"'
DROPPED_METRIC="snmp_trap_dropped_total"
DROPPED_FILTER=""

ACTIVITY_BEFORE=$(snapshot_counter "$ACTIVITY_METRIC" "$ACTIVITY_FILTER")
DROPPED_BEFORE=$(snapshot_counter "$DROPPED_METRIC" "$DROPPED_FILTER")

poll_until 45 "$POLL_INTERVAL" "$ACTIVITY_METRIC" "$ACTIVITY_FILTER" "$ACTIVITY_BEFORE" || true
sleep 20

ACTIVITY_AFTER=$(snapshot_counter "$ACTIVITY_METRIC" "$ACTIVITY_FILTER")
DROPPED_AFTER=$(snapshot_counter "$DROPPED_METRIC" "$DROPPED_FILTER")

ACTIVITY_DELTA=$((ACTIVITY_AFTER - ACTIVITY_BEFORE))
DROPPED_DELTA=$((DROPPED_AFTER - DROPPED_BEFORE))

EVIDENCE="activity_delta=${ACTIVITY_DELTA} dropped_delta=${DROPPED_DELTA} dropped_before=${DROPPED_BEFORE} dropped_after=${DROPPED_AFTER}"

if [ "$ACTIVITY_DELTA" -gt 0 ]; then
    assert_delta_eq "$DROPPED_DELTA" 0 "$SCENARIO_NAME" "$EVIDENCE"
else
    record_fail "$SCENARIO_NAME" "Pipeline inactive -- cannot verify dropped=0. $EVIDENCE"
fi
