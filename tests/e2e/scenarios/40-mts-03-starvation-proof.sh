# Scenario 40: MTS-03 Priority starvation proof -- P2 never evaluated while P1 is Unresolved
# Uses tenant-cfg03-two-diff-prio.yaml (P1 priority=1, P2 priority=2, both SuppressionWindowSeconds=10)
#
# With SnapshotJob IntervalSeconds=1 (E2E cluster config):
#   - P1 reaches tier=4 Unresolved on every cycle (command_trigger scenario)
#   - First cycle: commands enqueued (sent counter +1)
#   - Next 10 cycles: commands suppressed (suppressed counter increments)
#   - Cycle 11+: commands enqueue again (sent counter +1), suppress cycle repeats
#   - P1 is ALWAYS Unresolved -> advance gate ALWAYS blocks -> P2 NEVER evaluated
#
# This proves the Phase 59 bug fix: suppressed commands no longer return Resolved/Violated
# (which would have let P2 through). They return Unresolved, permanently blocking P2.

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply equal-suppression fixture
# ---------------------------------------------------------------------------

log_info "MTS-03: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "MTS-03: Applying tenant-cfg03-two-diff-prio.yaml (equal suppression windows)..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg03-two-diff-prio.yaml" > /dev/null 2>&1 || true

log_info "MTS-03: Waiting for tenant vector reload (timeout 60s)..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "MTS-03: Tenant vector reload confirmed"
else
    log_warn "MTS-03: Tenant vector reload log not found within 60s — proceeding anyway"
fi

# ---------------------------------------------------------------------------
# Set command_trigger scenario and capture baselines
# ---------------------------------------------------------------------------

log_info "MTS-03: Setting simulator to command_trigger scenario..."
sim_set_scenario command_trigger

BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
BEFORE_SUPP=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-tenant-P1"')
log_info "MTS-03: Baselines: sent=${BEFORE_SENT}, suppressed_P1=${BEFORE_SUPP}"

# ---------------------------------------------------------------------------
# === MTS-03A: P1 reaches tier=4 and sends first command ===
# ---------------------------------------------------------------------------

log_info "MTS-03A: Polling for P1 tier=4 log..."
if poll_until_log 90 1 "e2e-tenant-P1.*tier=4 — commands enqueued" 60; then
    record_pass "MTS-03A: P1 tier=4 detected (command intent)" "log=tier4_P1_found"
else
    record_fail "MTS-03A: P1 tier=4 detected (command intent)" "tier4 log for P1 not found within 90s"
fi

# Sub-scenario 40b: P1 sent counter incremented
log_info "MTS-03A: Polling for P1 sent counter increment..."
if poll_until 45 5 "snmp_command_sent_total" 'device_name="E2E-SIM"' "$BEFORE_SENT"; then
    AFTER_SENT_A=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_SENT_A=$((AFTER_SENT_A - BEFORE_SENT))
    record_pass "MTS-03A: P1 sent counter incremented" "sent_delta=${DELTA_SENT_A}"
else
    AFTER_SENT_A=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_SENT_A=$((AFTER_SENT_A - BEFORE_SENT))
    record_fail "MTS-03A: P1 sent counter incremented" "sent_delta=${DELTA_SENT_A} after 45s polling"
fi

# ---------------------------------------------------------------------------
# === MTS-03B: Wait for suppression window, then verify suppressed counter ===
# P1's 10s suppression window means commands are suppressed for ~10 cycles (1s interval).
# Wait 12s (10s window + 2s margin) then check suppressed counter.
# ---------------------------------------------------------------------------

log_info "MTS-03B: Waiting 12s for suppression window to produce suppressed events..."
sleep 12

log_info "MTS-03B: Polling for P1 suppressed counter increment..."
if poll_until 45 5 "snmp_command_suppressed_total" 'device_name="e2e-tenant-P1"' "$BEFORE_SUPP"; then
    AFTER_SUPP=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-tenant-P1"')
    DELTA_SUPP=$((AFTER_SUPP - BEFORE_SUPP))
    record_pass "MTS-03B: P1 suppressed counter incremented" "suppressed_delta=${DELTA_SUPP}"
else
    AFTER_SUPP=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-tenant-P1"')
    DELTA_SUPP=$((AFTER_SUPP - BEFORE_SUPP))
    record_fail "MTS-03B: P1 suppressed counter incremented" "suppressed_delta=${DELTA_SUPP} after 45s polling"
fi

# ---------------------------------------------------------------------------
# === MTS-03C: Starvation assertion -- P2 NEVER appears in tier logs ===
# Check the full observation window (120s since scenario started).
# If P2 has ANY tier log, the advance gate is NOT blocking correctly.
# ---------------------------------------------------------------------------

log_info "MTS-03C: Checking P2 is absent from ALL tier logs (starvation proof)..."
PODS=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true
P2_TIER_FOUND=0
for POD in $PODS; do
    P2_LOGS=$(kubectl logs "$POD" -n simetra --since=120s 2>/dev/null \
        | grep "e2e-tenant-P2.*tier=" || echo "") || true
    if [ -n "$P2_LOGS" ]; then
        P2_TIER_FOUND=1
        log_info "MTS-03C: UNEXPECTED P2 tier log found in pod ${POD}: ${P2_LOGS}"
        break
    fi
done

if [ "$P2_TIER_FOUND" -eq 0 ]; then
    record_pass "MTS-03C: P2 never evaluated (starvation proven)" "P2 tier log absent in 120s window across all pods"
else
    record_fail "MTS-03C: P2 never evaluated (starvation proven)" "P2 tier log found — advance gate did NOT block"
fi

# ---------------------------------------------------------------------------
# Cleanup: reset simulator, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "MTS-03: Resetting simulator scenario..."
reset_scenario

log_info "MTS-03: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "MTS-03: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "MTS-03: Original tenant ConfigMap snapshot not found — skipping restore"
fi
