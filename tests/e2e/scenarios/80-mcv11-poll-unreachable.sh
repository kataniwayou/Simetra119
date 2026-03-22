# Scenario 80: MCV-11 -- poll.unreachable increments after 3 consecutive failures
# Scales down the E2E simulator to 0 replicas, causing all polls to E2E-SIM to fail
# with connection refused (immediate, no timeout wait). After 3 consecutive failures,
# DeviceUnreachabilityTracker fires poll.unreachable for E2E-SIM.
#
# Advantages over FAKE-UNREACHABLE approach:
# - No ConfigMap mutation (no DynamicPollScheduler reconciliation jitter)
# - No pre-recovery idempotency step (E2E-SIM is always in the tracker)
# - Connection refused is instant (vs 8s timeout per attempt)
# - Tests a REAL device going unreachable
#
# Leaves simulator scaled down for scenario 81 (recovery test).
SCENARIO_NAME="MCV-11: poll.unreachable increments after 3 consecutive failures"
METRIC="snmp_poll_unreachable_total"
FILTER='device_name="E2E-SIM"'

BEFORE=$(snapshot_counter "$METRIC" "$FILTER")

# Scale down E2E simulator to 0 replicas
log_info "Scaling down e2e-simulator to 0 replicas..."
kubectl scale deployment e2e-simulator -n simetra --replicas=0
kubectl rollout status deployment/e2e-simulator -n simetra --timeout=30s 2>/dev/null || true

# Wait for 3 consecutive poll failures + OTel export
# E2E-SIM poll group 2 has 1s interval. Connection refused is instant.
# 3 failures × 1s interval = ~3s per replica, plus OTel 15s + Prometheus 5s = ~23s minimum
# Use 60s timeout (generous for 3-replica cluster)
log_info "Waiting for E2E-SIM to become unreachable (up to 60s)..."
poll_until 60 3 "$METRIC" "$FILTER" "$BEFORE" || true

AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE=$(get_evidence "$METRIC" "$FILTER")
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "before=$BEFORE after=$AFTER delta=$DELTA | $EVIDENCE"
