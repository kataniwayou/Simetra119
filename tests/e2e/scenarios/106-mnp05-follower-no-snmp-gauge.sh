# Scenario 106: MNP-05 — follower pod exports no snmp_gauge or snmp_info
# MetricRoleGatedExporter gates the SnmpCollector.Leader meter behind IsLeader.
# Only the leader pod sends snmp_gauge/snmp_info to OTLP. Follower pods suppress them.
# k8s.pod.name resource attribute is converted to k8s_pod_name Prometheus label via
# resource_to_telemetry_conversion.enabled: true in otel-collector config.
# Proof: identify a follower pod via Prometheus label query, assert 0 snmp_gauge/snmp_info.
SCENARIO_NAME="MNP-05: follower pod exports no snmp_gauge or snmp_info"

# Step 1: Preflight — verify k8s_pod_name label exists in Prometheus
VERIFY_LABEL=$(query_prometheus 'snmp_gauge' | jq -r '.data.result[0].metric.k8s_pod_name // ""')
if [ -z "$VERIFY_LABEL" ]; then
    record_fail "$SCENARIO_NAME" "k8s_pod_name label not present in snmp_gauge -- resource_to_telemetry_conversion may not be working"
    return 0 2>/dev/null || exit 0
fi

# Step 2: Get all snmp-collector pod names
POD_NAMES=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{range .items[*]}{.metadata.name}{"\n"}{end}')

if [ -z "$POD_NAMES" ]; then
    record_fail "$SCENARIO_NAME" "No snmp-collector pods found in namespace simetra"
    return 0 2>/dev/null || exit 0
fi

# Step 3: Identify a follower pod (snmp_gauge count == 0 for that pod name)
FOLLOWER_POD=""
for POD in $POD_NAMES; do
    [ -z "$POD" ] && continue
    COUNT=$(query_prometheus "snmp_gauge{k8s_pod_name=\"${POD}\"}" | jq -r '.data.result | length')
    if [ "$COUNT" -eq 0 ]; then
        FOLLOWER_POD="$POD"
        break
    fi
done

if [ -z "$FOLLOWER_POD" ]; then
    record_fail "$SCENARIO_NAME" "Could not identify a follower pod -- all pods have snmp_gauge series (unexpected)"
    return 0 2>/dev/null || exit 0
fi

# Step 4: Also verify the follower has no snmp_info
FOLLOWER_INFO_COUNT=$(query_prometheus "snmp_info{k8s_pod_name=\"${FOLLOWER_POD}\"}" | jq -r '.data.result | length')

EVIDENCE="follower_pod=${FOLLOWER_POD} snmp_gauge_count=0 snmp_info_count=${FOLLOWER_INFO_COUNT}"

if [ "$FOLLOWER_INFO_COUNT" -eq 0 ]; then
    record_pass "$SCENARIO_NAME" "follower pod ${FOLLOWER_POD} exports no snmp_gauge or snmp_info (MetricRoleGatedExporter confirmed). $EVIDENCE"
else
    record_fail "$SCENARIO_NAME" "follower pod ${FOLLOWER_POD} has snmp_info series (snmp_info_count=${FOLLOWER_INFO_COUNT}) -- gating not working. $EVIDENCE"
fi
