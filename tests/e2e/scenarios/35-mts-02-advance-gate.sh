# Scenario 35: MTS-02 Advance gate -- P1 Unresolved blocks P2 (02A), then P1 Healthy passes gate for P2 (02B)
# Uses tenant-cfg03-two-diff-prio-mts.yaml (P1 SuppressionWindowSeconds=30, P2 SuppressionWindowSeconds=10)
#
# Timing model:
#   T=0:   command_trigger set; P1 first SnapshotJob cycle -> Unresolved (tier=4 command intent)
#          Advance gate sees P1 Unresolved -> gate blocked -> P2 NOT evaluated
#   T=Ns:  Simulator switched to 'default' scenario; all metrics return baseline values (0)
#          P1's evaluate metric e2e_port_utilization=0 (< Max:80) -> tier=3 Healthy
#          Advance gate sees P1 Healthy -> passes -> P2 evaluated -> P2 also Healthy
#
# MTS-02A asserts: P1 evaluated (tier=4 log present), P2 NOT evaluated (no tier log),
#                  P1 counter incremented (sent delta > 0)
# MTS-02B asserts: After switching to default scenario, P1 returns Healthy (tier=3 log),
#                  P2 is evaluated (P2 tier=3 log present, proving gate passed)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply MTS fixture (P1 window=30s)
# ---------------------------------------------------------------------------

log_info "MTS-02: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "MTS-02: Applying tenant-cfg03-two-diff-prio-mts.yaml (P1 SuppressionWindowSeconds=30)..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg03-two-diff-prio-mts.yaml" > /dev/null 2>&1 || true

log_info "MTS-02: Waiting for tenant vector reload (timeout 60s)..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "MTS-02: Tenant vector reload confirmed"
else
    log_warn "MTS-02: Tenant vector reload log not found within 60s — proceeding anyway"
fi

# ---------------------------------------------------------------------------
# Set command_trigger scenario and capture initial baseline
# ---------------------------------------------------------------------------

log_info "MTS-02: Setting simulator to command_trigger scenario..."
sim_set_scenario command_trigger

BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
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

# Sub-scenario 35b (pass/fail): P2 must NOT appear in tier logs while P1 is Unresolved (--since=15s check).
log_info "MTS-02A: Checking P2 is absent from tier logs in same cycle as P1 (--since=15s)..."
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
# Poll for counter — SNMP SET round-trip + OTel export + Prometheus scrape takes time.
if poll_until 45 5 "snmp_command_sent_total" 'device_name="E2E-SIM"' "$BEFORE_SENT"; then
    AFTER_SENT_A=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_A=$((AFTER_SENT_A - BEFORE_SENT))
    log_info "MTS-02A: sent delta after P1 command: ${DELTA_A} (before=${BEFORE_SENT} after=${AFTER_SENT_A})"
    record_pass "MTS-02A: P1 sent counter incremented" "sent_delta=${DELTA_A}"
else
    AFTER_SENT_A=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_A=$((AFTER_SENT_A - BEFORE_SENT))
    log_info "MTS-02A: sent delta after P1 command: ${DELTA_A} (before=${BEFORE_SENT} after=${AFTER_SENT_A})"
    record_fail "MTS-02A: P1 sent counter incremented" "sent_delta=${DELTA_A} after 45s polling"
fi

# ---------------------------------------------------------------------------
# === MTS-02B: Gate Passed ===
# Switch simulator to 'default' — P1 metrics return to baseline (evaluate not violated).
# P1 reaches tier=3 Healthy → advance gate passes → P2 is evaluated.
# ---------------------------------------------------------------------------

log_info "MTS-02B: Switching simulator to 'default' to make P1 Healthy..."
sim_set_scenario default

# Sub-scenario 35e (pass/fail): P1 tier=3 Healthy log — confirms P1 is no longer Unresolved
log_info "MTS-02B: Polling for P1 tier=3 Healthy log (timeout 60s, since 15s)..."
if poll_until_log 60 5 "e2e-tenant-P1.*tier=3 — not all evaluate metrics violated" 15; then
    record_pass "MTS-02B: P1 tier=3 Healthy (gate passes)" "log=tier3_P1_healthy_found"
else
    record_fail "MTS-02B: P1 tier=3 Healthy (gate passes)" "tier3 Healthy log for P1 not found within 60s"
fi

# Sub-scenario 35f (pass/fail): P2 tier log appears — confirms gate passed and P2 was evaluated
log_info "MTS-02B: Polling for P2 tier log (gate-passed assertion, timeout 60s, since 15s)..."
if poll_until_log 60 5 "e2e-tenant-P2.*tier=" 15; then
    record_pass "MTS-02B: P2 evaluated (gate passed)" "log=P2_tier_found"
else
    record_fail "MTS-02B: P2 evaluated (gate passed)" "P2 tier log not found within 60s"
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
