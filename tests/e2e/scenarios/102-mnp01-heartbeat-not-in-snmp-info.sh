# Scenario 102: MNP-01 — heartbeat never appears in snmp_info and never has resolved_name=Unknown
# HeartbeatJob fires a Counter32 trap with community=Simetra. OidMapService.MergeWithHeartbeatSeed
# injects the heartbeat OID with MetricName="Heartbeat" so it is correctly mapped.
# Two negative proofs:
# 1. snmp_info{device_name="Simetra"} count == 0  (Counter32 never reaches RecordInfo path)
# 2. snmp_gauge{device_name="Simetra",resolved_name="Unknown"} count == 0  (heartbeat is correctly seeded, not leaked as Unknown)
SCENARIO_NAME="MNP-01: heartbeat never appears in snmp_info and never has resolved_name=Unknown"

INFO_RESPONSE=$(query_prometheus 'snmp_info{device_name="Simetra"}')
INFO_COUNT=$(echo "$INFO_RESPONSE" | jq -r '.data.result | length')

UNKNOWN_GAUGE_RESPONSE=$(query_prometheus 'snmp_gauge{device_name="Simetra",resolved_name="Unknown"}')
UNKNOWN_GAUGE_COUNT=$(echo "$UNKNOWN_GAUGE_RESPONSE" | jq -r '.data.result | length')

EVIDENCE="info_count=${INFO_COUNT} unknown_gauge_count=${UNKNOWN_GAUGE_COUNT}"

if [ "$INFO_COUNT" -eq 0 ] && [ "$UNKNOWN_GAUGE_COUNT" -eq 0 ]; then
    record_pass "$SCENARIO_NAME" "heartbeat correctly suppressed: snmp_info{device_name=Simetra}=0 snmp_gauge{...,resolved_name=Unknown}=0. $EVIDENCE"
else
    record_fail "$SCENARIO_NAME" "heartbeat leak detected: info_count=${INFO_COUNT} unknown_gauge_count=${UNKNOWN_GAUGE_COUNT}. $EVIDENCE"
fi
