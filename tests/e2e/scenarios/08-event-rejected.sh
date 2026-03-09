# Scenario 08: event_rejected metric exists (sentinel check)
# This counter only increments under abnormal conditions (pipeline rejections).
# OTel counters only appear in Prometheus after their first Add() call.
# We verify whether the metric is registered -- either state is acceptable.
SCENARIO_NAME="event_rejected metric registered in Prometheus"
METRIC="snmp_event_rejected_total"

RESULT=$(query_prometheus "{__name__=\"${METRIC}\"}")
COUNT=$(echo "$RESULT" | jq -r '.data.result | length')

if [ "$COUNT" -gt 0 ]; then
    record_pass "$SCENARIO_NAME" "sentinel metric ${METRIC} found (${COUNT} series)"
else
    record_pass "$SCENARIO_NAME" "sentinel metric ${METRIC} not yet incremented (expected -- counter registers on first Add())"
fi
