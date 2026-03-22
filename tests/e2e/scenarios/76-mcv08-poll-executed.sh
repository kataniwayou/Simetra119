# Scenario 76: MCV-08 — poll.executed increments each poll cycle
# MetricPollJob.Execute fires the finally block unconditionally per poll group per replica.
# E2E-SIM has 4 poll groups (one at 1s interval) — delta will be >> 0 within any 30s window.
SCENARIO_NAME="MCV-08: poll.executed increments each poll cycle"
METRIC="snmp_poll_executed_total"
FILTER='device_name="E2E-SIM"'

BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
poll_until 45 "$POLL_INTERVAL" "$METRIC" "$FILTER" "$BEFORE" || true
AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE="before=$BEFORE after=$AFTER delta=$DELTA (expect > 0: fires in finally block per poll group per replica)"
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "$EVIDENCE"
