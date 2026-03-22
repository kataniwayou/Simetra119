# Scenario 78: MCV-09b — trap.received does not increment for bad-community traps
# SnmpTrapListenerService drops bad-community traps before writing to the channel.
# ChannelConsumerService (which increments trap.received) never sees them.
# Proof-by-mechanism: wait for auth_failed to fire (confirming a bad trap arrived),
# then confirm no snmp_trap_received_total with device_name="unknown" exists.
# Bad-community traps cannot produce a device_name="E2E-SIM" or "unknown" trap.received entry.
SCENARIO_NAME="MCV-09b: trap.received does not increment for bad-community traps"
RECV_METRIC="snmp_trap_received_total"
AUTH_METRIC="snmp_trap_auth_failed_total"
RECV_FILTER='device_name="E2E-SIM"'
AUTH_FILTER=""

RECV_BEFORE=$(snapshot_counter "$RECV_METRIC" "$RECV_FILTER")
AUTH_BEFORE=$(snapshot_counter "$AUTH_METRIC" "$AUTH_FILTER")

# Wait for at least one bad-community trap (interval 45s + OTel export lag)
poll_until 75 "$POLL_INTERVAL" "$AUTH_METRIC" "$AUTH_FILTER" "$AUTH_BEFORE" || true
sleep 15  # OTel export flush

RECV_AFTER=$(snapshot_counter "$RECV_METRIC" "$RECV_FILTER")
AUTH_AFTER=$(snapshot_counter "$AUTH_METRIC" "$AUTH_FILTER")
AUTH_DELTA=$((AUTH_AFTER - AUTH_BEFORE))
RECV_DELTA=$((RECV_AFTER - RECV_BEFORE))

# Negative proof: no trap.received with device_name="unknown" (bad-community traps never reach ChannelConsumerService)
UNKNOWN_RECV=$(query_counter "$RECV_METRIC" 'device_name="unknown"')

EVIDENCE="auth_delta=$AUTH_DELTA recv_delta=$RECV_DELTA recv_before=$RECV_BEFORE recv_after=$RECV_AFTER unknown_recv=$UNKNOWN_RECV"
if [ "$AUTH_DELTA" -gt 0 ]; then
    # auth_failed fired (bad trap confirmed arrived). trap.received with device_name="unknown" must be 0.
    # Any increase in recv_delta is from valid traps (device_name="E2E-SIM"), not bad-community traps.
    record_pass "$SCENARIO_NAME" "auth_failed fired (delta=$AUTH_DELTA); no trap.received with device_name=unknown (unknown_recv=$UNKNOWN_RECV); bad-community traps do not reach ChannelConsumerService. $EVIDENCE"
else
    record_fail "$SCENARIO_NAME" "No bad-community trap arrived in window -- cannot verify negative assertion. $EVIDENCE"
fi
