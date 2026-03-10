# Scenario 28: TenantVector routing — fan-out counter increments, watcher loads, hot-reload works
# Deploys tenantvector ConfigMap with real ClusterIPs, verifies Prometheus routing counter
# increments, confirms watcher load logs, and tests hot-reload with a 4th tenant.

# ---------------------------------------------------------------------------
# Step 0: Derive ClusterIPs and apply tenantvector ConfigMap with real IPs
# ---------------------------------------------------------------------------

log_info "Deriving ClusterIPs for npb-simulator and obp-simulator..."
NPB_IP=$(kubectl get svc npb-simulator -n simetra -o jsonpath='{.spec.clusterIP}' 2>/dev/null) || true
OBP_IP=$(kubectl get svc obp-simulator -n simetra -o jsonpath='{.spec.clusterIP}' 2>/dev/null) || true

if [ -z "$NPB_IP" ] || [ -z "$OBP_IP" ]; then
    log_error "Could not derive ClusterIPs: NPB_IP='${NPB_IP}' OBP_IP='${OBP_IP}'"
    record_fail "TenantVector ConfigMap mounted in pod" "Failed to derive ClusterIPs from kubectl get svc"
    record_fail "TenantVectorWatcher initial load detected" "Prerequisite: ClusterIP derivation failed"
    record_fail "TenantVector routing counter increments" "Prerequisite: ClusterIP derivation failed"
    record_fail "TenantVector hot-reload detects added tenant" "Prerequisite: ClusterIP derivation failed"
    return 0
fi

log_info "NPB ClusterIP: ${NPB_IP}  OBP ClusterIP: ${OBP_IP}"

# Snapshot current tenantvector ConfigMap BEFORE applying (for restore later)
FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"
save_configmap "simetra-tenantvector" "simetra" "$FIXTURES_DIR/.original-tenantvector-configmap.yaml" || true

TENANTVECTOR_YAML="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/deploy/k8s/snmp-collector/simetra-tenantvector.yaml"

log_info "Applying tenantvector ConfigMap with real ClusterIPs..."
sed \
    -e "s/PLACEHOLDER_NPB_IP/${NPB_IP}/g" \
    -e "s/PLACEHOLDER_OBP_IP/${OBP_IP}/g" \
    "$TENANTVECTOR_YAML" \
    | kubectl apply -f - || true

log_info "Restarting snmp-collector deployment..."
kubectl rollout restart deployment/snmp-collector -n simetra || true
kubectl rollout status deployment/snmp-collector -n simetra --timeout=90s || true

log_info "Waiting 30s for OID map load + first poll cycle + routing..."
sleep 30

# ---------------------------------------------------------------------------
# Sub-scenario 28a: Volume mount verification
# ---------------------------------------------------------------------------

SCENARIO_NAME="TenantVector ConfigMap mounted in pod"

POD1=$(kubectl get pods -n simetra -l app=snmp-collector -o jsonpath='{.items[0].metadata.name}' 2>/dev/null) || true

if [ -z "$POD1" ]; then
    record_fail "$SCENARIO_NAME" "No snmp-collector pods found"
else
    DESCRIBE_OUTPUT=$(kubectl describe pod "$POD1" -n simetra 2>/dev/null) || true
    if echo "$DESCRIBE_OUTPUT" | grep "simetra-tenantvector" > /dev/null 2>&1; then
        record_pass "$SCENARIO_NAME" "pod=${POD1} describe output contains simetra-tenantvector mount"
    else
        record_fail "$SCENARIO_NAME" "pod=${POD1} describe output does not contain simetra-tenantvector"
    fi
fi

# ---------------------------------------------------------------------------
# Sub-scenario 28b: Watcher initial load
# ---------------------------------------------------------------------------

SCENARIO_NAME="TenantVectorWatcher initial load detected"

