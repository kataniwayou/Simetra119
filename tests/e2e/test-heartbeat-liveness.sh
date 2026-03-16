#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
# test-heartbeat-liveness.sh -- Build, deploy, and validate heartbeat liveness
#
# Validates that /healthz/live reports pipeline-heartbeat as NOT stale and
# all job entries as NOT stale after a fresh build and deploy.
#
# Usage:
#   ./test-heartbeat-liveness.sh                     # full build + deploy + test
#   SKIP_BUILD=true ./test-heartbeat-liveness.sh     # deploy + test only
#   SKIP_BUILD=true SKIP_DEPLOY=true ./test-heartbeat-liveness.sh  # test only
#   CHECK_PROMETHEUS=true ./test-heartbeat-liveness.sh  # include Prometheus check
# ============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$SCRIPT_DIR/../.."

# shellcheck source=lib/common.sh
source "$SCRIPT_DIR/lib/common.sh"
# shellcheck source=lib/kubectl.sh
source "$SCRIPT_DIR/lib/kubectl.sh"

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

IMAGE_NAME="snmp-collector:local"
DEPLOYMENT="snmp-collector"
NAMESPACE="simetra"
HEALTH_PORT=18080
LIVENESS_PATH="/healthz/live"
WAIT_READY_TIMEOUT=120
LIVENESS_SETTLE_WAIT=45

SKIP_BUILD="${SKIP_BUILD:-false}"
SKIP_DEPLOY="${SKIP_DEPLOY:-false}"
CHECK_PROMETHEUS="${CHECK_PROMETHEUS:-false}"

# ---------------------------------------------------------------------------
# Cleanup
# ---------------------------------------------------------------------------

cleanup() {
    stop_port_forwards
}
trap cleanup EXIT

# ---------------------------------------------------------------------------
# Banner
# ---------------------------------------------------------------------------

echo ""
echo "============================================================================"
echo "  Heartbeat Liveness E2E Test"
echo "  $(date '+%Y-%m-%d %H:%M:%S')"
echo "============================================================================"
echo ""

# ---------------------------------------------------------------------------
# Step 1 - Build
# ---------------------------------------------------------------------------

if [ "$SKIP_BUILD" = "true" ]; then
    log_info "Skipping build (SKIP_BUILD=true)"
else
    log_info "Building Docker image: $IMAGE_NAME"
    if (cd "$REPO_ROOT" && docker build -t "$IMAGE_NAME" -f Dockerfile .); then
        log_info "Build complete"
    else
        log_error "Docker build failed"
        exit 1
    fi
fi

# ---------------------------------------------------------------------------
# Step 2 - Deploy
# ---------------------------------------------------------------------------

if [ "$SKIP_DEPLOY" = "true" ]; then
    log_info "Skipping deploy (SKIP_DEPLOY=true)"
else
    log_info "Ensuring deployment uses image $IMAGE_NAME"
    kubectl set image "deployment/$DEPLOYMENT" "snmp-collector=$IMAGE_NAME" -n "$NAMESPACE"

    log_info "Rolling restart deployment/$DEPLOYMENT in namespace $NAMESPACE"
    if ! kubectl rollout restart "deployment/$DEPLOYMENT" -n "$NAMESPACE"; then
        log_error "Rollout restart failed"
        exit 1
    fi

    log_info "Waiting for rollout to complete (timeout: ${WAIT_READY_TIMEOUT}s)..."
    if ! kubectl rollout status "deployment/$DEPLOYMENT" -n "$NAMESPACE" --timeout="${WAIT_READY_TIMEOUT}s"; then
        log_error "Rollout did not complete within ${WAIT_READY_TIMEOUT}s"
        exit 1
    fi
fi

# ---------------------------------------------------------------------------
# Step 3 - Wait for pods ready
# ---------------------------------------------------------------------------

