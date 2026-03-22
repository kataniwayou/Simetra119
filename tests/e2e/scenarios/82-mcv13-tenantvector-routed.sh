# Scenario 82: MCV-13 -- tenantvector.routed increments on tenant vector fan-out write
# TenantVectorFanOutBehavior fires once per holder.WriteValue call per matching tenant vector slot.
# Requires tenants ConfigMap to be applied (applied unconditionally here for idempotency).
SCENARIO_NAME="MCV-13: tenantvector.routed increments on fan-out write"
METRIC="snmp_tenantvector_routed_total"
FILTER=""

# Apply simetra-tenants.yaml unconditionally to ensure tenants are active.
# Handles the case where scenario 28 restored an empty original ConfigMap.
TENANTVECTOR_YAML="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/deploy/k8s/snmp-collector/simetra-tenants.yaml"

log_info "Applying tenantvector ConfigMap unconditionally (idempotency)..."
kubectl apply -f "$TENANTVECTOR_YAML" || true

# Wait for tenant watcher to reload and first poll cycle to complete routing
log_info "Waiting 30s for tenant watcher reload + first poll cycle + routing..."
sleep 30

# Snapshot BEFORE
BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
log_info "Baseline ${METRIC}: ${BEFORE}"

# Poll for counter increment (tenants active, routing should fire each poll interval)
log_info "Polling ${METRIC} for 90s (5s interval)..."
poll_until 90 5 "$METRIC" "$FILTER" "$BEFORE" || true

AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE=$(get_evidence "$METRIC" "$FILTER")
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "before=$BEFORE after=$AFTER delta=$DELTA | $EVIDENCE"
