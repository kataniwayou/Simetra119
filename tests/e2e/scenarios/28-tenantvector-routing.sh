# Scenario 28: TenantVector routing — fan-out counter increments, watcher loads, hot-reload works
# Deploys tenantvector ConfigMap with DNS names, verifies Prometheus routing counter
# increments, confirms watcher load logs, and tests hot-reload with a 4th tenant.

# ---------------------------------------------------------------------------
# Step 0: Apply tenantvector ConfigMap
# ---------------------------------------------------------------------------

# Snapshot current tenantvector ConfigMap BEFORE applying (for restore later)
FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

TENANTVECTOR_YAML="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/deploy/k8s/snmp-collector/simetra-tenants.yaml"

log_info "Applying tenantvector ConfigMap..."
kubectl apply -f "$TENANTVECTOR_YAML" || true

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
    if echo "$DESCRIBE_OUTPUT" | grep "simetra-tenants" > /dev/null 2>&1; then
        record_pass "$SCENARIO_NAME" "pod=${POD1} describe output contains simetra-tenants mount"
    else
        record_fail "$SCENARIO_NAME" "pod=${POD1} describe output does not contain simetra-tenants"
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
  name: simetra-tenants
  namespace: simetra
data:
  tenants.json: |
    {
      "Tenants": [
        {
          "Priority": 1,
          "Metrics": [
            { "Ip": "npb-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "npb_port_status_P1" },
            { "Ip": "npb-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "npb_port_rx_octets_P1" },
            { "Ip": "npb-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "npb_port_tx_octets_P1" },
            { "Ip": "npb-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "npb_cpu_util" }
          ]
        },
        {
          "Priority": 2,
          "Metrics": [
            { "Ip": "npb-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "npb_mem_util" },
            { "Ip": "npb-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "npb_sys_temp" },
            { "Ip": "npb-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "npb_port_rx_packets_P1" },
            { "Ip": "npb-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "npb_port_tx_packets_P1" }
          ]
        },
        {
          "Priority": 3,
          "Metrics": [
            { "Ip": "obp-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "obp_channel_L1" },
            { "Ip": "obp-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "obp_r1_power_L1" },
            { "Ip": "obp-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "obp_r2_power_L1" },
            { "Ip": "obp-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "obp_channel_L2" }
          ]
        },
        {
          "Priority": 4,
          "Metrics": [
            { "Ip": "obp-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "obp_r3_power_L1" },
            { "Ip": "obp-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "obp_r4_power_L1" }
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
    if echo "$POD_LOGS" | grep "reloaded" > /dev/null 2>&1 && echo "$POD_LOGS" | grep "tenants=4" > /dev/null 2>&1; then
        FOUND_RELOAD=1
        RELOAD_EVIDENCE="pod=${POD} logged reload with tenants=4"
        break
    fi
done

if [ "$FOUND_RELOAD" -eq 1 ]; then
    record_pass "$SCENARIO_NAME" "$RELOAD_EVIDENCE"
else
    record_fail "$SCENARIO_NAME" "No pod logged 'reloaded' with 'tenants=4' within 30s window"
fi

# ---------------------------------------------------------------------------
# Cleanup: Restore original tenantvector ConfigMap
# ---------------------------------------------------------------------------

log_info "Restoring original tenantvector ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "Failed to restore tenantvector ConfigMap from snapshot; re-applying dev file"
else
    log_warn "Original tenantvector snapshot not found; re-applying dev file"
    kubectl apply -f "$TENANTVECTOR_YAML" || log_warn "Fallback tenantvector restore also failed"
fi