PODS=$(kubectl get pods -n simetra -l app=snmp-collector -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true
FOUND_WATCHER_LOAD=0
WATCHER_EVIDENCE=""

for POD in $PODS; do
    POD_LOGS=$(kubectl logs "$POD" -n simetra --since=300s 2>/dev/null) || true
    # Use grep > /dev/null 2>&1 (not grep -q) to avoid SIGPIPE with pipefail
    if echo "$POD_LOGS" | grep "TenantVectorWatcher initial load complete" > /dev/null 2>&1; then
        FOUND_WATCHER_LOAD=1
        WATCHER_EVIDENCE="pod=${POD} logged 'TenantVectorWatcher initial load complete'"
        break
    fi
    if echo "$POD_LOGS" | grep "Tenant vector reload complete" > /dev/null 2>&1; then
        FOUND_WATCHER_LOAD=1
        WATCHER_EVIDENCE="pod=${POD} logged 'Tenant vector reload complete'"
        break
    fi
done

if [ "$FOUND_WATCHER_LOAD" -eq 1 ]; then
    record_pass "$SCENARIO_NAME" "$WATCHER_EVIDENCE"
else
    record_fail "$SCENARIO_NAME" "No pod logged watcher load completion within 300s window"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 28c: Routing counter > 0
# ---------------------------------------------------------------------------

SCENARIO_NAME="TenantVector routing counter increments"
METRIC="snmp_tenantvector_routed_total"

BEFORE=$(snapshot_counter "$METRIC" "")
log_info "Baseline ${METRIC}: ${BEFORE}"

log_info "Polling ${METRIC} for 90s (5s interval)..."
poll_until 90 5 "$METRIC" "" "$BEFORE" || true

AFTER=$(query_counter "$METRIC" "")
DELTA=$((AFTER - BEFORE))
log_info "After ${METRIC}: ${AFTER}  delta=${DELTA}"

assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "$(get_evidence "$METRIC" "")"

# ---------------------------------------------------------------------------
# Sub-scenario 28d: Hot-reload with 4th tenant
# ---------------------------------------------------------------------------

SCENARIO_NAME="TenantVector hot-reload detects added tenant"

log_info "Applying 4-tenant tenantvector ConfigMap (adding obp-poll-2)..."
kubectl apply -f - <<EOF || true
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-tenantvector
  namespace: simetra
data:
  tenantvector.json: |
    {
      "Tenants": [
        {
          "Id": "npb-trap",
          "Priority": 1,
          "Metrics": [
            { "Ip": "${NPB_IP}", "Port": 161, "MetricName": "npb_port_status_P1" },
            { "Ip": "${NPB_IP}", "Port": 161, "MetricName": "npb_port_rx_octets_P1" },
            { "Ip": "${NPB_IP}", "Port": 161, "MetricName": "npb_port_tx_octets_P1" },
            { "Ip": "${NPB_IP}", "Port": 161, "MetricName": "npb_cpu_util" }
          ]
        },
        {
          "Id": "npb-poll",
          "Priority": 2,
          "Metrics": [
            { "Ip": "${NPB_IP}", "Port": 161, "MetricName": "npb_mem_util" },
            { "Ip": "${NPB_IP}", "Port": 161, "MetricName": "npb_sys_temp" },
            { "Ip": "${NPB_IP}", "Port": 161, "MetricName": "npb_port_rx_packets_P1" },
            { "Ip": "${NPB_IP}", "Port": 161, "MetricName": "npb_port_tx_packets_P1" }
          ]
        },
        {
          "Id": "obp-poll",
          "Priority": 3,
          "Metrics": [
            { "Ip": "${OBP_IP}", "Port": 161, "MetricName": "obp_channel_L1" },
            { "Ip": "${OBP_IP}", "Port": 161, "MetricName": "obp_r1_power_L1" },
            { "Ip": "${OBP_IP}", "Port": 161, "MetricName": "obp_r2_power_L1" },
            { "Ip": "${OBP_IP}", "Port": 161, "MetricName": "obp_channel_L2" }
          ]
        },
        {
          "Id": "obp-poll-2",
          "Priority": 4,
          "Metrics": [
            { "Ip": "${OBP_IP}", "Port": 161, "MetricName": "obp_r3_power_L1" },
            { "Ip": "${OBP_IP}", "Port": 161, "MetricName": "obp_r4_power_L1" }
          ]
        }
      ]
    }
EOF

log_info "Waiting 15s for watcher detection..."
sleep 15

PODS=$(kubectl get pods -n simetra -l app=snmp-collector -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true
FOUND_RELOAD=0
RELOAD_EVIDENCE=""

for POD in $PODS; do
    POD_LOGS=$(kubectl logs "$POD" -n simetra --since=30s 2>/dev/null) || true
    # Check for registry diff log containing both "added" and "obp-poll-2"
    if echo "$POD_LOGS" | grep "added" > /dev/null 2>&1 && echo "$POD_LOGS" | grep "obp-poll-2" > /dev/null 2>&1; then
        FOUND_RELOAD=1
        RELOAD_EVIDENCE="pod=${POD} logged diff with added=[obp-poll-2]"
        break
    fi
done

if [ "$FOUND_RELOAD" -eq 1 ]; then
    record_pass "$SCENARIO_NAME" "$RELOAD_EVIDENCE"
else
    record_fail "$SCENARIO_NAME" "No pod logged diff containing 'added' and 'obp-poll-2' within 30s window"
fi

# ---------------------------------------------------------------------------
# Cleanup: Restore original tenantvector ConfigMap
# ---------------------------------------------------------------------------

log_info "Restoring original tenantvector ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenantvector-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenantvector-configmap.yaml" || \
        log_warn "Failed to restore tenantvector ConfigMap from snapshot; re-applying dev file with placeholders substituted"
else
    log_warn "Original tenantvector snapshot not found; re-applying dev file with placeholder IPs substituted"
    sed \
        -e "s/PLACEHOLDER_NPB_IP/${NPB_IP}/g" \
        -e "s/PLACEHOLDER_OBP_IP/${OBP_IP}/g" \
        "$TENANTVECTOR_YAML" \
        | kubectl apply -f - || log_warn "Fallback tenantvector restore also failed"
fi
