# Scenario 77: MCV-09 — trap.received increments for valid-community traps
# ChannelConsumerService increments once per varbind dequeued from the channel.
# E2E-SIM sends valid traps every 30s with community="Simetra.E2E-SIM".
# With 3 replicas receiving the broadcast: sum delta >= 3 per trap cycle.
SCENARIO_NAME="MCV-09: trap.received increments for valid-community traps"
METRIC="snmp_trap_received_total"
FILTER='device_name="E2E-SIM"'

BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
poll_until 60 "$POLL_INTERVAL" "$METRIC" "$FILTER" "$BEFORE" || true
AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE="before=$BEFORE after=$AFTER delta=$DELTA (valid traps every 30s, 3 replicas)"
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "$EVIDENCE"
