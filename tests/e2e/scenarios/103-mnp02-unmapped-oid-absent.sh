# Scenario 103: MNP-02 — unmapped OIDs produce no snmp_gauge or snmp_info
# OIDs .999.2.1.0 and .999.2.2.0 are served by the E2E simulator but are NOT in oidmaps.json
# and NOT in the poll config (devices.json). They are never polled and never send traps.
# Both queries must return 0 results — confirmed absent from the Prometheus series database.
SCENARIO_NAME="MNP-02: unmapped OIDs produce no snmp_gauge or snmp_info"

GAUGE_RESPONSE=$(query_prometheus 'snmp_gauge{oid="1.3.6.1.4.1.47477.999.2.1.0"}')
GAUGE_COUNT=$(echo "$GAUGE_RESPONSE" | jq -r '.data.result | length')

INFO_RESPONSE=$(query_prometheus 'snmp_info{oid="1.3.6.1.4.1.47477.999.2.2.0"}')
INFO_COUNT=$(echo "$INFO_RESPONSE" | jq -r '.data.result | length')

EVIDENCE="gauge_count=${GAUGE_COUNT} info_count=${INFO_COUNT}"

if [ "$GAUGE_COUNT" -eq 0 ] && [ "$INFO_COUNT" -eq 0 ]; then
    record_pass "$SCENARIO_NAME" "unmapped OIDs absent: snmp_gauge{oid=.999.2.1.0}=0 snmp_info{oid=.999.2.2.0}=0. $EVIDENCE"
else
    record_fail "$SCENARIO_NAME" "unmapped OID found in Prometheus: gauge_count=${GAUGE_COUNT} info_count=${INFO_COUNT}. $EVIDENCE"
fi
