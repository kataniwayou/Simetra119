# Scenario 81: MCV-12 -- poll.recovered increments when device becomes reachable
# Scales the E2E simulator back up to 1 replica after scenario 80 left it at 0.
# First successful poll triggers DeviceUnreachabilityTracker recovery → poll.recovered++.
#
# Depends on scenario 80 having scaled down the simulator (E2E-SIM is unreachable).
SCENARIO_NAME="MCV-12: poll.recovered increments when device becomes reachable"
METRIC="snmp_poll_recovered_total"
FILTER='device_name="E2E-SIM"'

BEFORE=$(snapshot_counter "$METRIC" "$FILTER")

# Scale simulator back up
log_info "Scaling up e2e-simulator to 1 replica..."
kubectl scale deployment e2e-simulator -n simetra --replicas=1
kubectl rollout status deployment/e2e-simulator -n simetra --timeout=60s

# Wait for pod ready + first successful poll + OTel export
# Pod startup ~5-10s, first poll within 1s (group 2 interval), OTel 15s + Prometheus 5s
# Use 60s timeout
log_info "Waiting for E2E-SIM to recover (up to 60s)..."
poll_until 60 3 "$METRIC" "$FILTER" "$BEFORE" || true

AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE=$(get_evidence "$METRIC" "$FILTER")
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "before=$BEFORE after=$AFTER delta=$DELTA | $EVIDENCE"

# Re-establish port forward to simulator (it was lost when pod was scaled down)
log_info "Re-establishing e2e-simulator port forward..."
start_port_forward simetra e2e-simulator 8080:8080 2>/dev/null || true
sleep 2
