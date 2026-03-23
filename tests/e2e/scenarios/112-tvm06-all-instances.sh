# Scenario 112: TVM-06 All-instances export -- tenant_state present on every replica pod
# The SnmpCollector.Tenant meter is NOT gated by MetricRoleGatedExporter.
# It exports on ALL instances (leader + followers). The SnmpCollector.Leader meter
# IS gated -- only the leader exports snmp_gauge/snmp_info.
# k8s.pod.name resource attribute is converted to k8s_pod_name Prometheus label via
# resource_to_telemetry_conversion.enabled: true in otel-collector config.
#
# Proof: tenant_state series present for EVERY pod (including followers),
# while snmp_gauge is absent on follower pods -- confirming the third meter architecture.
#
# Sub-assertions:
#   TVM-06A: tenant_state present on every pod (all-instances export confirmed)
#   TVM-06B: at least one follower pod exports tenant_state but has no snmp_gauge
#            (leader-gated meter absent on followers confirmed)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply single-tenant PSS fixture
# ---------------------------------------------------------------------------

log_info "TVM-06: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "TVM-06: Applying tenant-cfg06-pss-single.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-single.yaml" > /dev/null 2>&1 || true

log_info "TVM-06: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "TVM-06: Tenant vector reload confirmed"
else
    log_warn "TVM-06: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime T2 OIDs to in-range values and wait for a healthy evaluation cycle
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# ---------------------------------------------------------------------------

log_info "TVM-06: Priming T2 OIDs with in-range values..."
sim_set_oid "5.1" "10"   # T2 eval in-range (>= Min:10)
sim_set_oid "5.2" "1"    # T2 res1 in-range (>= Min:1)
sim_set_oid "5.3" "1"    # T2 res2 in-range (>= Min:1)

log_info "TVM-06: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Wait for at least one healthy evaluation cycle (tier=3 log)
# ---------------------------------------------------------------------------

log_info "TVM-06: Polling for e2e-pss-tenant tier=3 log (healthy evaluation cycle, 30s)..."
if poll_until_log 30 1 "e2e-pss-tenant.*tier=3" 15; then
    log_info "TVM-06: Healthy evaluation cycle confirmed"
else
    log_warn "TVM-06: tier=3 log not detected within 30s; proceeding"
fi

log_info "TVM-06: Waiting 5s for metrics to propagate to Prometheus..."
sleep 5

# ---------------------------------------------------------------------------
# Preflight: verify k8s_pod_name label exists on tenant_state
# Requires resource_to_telemetry_conversion.enabled: true in otel-collector config.
# ---------------------------------------------------------------------------

VERIFY_LABEL=$(query_prometheus 'tenant_state{tenant_id="e2e-pss-tenant"}' \
    | jq -r '.data.result[0].metric.k8s_pod_name // ""')

if [ -z "$VERIFY_LABEL" ]; then
    record_fail "TVM-06: preflight" \
        "k8s_pod_name label not present on tenant_state -- resource_to_telemetry_conversion may not be working"
    # Cleanup before aborting
    reset_oid_overrides
    if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
        restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
            log_warn "TVM-06: Failed to restore tenant ConfigMap from snapshot"
    fi
    return 0 2>/dev/null || exit 0
fi

log_info "TVM-06: Preflight: k8s_pod_name label confirmed (example: ${VERIFY_LABEL})"

# ---------------------------------------------------------------------------
# Get all snmp-collector pod names
# ---------------------------------------------------------------------------

POD_NAMES=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{range .items[*]}{.metadata.name}{"\n"}{end}')

if [ -z "$POD_NAMES" ]; then
    record_fail "TVM-06: pod list" "No snmp-collector pods found in namespace simetra"
    reset_oid_overrides
    if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
        restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
            log_warn "TVM-06: Failed to restore tenant ConfigMap from snapshot"
    fi
    return 0 2>/dev/null || exit 0
fi

log_info "TVM-06: Found pods:"
for POD in $POD_NAMES; do
    log_info "TVM-06:   ${POD}"
done

# ---------------------------------------------------------------------------
# Sub-assertion TVM-06A: tenant_state present on EVERY pod (all-instances export)
# SnmpCollector.Tenant meter is not gated -- all replicas export tenant metrics.
# ---------------------------------------------------------------------------

ALL_PODS_HAVE_TENANT=true
for POD in $POD_NAMES; do
    [ -z "$POD" ] && continue
    COUNT=$(query_prometheus "tenant_state{k8s_pod_name=\"${POD}\",tenant_id=\"e2e-pss-tenant\"}" \
        | jq -r '.data.result | length')
    if [ "$COUNT" -gt 0 ]; then
        record_pass "TVM-06A: tenant_state present on pod ${POD}" \
            "k8s_pod_name=${POD} series_count=${COUNT}"
    else
        record_fail "TVM-06A: tenant_state present on pod ${POD}" \
            "k8s_pod_name=${POD} series_count=0"
        ALL_PODS_HAVE_TENANT=false
    fi
done

# ---------------------------------------------------------------------------
# Sub-assertion TVM-06B: follower pod exports tenant_state but NOT snmp_gauge
# A follower is a pod where snmp_gauge series count == 0 but tenant_state count > 0.
# At least one such pod must exist (3-replica cluster has 2 followers).
# ---------------------------------------------------------------------------

FOLLOWER_COUNT=0
for POD in $POD_NAMES; do
    [ -z "$POD" ] && continue
    GAUGE_COUNT=$(query_prometheus "snmp_gauge{k8s_pod_name=\"${POD}\"}" \
        | jq -r '.data.result | length')
    TENANT_COUNT=$(query_prometheus "tenant_state{k8s_pod_name=\"${POD}\",tenant_id=\"e2e-pss-tenant\"}" \
        | jq -r '.data.result | length')
    if [ "$GAUGE_COUNT" -eq 0 ] && [ "$TENANT_COUNT" -gt 0 ]; then
        FOLLOWER_COUNT=$((FOLLOWER_COUNT + 1))
        record_pass "TVM-06B: follower ${POD} has tenant_state but no snmp_gauge" \
            "snmp_gauge=0 tenant_state=${TENANT_COUNT} k8s_pod_name=${POD}"
    fi
done

if [ "$FOLLOWER_COUNT" -eq 0 ]; then
    record_fail "TVM-06B: at least one follower exports tenant_state without snmp_gauge" \
        "follower_count=0 -- no pod found with tenant_state > 0 and snmp_gauge == 0"
else
    log_info "TVM-06: ${FOLLOWER_COUNT} follower pod(s) confirmed with tenant metrics but no leader-gated metrics"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "TVM-06: Clearing OID overrides..."
reset_oid_overrides

log_info "TVM-06: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "TVM-06: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "TVM-06: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