log_info "Checking pod readiness..."
ELAPSED=0
while ! check_pods_ready; do
    ELAPSED=$((ELAPSED + 5))
    if [ "$ELAPSED" -ge "$WAIT_READY_TIMEOUT" ]; then
        log_error "Pods not ready after ${WAIT_READY_TIMEOUT}s"
        exit 1
    fi
    sleep 5
done

# ---------------------------------------------------------------------------
# Step 4 - Wait for heartbeat liveness to settle
# ---------------------------------------------------------------------------

log_info "Waiting ${LIVENESS_SETTLE_WAIT}s for heartbeat stamps to populate..."
log_info "(HeartbeatJob fires on startup, pipeline-arrival needs one full loop)"
sleep "$LIVENESS_SETTLE_WAIT"

# ---------------------------------------------------------------------------
# Step 5 - Port-forward to a single pod
# ---------------------------------------------------------------------------

POD=$(kubectl get pods -n "$NAMESPACE" -l app=snmp-collector -o jsonpath='{.items[0].metadata.name}')
log_info "Port-forwarding pod/$POD ${HEALTH_PORT}:8080"
kubectl port-forward "pod/$POD" "${HEALTH_PORT}:8080" -n "$NAMESPACE" &>/dev/null &
PF_PIDS+=($!)
sleep 2

# ---------------------------------------------------------------------------
# Step 6 - Query /healthz/live
# ---------------------------------------------------------------------------

log_info "Querying http://localhost:${HEALTH_PORT}${LIVENESS_PATH}"

HTTP_CODE=""
RESPONSE=""
HTTP_CODE=$(curl -s -o /tmp/liveness-response.json -w '%{http_code}' "http://localhost:${HEALTH_PORT}${LIVENESS_PATH}" 2>/dev/null) || true
RESPONSE=$(cat /tmp/liveness-response.json 2>/dev/null) || true

if [ -z "$RESPONSE" ]; then
    log_error "No response from liveness endpoint (HTTP $HTTP_CODE)"
    record_fail "liveness-reachable" "No response from endpoint"
    record_fail "overall-liveness-healthy" "Endpoint unreachable"
    record_fail "pipeline-heartbeat-exists" "Endpoint unreachable"
    record_fail "pipeline-heartbeat-not-stale" "Endpoint unreachable"
    record_fail "no-stale-jobs" "Endpoint unreachable"
    print_summary
    exit 1
fi

log_info "HTTP status: $HTTP_CODE"
log_info "Response:"
echo "$RESPONSE" | jq . 2>/dev/null || echo "$RESPONSE"

# ---------------------------------------------------------------------------
# Step 7 - Parse and validate
# ---------------------------------------------------------------------------

# Extract liveness check data
LIVENESS_DATA=$(echo "$RESPONSE" | jq -r '.checks[] | select(.name == "liveness") | .data' 2>/dev/null) || true

if [ -z "$LIVENESS_DATA" ] || [ "$LIVENESS_DATA" = "null" ]; then
    log_error "Could not extract liveness data from response"
    record_fail "overall-liveness-healthy" "No liveness data in response"
    record_fail "pipeline-heartbeat-exists" "No liveness data"
    record_fail "pipeline-heartbeat-not-stale" "No liveness data"
    record_fail "no-stale-jobs" "No liveness data"
    print_summary
    exit 1
fi

echo ""
log_info "=== Test Results ==="
echo ""

# Test A - Overall status is Healthy
OVERALL=$(echo "$RESPONSE" | jq -r '.status')
if [ "$OVERALL" = "Healthy" ]; then
    record_pass "overall-liveness-healthy" "status=$OVERALL, http=$HTTP_CODE"
else
    record_fail "overall-liveness-healthy" "status=$OVERALL, http=$HTTP_CODE"
fi

# Test B - pipeline-heartbeat key exists
HAS_PIPELINE=$(echo "$LIVENESS_DATA" | jq 'has("pipeline-heartbeat")')
if [ "$HAS_PIPELINE" = "true" ]; then
    record_pass "pipeline-heartbeat-exists" "key present in liveness data"
