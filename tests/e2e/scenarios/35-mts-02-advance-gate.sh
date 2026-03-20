# Scenario 35: MTS-02 Advance gate -- three sub-scenarios testing all gate results
# Uses tenant-cfg03-two-diff-prio-mts.yaml (P1 SuppressionWindowSeconds=30, P2 SuppressionWindowSeconds=10)
#
# MTS-02A: P1 Unresolved (command_trigger, tier=4) -> gate BLOCKS -> P2 NOT evaluated
# MTS-02B: P1 Resolved  (default, tier=2 all resolved violated) -> gate PASSES -> P2 evaluated
# MTS-02C: P1 Healthy   (healthy, tier=3 evaluate not violated) -> gate PASSES -> P2 evaluated

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply MTS fixture (P1 window=30s)
# ---------------------------------------------------------------------------

log_info "MTS-02: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

# Set command_trigger BEFORE tenant config so the first evaluation after reload
# sees violated evaluate metrics → P1 tier=4 → Unresolved → gate blocks P2.
# Without this, the default scenario gives P1 tier=2 Resolved which PASSES the gate.
log_info "MTS-02: Setting simulator to command_trigger scenario..."
sim_set_scenario command_trigger

log_info "MTS-02: Applying tenant-cfg03-two-diff-prio-mts.yaml (P1 SuppressionWindowSeconds=30)..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg03-two-diff-prio-mts.yaml" > /dev/null 2>&1 || true

log_info "MTS-02: Waiting for tenant vector reload (timeout 60s)..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "MTS-02: Tenant vector reload confirmed"
else
    log_warn "MTS-02: Tenant vector reload log not found within 60s — proceeding anyway"
fi

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
log_info "MTS-02: Initial sent baseline=${BEFORE_SENT}"

# ---------------------------------------------------------------------------
# === MTS-02A: Gate Blocked ===
# P1 (Priority 1) reaches tier=4 on first cycle and is Unresolved.
# Gate sees Unresolved -> blocks evaluation of P2 (Priority 2).
# ---------------------------------------------------------------------------

log_info "MTS-02A: Polling for P1 tier=4 log (gate blocker assertion)..."

# Sub-scenario 35a (pass): P1 tier=4 log detected -- confirms P1 was evaluated and commands enqueued
if poll_until_log 90 5 "e2e-tenant-P1.*tier=4 — commands enqueued" 60; then
    record_pass "MTS-02A: P1 tier=4 detected (gate blocker)" "log=tier4_P1_found"
else
    record_fail "MTS-02A: P1 tier=4 detected (gate blocker)" "tier4 log for P1 not found within 90s"
fi

# Sub-scenario 35b (pass/fail): P2 must NOT appear in tier logs while P1 is Unresolved.
# command_trigger was set before tenant reload, so P1 reaches tier=4 → Unresolved from
# the very first evaluation cycle. P2 should never appear in tier logs.
log_info "MTS-02A: Checking P2 is absent from tier logs (--since=15s)..."
PODS=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true
P2_FOUND=0
for POD in $PODS; do
    P2_LOGS=$(kubectl logs "$POD" -n simetra --since=15s 2>/dev/null \
        | grep "e2e-tenant-P2.*tier=" || echo "") || true
    if [ -n "$P2_LOGS" ]; then
        P2_FOUND=1
        break
    fi
done

if [ "$P2_FOUND" -eq 0 ]; then
    record_pass "MTS-02A: P2 not evaluated when gate blocked" "e2e-tenant-P2 tier log absent in 15s window"
else
    record_fail "MTS-02A: P2 not evaluated when gate blocked" "e2e-tenant-P2 tier log found unexpectedly: $P2_LOGS"
fi

# Sub-scenario 35c (pass/fail): sent counter delta > 0 -- confirms P1 actually sent a command
# Re-capture baseline now (after P1 tier=4 confirmed) to guard against counter resets
# from prior scenario pod restarts (scenario 28 does rollout restart).
FRESH_BASELINE=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
# Poll for counter — SNMP SET round-trip + OTel export + Prometheus scrape takes time.
if poll_until 45 5 "snmp_command_dispatched_total" 'device_name="E2E-SIM"' "$FRESH_BASELINE"; then
    AFTER_SENT_A=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
    DELTA_A=$((AFTER_SENT_A - FRESH_BASELINE))
    log_info "MTS-02A: sent delta after P1 command: ${DELTA_A} (before=${FRESH_BASELINE} after=${AFTER_SENT_A})"
    record_pass "MTS-02A: P1 sent counter incremented" "sent_delta=${DELTA_A}"
