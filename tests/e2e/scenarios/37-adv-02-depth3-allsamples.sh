# Scenario 37: ADV-02 Depth-3 time-series all-samples check
# Uses tenant-cfg04-aggregate.yaml (e2e-tenant-agg, Priority 1, Evaluate=e2e_total_util, TimeSeriesSize=3, Max:80)
#
# Phase 1 (Breach): agg_breach fills 3 slots with violated values -> AreAllEvaluateViolated returns true -> tier-4
# Phase 2 (Recovery): healthy overwrites one slot with in-range value -> AreAllEvaluateViolated returns false -> tier-3
#
# Timing model:
#   T=0:    sim_set_scenario agg_breach (sum=100 > Max:80)
#   T=10s:  Poll 1 -> slot[0] = 100 (violated)
#   T=20s:  Poll 2 -> slot[1] = 100 (violated)
#   T=30s:  Poll 3 -> slot[2] = 100 (violated) — series full
#   T=~45s: SnapshotJob: all 3 violated -> tier=4 fires, commands enqueued
#   Recovery:
#   T=breach+0:  sim_set_scenario healthy (sum=0 < Max:80)
#   T=breach+10: Poll overwrites one slot with 0 (in-range)
#   T=breach+15: SnapshotJob: 1 in-range + 2 violated -> AreAllEvaluateViolated = false -> tier=3
#
# Success criteria (from CONTEXT.md):
#   - tier-4 fires only after all 3 slots violated (min 75s wait)
#   - single in-range sample recovers to Healthy (tier-3 log + counter delta == 0)
#   - partial series violation does not fire (proven by recovery assertions)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply aggregate fixture
# ---------------------------------------------------------------------------

log_info "ADV-02: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "ADV-02: Applying tenant-cfg04-aggregate.yaml (e2e-tenant-agg, Evaluate=e2e_total_util, TimeSeriesSize=3, Max:80)..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg04-aggregate.yaml" > /dev/null 2>&1 || true

log_info "ADV-02: Waiting for tenant vector reload (timeout 60s)..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "ADV-02: Tenant vector reload confirmed"
else
    log_warn "ADV-02: Tenant vector reload log not found within 60s — proceeding anyway"
fi

# ---------------------------------------------------------------------------
# Phase 1 (Breach): set agg_breach scenario and capture initial baseline
# agg_breach: .4.2=2 (e2e_channel_state, in-range), .4.3=2 (e2e_bypass_status, in-range),
#             .4.5=50, .4.6=50 -> sum=100 > Max:80 -> e2e_total_util violated
# ---------------------------------------------------------------------------

log_info "ADV-02: Setting simulator to agg_breach scenario (.4.5=50 + .4.6=50 = sum 100 > Max:80)..."
sim_set_scenario agg_breach

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
log_info "ADV-02: Initial sent baseline=${BEFORE_SENT}"

# ---------------------------------------------------------------------------
# Sub-scenario 37a: tier=4 log fires for e2e-tenant-agg after all 3 slots filled
# TimeSeriesSize=3 requires ~30s to fill (3 polls x 10s) + SnapshotJob 15s = ~75s min.
# 90s timeout accommodates 3 poll cycles + SnapshotJob cycle + jitter.
# ---------------------------------------------------------------------------

log_info "ADV-02: Polling for e2e-tenant-agg tier=4 log after depth-3 fill (timeout 90s, since 60s)..."
if poll_until_log 90 5 "e2e-tenant-agg.*tier=4 — commands enqueued" 60; then
    record_pass "ADV-02: Depth-3 tier=4 after all slots violated" "log=tier4_depth3_found"
else
    record_fail "ADV-02: Depth-3 tier=4 after all slots violated" "tier4 log not found within 90s — series may not have filled"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 37b: sent counter incremented (command dispatch confirmed)
# Poll for counter — SNMP SET round-trip + OTel export + Prometheus scrape takes time.
# ---------------------------------------------------------------------------

if poll_until 45 5 "snmp_command_dispatched_total" 'device_name="E2E-SIM"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "ADV-02: Sent counter after tier=4: before=${BEFORE_SENT} after=${AFTER_SENT} delta=${DELTA_SENT}"
    record_pass "ADV-02: Command sent counter incremented" \
        "sent_delta=${DELTA_SENT} $(get_evidence "snmp_command_dispatched_total" 'device_name="E2E-SIM"')"
else
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "ADV-02: Sent counter after tier=4: before=${BEFORE_SENT} after=${AFTER_SENT} delta=${DELTA_SENT}"
    record_fail "ADV-02: Command sent counter incremented" \
        "sent_delta=${DELTA_SENT} expected > 0 after 45s polling $(get_evidence "snmp_command_dispatched_total" 'device_name="E2E-SIM"')"
fi

# ---------------------------------------------------------------------------
# Phase 2 (Recovery): switch to healthy, capture recovery baseline, assert tier=3
# healthy scenario: .4.5=0, .4.6=0 -> sum=0 < Max:80 -> e2e_total_util in-range
# After one poll cycle overwrites one slot, AreAllEvaluateViolated returns false -> tier=3
# ---------------------------------------------------------------------------

log_info "ADV-02: Switching to healthy scenario for recovery phase..."
sim_set_scenario healthy

RECOVERY_BASELINE=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
log_info "ADV-02: Recovery baseline captured: recovery_baseline=${RECOVERY_BASELINE}"

# ---------------------------------------------------------------------------
# Sub-scenario 37c: tier=3 recovery log for e2e-tenant-agg
# Use since=30 to focus on recent logs and avoid matching any pre-breach tier-3 lines.
# ---------------------------------------------------------------------------

log_info "ADV-02: Polling for e2e-tenant-agg tier=3 recovery log (timeout 90s, since 30s)..."
if poll_until_log 90 5 "e2e-tenant-agg.*tier=3" 30; then
    record_pass "ADV-02: Recovery tier=3 after single in-range sample" "log=tier3_recovery_found"
else
    record_fail "ADV-02: Recovery tier=3 after single in-range sample" "tier3 log not found within 90s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 37d: counter delta == 0 during recovery window
# After tier-3 confirmed, snapshot counter. Delta must be 0 — no commands during recovery.
# A delta > 0 would mean tier-4 fired again during recovery (partial violation should not fire).
# ---------------------------------------------------------------------------

RECOVERY_AFTER=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
RECOVERY_DELTA=$((RECOVERY_AFTER - RECOVERY_BASELINE))
log_info "ADV-02: Recovery counter delta: recovery_baseline=${RECOVERY_BASELINE} recovery_after=${RECOVERY_AFTER} recovery_delta=${RECOVERY_DELTA}"

if [ "$RECOVERY_DELTA" -eq 0 ]; then
    record_pass "ADV-02: No commands sent during recovery" \
        "recovery_delta=0 baseline=${RECOVERY_BASELINE} after=${RECOVERY_AFTER}"
else
    record_fail "ADV-02: No commands sent during recovery" \
        "recovery_delta=${RECOVERY_DELTA} expected 0 — commands fired during recovery window"
fi

# ---------------------------------------------------------------------------
# Cleanup: reset simulator, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "ADV-02: Resetting simulator scenario..."
reset_scenario

log_info "ADV-02: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "ADV-02: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "ADV-02: Original tenant ConfigMap snapshot not found — skipping restore"
fi