else
    record_fail "pipeline-heartbeat-exists" "key missing from liveness data"
fi

# Test C - pipeline-heartbeat is not stale
PIPELINE_STALE=$(echo "$LIVENESS_DATA" | jq -r 'if .["pipeline-heartbeat"].stale == null then "missing" else (.["pipeline-heartbeat"].stale | tostring) end')
PIPELINE_AGE=$(echo "$LIVENESS_DATA" | jq -r '.["pipeline-heartbeat"].ageSeconds // "null"')
PIPELINE_THRESHOLD=$(echo "$LIVENESS_DATA" | jq -r '.["pipeline-heartbeat"].thresholdSeconds // "null"')

if [ "$PIPELINE_STALE" = "false" ]; then
    record_pass "pipeline-heartbeat-not-stale" "stale=false, age=${PIPELINE_AGE}s, threshold=${PIPELINE_THRESHOLD}s"
else
    record_fail "pipeline-heartbeat-not-stale" "stale=$PIPELINE_STALE, age=${PIPELINE_AGE}s, threshold=${PIPELINE_THRESHOLD}s"
fi

# Test D - No stale jobs
STALE_COUNT=$(echo "$LIVENESS_DATA" | jq '[to_entries[] | select(.value.stale == true)] | length')
if [ "$STALE_COUNT" -eq 0 ]; then
    TOTAL_JOBS=$(echo "$LIVENESS_DATA" | jq 'keys | length')
    record_pass "no-stale-jobs" "0 stale out of $TOTAL_JOBS entries"
else
    STALE_NAMES=$(echo "$LIVENESS_DATA" | jq -r '[to_entries[] | select(.value.stale == true) | .key] | join(", ")')
    record_fail "no-stale-jobs" "$STALE_COUNT stale: $STALE_NAMES"
fi

# Test E - pipeline-heartbeat age is reasonable (< threshold)
if [ "$PIPELINE_AGE" != "null" ] && [ "$PIPELINE_THRESHOLD" != "null" ]; then
    AGE_INT=${PIPELINE_AGE%.*}
    THRESH_INT=${PIPELINE_THRESHOLD%.*}
    if [ "$AGE_INT" -lt "$THRESH_INT" ]; then
        record_pass "pipeline-heartbeat-age-reasonable" "age=${PIPELINE_AGE}s < threshold=${PIPELINE_THRESHOLD}s"
    else
        record_fail "pipeline-heartbeat-age-reasonable" "age=${PIPELINE_AGE}s >= threshold=${PIPELINE_THRESHOLD}s"
    fi
else
    record_fail "pipeline-heartbeat-age-reasonable" "age or threshold is null"
fi

# ---------------------------------------------------------------------------
# Step 8 - Optional Prometheus check
# ---------------------------------------------------------------------------

if [ "$CHECK_PROMETHEUS" = "true" ]; then
    # shellcheck source=lib/prometheus.sh
    source "$SCRIPT_DIR/lib/prometheus.sh"

    log_info "Starting Prometheus port-forward..."
    start_port_forward "prometheus-server" 9090 80

    log_info "Checking snmp_event_handled_total for heartbeat device..."
    HANDLED=$(query_prometheus 'snmp_event_handled_total{device_name="Simetra"}')
    HANDLED_COUNT=$(echo "$HANDLED" | jq -r '.data.result | length')

    if [ "$HANDLED_COUNT" -gt 0 ]; then
        HANDLED_VALUE=$(echo "$HANDLED" | jq -r '.data.result[0].value[1]')
        record_pass "prometheus-heartbeat-handled" "snmp_event_handled_total{device_name=Simetra} has $HANDLED_COUNT series, value=$HANDLED_VALUE"
    else
        record_fail "prometheus-heartbeat-handled" "No snmp_event_handled_total series for device_name=Simetra"
    fi
fi

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

echo ""
print_summary

if [ "$FAIL_COUNT" -eq 0 ]; then
    exit 0
else
    exit 1
fi
