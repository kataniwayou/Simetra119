# Scenario 70: MCV-02 — trap published increments per trap varbind
# E2E-SIM sends 1 valid trap every 30s with 1 varbind (e2e_gauge_test, mapped)
# All 3 replicas receive the broadcast trap
SCENARIO_NAME="MCV-02: trap causes published increment (trap path)"
PUB_METRIC="snmp_event_published_total"
TRAP_METRIC="snmp_trap_received_total"
FILTER='device_name="E2E-SIM"'

PUB_BEFORE=$(snapshot_counter "$PUB_METRIC" "$FILTER")
TRAP_BEFORE=$(snapshot_counter "$TRAP_METRIC" "$FILTER")

# Wait for at least one trap to arrive (30s interval + OTel export lag)
poll_until 60 "$POLL_INTERVAL" "$TRAP_METRIC" "$FILTER" "$TRAP_BEFORE" || true
# Extra wait for OTel export to flush both counters
sleep 20

PUB_AFTER=$(snapshot_counter "$PUB_METRIC" "$FILTER")
TRAP_AFTER=$(snapshot_counter "$TRAP_METRIC" "$FILTER")
PUB_DELTA=$((PUB_AFTER - PUB_BEFORE))
TRAP_DELTA=$((TRAP_AFTER - TRAP_BEFORE))

EVIDENCE="pub_before=$PUB_BEFORE pub_after=$PUB_AFTER pub_delta=$PUB_DELTA trap_delta=$TRAP_DELTA"
# Published includes BOTH poll and trap events. If traps arrived (trap_delta > 0),
# published must have increased by at least that much (plus poll activity).
if [ "$TRAP_DELTA" -gt 0 ]; then
    assert_delta_ge "$PUB_DELTA" "$TRAP_DELTA" "$SCENARIO_NAME" "$EVIDENCE"
else
    # No trap arrived in window -- cannot verify, record as fail with evidence
    record_fail "$SCENARIO_NAME" "No trap arrived during window. $EVIDENCE"
fi
