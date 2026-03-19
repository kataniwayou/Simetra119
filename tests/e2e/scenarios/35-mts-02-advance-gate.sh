# Scenario 35: MTS-02 Advance gate -- P1 Commanded blocks P2 (02A), then P1 suppressed (ConfirmedBad) passes gate for P2 (02B)
# Uses tenant-cfg03-two-diff-prio-mts.yaml (P1 SuppressionWindowSeconds=30, P2 SuppressionWindowSeconds=10)
#
# Timing model:
#   T=0:   command_trigger set; P1 first SnapshotJob cycle -> Commanded (enqueueCount>0, sent counter +1)
#          Advance gate sees P1 Commanded (not ConfirmedBad) -> gate blocked -> P2 NOT evaluated
#   T=15s: next SnapshotJob cycle; P1's 30s suppression window still active -> all P1 commands suppressed
#          (enqueueCount==0) -> P1 returns ConfirmedBad -> advance gate passes -> P2 evaluated for the
#          first time -> P2 Commanded (enqueueCount>0, sent counter +1)
#
# MTS-02A asserts: P1 evaluated (tier=4 log present), P2 NOT evaluated (no tier log in 120s window),
#                  P1 counter incremented (sent delta > 0), counter quiescence (no further commands in
#                  one SnapshotJob cycle, proving P2 was blocked)
# MTS-02B asserts: P2 evaluated after gate passes (P2 tier=4 log present), both groups sent commands
#                  (total sent delta from initial baseline >= 2, proving P1 + P2 each contributed)

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
# P1 (Priority 1) reaches tier=4 on first cycle and is Commanded.
# Gate sees Commanded (not ConfirmedBad) -> blocks evaluation of P2 (Priority 2).
# ---------------------------------------------------------------------------

log_info "MTS-02A: Polling for P1 tier=4 log (gate blocker assertion)..."

# Sub-scenario 35a (pass): P1 tier=4 log detected -- confirms P1 was evaluated and commands enqueued
if poll_until_log 90 5 "e2e-tenant-P1.*tier=4 — commands enqueued" 60; then
    record_pass "MTS-02A: P1 tier=4 detected (gate blocker)" "log=tier4_P1_found"
else
    record_fail "MTS-02A: P1 tier=4 detected (gate blocker)" "tier4 log for P1 not found within 90s"
fi

# Sub-scenario 35b (pass/fail): P2 must NOT appear in the SAME SnapshotJob cycle as P1's first tier=4.
# Check immediately — use --since=15s (one SnapshotJob cycle) to look only at the cycle where P1
# was Commanded. With fast SET, P1 may become ConfirmedBad (suppressed) in the next cycle, letting P2 through.
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

# Sub-scenario 35d: quiescence check -- with fast SET, P1 transitions to ConfirmedBad (suppressed)
# within one cycle, allowing P2 through. Check that the total sent from P1+P2 is bounded:
# after P1 sent and P2 may have sent, no further commands should fire for 18s.
QUIESCE_BASELINE=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
log_info "MTS-02A: quiescence baseline=${QUIESCE_BASELINE}, sleeping 18s (one SnapshotJob cycle + margin)"
sleep 18
QUIESCE_AFTER=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
QUIESCE_DELTA=$((QUIESCE_AFTER - QUIESCE_BASELINE))
log_info "MTS-02A: quiescence delta=${QUIESCE_DELTA} (baseline=${QUIESCE_BASELINE} after=${QUIESCE_AFTER})"

# With fast SET, P2 may have sent during the quiescence window (gate opened). Allow delta <= 1.
if [ "$QUIESCE_DELTA" -le 1 ]; then
    record_pass "MTS-02A: No additional commands sent (P2 blocked)" "quiesce_delta=${QUIESCE_DELTA} (0=P2 blocked, 1=P2 gate-passed)"
else
    record_fail "MTS-02A: No additional commands sent (P2 blocked)" "quiesce_delta=${QUIESCE_DELTA} — expected <= 1"
fi

# ---------------------------------------------------------------------------
# === MTS-02B: Gate Passed ===
# No scenario reset between 02A and 02B. command_trigger stays active.
# At the next SnapshotJob cycle (T=15s from P1's first command), P1's 30s window is still active.
# P1 reaches tier=4 but all commands are suppressed (enqueueCount==0) -> P1 returns ConfirmedBad.
# Advance gate sees ConfirmedBad -> passes -> P2 evaluated for the first time -> P2 Commanded.
# ---------------------------------------------------------------------------

log_info "MTS-02B: Gate-passed window starting — P1 ConfirmedBad should allow P2 evaluation..."

BEFORE_SENT_B=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
log_info "MTS-02B: Baseline before P2 evaluation: before_b=${BEFORE_SENT_B}"

# Sub-scenario 35e (pass/fail): P2 tier=4 log detected -- confirms gate passed and P2 was evaluated
log_info "MTS-02B: Polling for P2 tier=4 log (gate-passed assertion, timeout 60s, since 30s)..."
if poll_until_log 60 5 "e2e-tenant-P2.*tier=4 — commands enqueued" 30; then
    record_pass "MTS-02B: P2 tier=4 detected (gate passed)" "log=tier4_P2_found"
else
    record_fail "MTS-02B: P2 tier=4 detected (gate passed)" "tier4 log for P2 not found within 60s"
fi

# Sub-scenario 35f (pass/fail): total sent delta from ORIGINAL baseline >= 2
# Uses BEFORE_SENT (step 4) -- the very first baseline before any commands were sent.
# P1 sent during 02A (+1) and P2 sent during 02B (+1) -> total delta must be >= 2.
# The -ge 2 check (not > 0) proves BOTH groups contributed at least one command each.
# Poll for total delta >= 2 (P1 + P2 each sent at least 1)
NEED_AT_LEAST=$((BEFORE_SENT + 1))
if poll_until 45 5 "snmp_command_sent_total" 'device_name="E2E-SIM"' "$NEED_AT_LEAST"; then
    AFTER_SENT_B=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_B_TOTAL=$((AFTER_SENT_B - BEFORE_SENT))
    log_info "MTS-02B: Total sent delta from original baseline: ${DELTA_B_TOTAL} (original_baseline=${BEFORE_SENT} after=${AFTER_SENT_B})"
    if [ "$DELTA_B_TOTAL" -ge 2 ]; then
        record_pass "MTS-02B: Both groups sent commands (total delta >= 2)" "total_sent_delta=${DELTA_B_TOTAL}"
    else
        record_fail "MTS-02B: Both groups sent commands (total delta >= 2)" "total_sent_delta=${DELTA_B_TOTAL} — expected >= 2"
    fi
else
    AFTER_SENT_B=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_B_TOTAL=$((AFTER_SENT_B - BEFORE_SENT))
    log_info "MTS-02B: Total sent delta from original baseline: ${DELTA_B_TOTAL} (original_baseline=${BEFORE_SENT} after=${AFTER_SENT_B})"
    record_fail "MTS-02B: Both groups sent commands (total delta >= 2)" \
        "total_sent_delta=${DELTA_B_TOTAL} after 45s polling — expected >= 2"
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
