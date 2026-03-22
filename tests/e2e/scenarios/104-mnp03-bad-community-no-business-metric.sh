# Scenario 104: MNP-03 — bad-community traps produce no snmp_gauge or snmp_info
# Extends MCV-09b (scenario 78): SnmpTrapListenerService drops bad-community traps before writing
# to the channel, so ChannelConsumerService and the MediatR pipeline never see them.
# Proof: wait for auth_failed to fire (confirming bad trap arrived), then assert that
# snmp_gauge{device_name="unknown"} and snmp_info{device_name="unknown"} both have 0 series.
SCENARIO_NAME="MNP-03: bad-community traps produce no snmp_gauge or snmp_info"
AUTH_METRIC="snmp_trap_auth_failed_total"
AUTH_FILTER=""

AUTH_BEFORE=$(snapshot_counter "$AUTH_METRIC" "$AUTH_FILTER")

# Wait for at least one bad-community trap (interval 45s + OTel export lag)
poll_until 75 "$POLL_INTERVAL" "$AUTH_METRIC" "$AUTH_FILTER" "$AUTH_BEFORE" || true
sleep 15  # OTel export flush

AUTH_AFTER=$(snapshot_counter "$AUTH_METRIC" "$AUTH_FILTER")
AUTH_DELTA=$((AUTH_AFTER - AUTH_BEFORE))

UNKNOWN_GAUGE_COUNT=$(query_prometheus 'snmp_gauge{device_name="unknown"}' | jq -r '.data.result | length')
UNKNOWN_INFO_COUNT=$(query_prometheus 'snmp_info{device_name="unknown"}' | jq -r '.data.result | length')

EVIDENCE="auth_delta=${AUTH_DELTA} unknown_gauge_count=${UNKNOWN_GAUGE_COUNT} unknown_info_count=${UNKNOWN_INFO_COUNT}"

if [ "$AUTH_DELTA" -gt 0 ]; then
    if [ "$UNKNOWN_GAUGE_COUNT" -eq 0 ] && [ "$UNKNOWN_INFO_COUNT" -eq 0 ]; then
        record_pass "$SCENARIO_NAME" "auth_failed fired (delta=${AUTH_DELTA}); no snmp_gauge or snmp_info with device_name=unknown. $EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "bad-community trap leaked into business metrics: unknown_gauge=${UNKNOWN_GAUGE_COUNT} unknown_info=${UNKNOWN_INFO_COUNT}. $EVIDENCE"
    fi
else
    record_fail "$SCENARIO_NAME" "No bad-community trap arrived in window -- cannot verify negative assertion. $EVIDENCE"
fi
