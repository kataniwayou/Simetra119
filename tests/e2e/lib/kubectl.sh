#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
# kubectl.sh -- K8s interaction utilities for E2E tests
# ============================================================================

# Global port-forward PID tracking
PF_PIDS=()

# ---------------------------------------------------------------------------
# Port-forward management
# ---------------------------------------------------------------------------

start_port_forward() {
    local service="$1"
    local local_port="$2"
    local remote_port="$3"

    log_info "Starting port-forward: ${service} ${local_port}:${remote_port}"
    kubectl port-forward "svc/${service}" "${local_port}:${remote_port}" -n simetra &>/dev/null &
    PF_PIDS+=($!)
    sleep 2
}

stop_port_forwards() {
    for pid in "${PF_PIDS[@]:-}"; do
        if [ -n "$pid" ]; then
            kill "$pid" 2>/dev/null || true
        fi
    done
    PF_PIDS=()
}

# ---------------------------------------------------------------------------
# Pod readiness
# ---------------------------------------------------------------------------

check_pods_ready() {
    local phases
    phases=$(kubectl get pods -n simetra -l app=snmp-collector -o jsonpath='{.items[*].status.phase}')

    if [ -z "$phases" ]; then
        log_error "No snmp-collector pods found"
        return 1
    fi

    local total=0
    local running=0
    for phase in $phases; do
        total=$((total + 1))
        if [ "$phase" = "Running" ]; then
            running=$((running + 1))
        fi
    done

    log_info "Pods: ${running}/${total} running"

    if [ "$running" -eq "$total" ]; then
        return 0
    else
        return 1
    fi
}

# ---------------------------------------------------------------------------
# Prometheus reachability
# ---------------------------------------------------------------------------

check_prometheus_reachable() {
    local http_code
    http_code=$(curl -s -o /dev/null -w '%{http_code}' "${PROM_URL:-http://localhost:9090}/-/ready" 2>/dev/null) || true

    if [ "$http_code" = "200" ]; then
        return 0
    else
        log_error "Prometheus returned HTTP ${http_code}"
        return 1
    fi
}

# ---------------------------------------------------------------------------
# ConfigMap management
# ---------------------------------------------------------------------------

save_configmap() {
    local name="$1"
    local namespace="$2"
    local output_file="$3"

    kubectl get configmap "$name" -n "$namespace" -o yaml \
        | sed '/resourceVersion:/d' \
        | sed '/uid:/d' \
        | sed '/creationTimestamp:/d' \
        > "$output_file"
}

restore_configmap() {
    local file="$1"

    kubectl apply -f "$file"
}

# ---------------------------------------------------------------------------
# ConfigMap snapshot/restore (wraps primitives above)
# ---------------------------------------------------------------------------

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

snapshot_configmaps() {
    log_info "Snapshotting ConfigMaps for mutation testing..."
    save_configmap "simetra-devices" "simetra" "$FIXTURES_DIR/.original-devices-configmap.yaml"
    save_configmap "simetra-oidmaps" "simetra" "$FIXTURES_DIR/.original-oidmaps-configmap.yaml"
    save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml"
    log_info "ConfigMap snapshots saved"
}

restore_configmaps() {
    log_info "Restoring ConfigMaps from snapshots..."
    if [ -f "$FIXTURES_DIR/.original-devices-configmap.yaml" ]; then
        restore_configmap "$FIXTURES_DIR/.original-devices-configmap.yaml"
    fi
    if [ -f "$FIXTURES_DIR/.original-oidmaps-configmap.yaml" ]; then
        restore_configmap "$FIXTURES_DIR/.original-oidmaps-configmap.yaml"
    fi
    if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
        restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml"
    fi
    log_info "ConfigMaps restored"
}