else
    AFTER_SENT_A=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
    DELTA_A=$((AFTER_SENT_A - FRESH_BASELINE))
    log_info "MTS-02A: sent delta after P1 command: ${DELTA_A} (before=${FRESH_BASELINE} after=${AFTER_SENT_A})"
    record_fail "MTS-02A: P1 sent counter incremented" "sent_delta=${DELTA_A} after 45s polling"
fi

# ---------------------------------------------------------------------------
# === MTS-02B: Gate Passed via Resolved ===
# Switch simulator to 'default' — all OIDs return 0.
# P1 resolved metrics 0 < Min:1.0 → all resolved violated → tier=2 Resolved.
# Resolved advances gate → P2 evaluated.
# ---------------------------------------------------------------------------

log_info "MTS-02B: Switching simulator to 'default' to make P1 Resolved (tier=2)..."
sim_set_scenario default

# Sub-scenario 35d (pass/fail): P1 tier=2 Resolved log — confirms P1 is Resolved (gate passes)
log_info "MTS-02B: Polling for P1 tier=2 Resolved log (timeout 60s, since 30s)..."
if poll_until_log 60 5 "e2e-tenant-P1.*tier=2 — all resolved violated" 30; then
    record_pass "MTS-02B: P1 tier=2 Resolved (gate passes)" "log=tier2_P1_resolved_found"
else
    record_fail "MTS-02B: P1 tier=2 Resolved (gate passes)" "tier2 Resolved log for P1 not found within 60s"
fi

# Sub-scenario 35e (pass/fail): P2 tier log appears — confirms gate passed via Resolved
log_info "MTS-02B: Polling for P2 tier log (gate-passed via Resolved, timeout 60s, since 30s)..."
if poll_until_log 60 5 "e2e-tenant-P2.*tier=" 30; then
    record_pass "MTS-02B: P2 evaluated (gate passed via Resolved)" "log=P2_tier_found_resolved"
else
    record_fail "MTS-02B: P2 evaluated (gate passed via Resolved)" "P2 tier log not found within 60s"
fi

# ---------------------------------------------------------------------------
# === MTS-02C: Gate Passed via Healthy ===
# Switch simulator to 'healthy' — resolved in-range, evaluate in-range.
# P1 reaches tier=3 Healthy. Healthy advances gate → P2 evaluated.
# ---------------------------------------------------------------------------

log_info "MTS-02C: Switching simulator to 'healthy' to make P1 Healthy (tier=3)..."
sim_set_scenario healthy

# Sub-scenario 35f (pass/fail): P1 tier=3 Healthy log
log_info "MTS-02C: Polling for P1 tier=3 Healthy log (timeout 60s, since 30s)..."
if poll_until_log 60 5 "e2e-tenant-P1.*tier=3 — not all evaluate metrics violated" 30; then
    record_pass "MTS-02C: P1 tier=3 Healthy (gate passes)" "log=tier3_P1_healthy_found"
else
    record_fail "MTS-02C: P1 tier=3 Healthy (gate passes)" "tier3 Healthy log for P1 not found within 60s"
fi

# Sub-scenario 35g (pass/fail): P2 tier log appears — confirms gate passed via Healthy
log_info "MTS-02C: Polling for P2 tier log (gate-passed via Healthy, timeout 60s, since 30s)..."
if poll_until_log 60 5 "e2e-tenant-P2.*tier=" 30; then
    record_pass "MTS-02C: P2 evaluated (gate passed via Healthy)" "log=P2_tier_found_healthy"
else
    record_fail "MTS-02C: P2 evaluated (gate passed via Healthy)" "P2 tier log not found within 60s"
fi

# ---------------------------------------------------------------------------
# Cleanup: reset simulator, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "MTS-02: Resetting simulator scenario..."
reset_scenario

log_info "MTS-02: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "MTS-02: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "MTS-02: Original tenant ConfigMap snapshot not found — skipping restore"
fi
