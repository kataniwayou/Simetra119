# Scenario 42: SNS-02 Stale to Commands -- stale OIDs cause tier=1 skip to tier=4 command dispatch
# Uses tenant-cfg05-four-tenant-snapshot.yaml (G1-T1: IntervalSeconds=1, GraceMultiplier=2.0)
#
# Staleness threshold = IntervalSeconds * GraceMultiplier = 1 * 2.0 = 2s from newest sample.
# ReadinessGrace = TimeSeriesSize * IntervalSeconds * GraceMultiplier = 3 * 1 * 2.0 = 6s.
#
# Sequence:
#   1. Apply fixture + wait for reload
#   2. Prime T1 with valid values to populate fresh timestamps + pass readiness grace
#   3. Sleep 8s (grace=6s + 2s margin) so T1 holders are ready
#   4. Capture sent counter baseline BEFORE switching to stale
#   5. Switch T1 OIDs to stale (NoSuchInstance via sim_set_oid_stale)
#   6. Sleep 5s for staleness age-out (newest sample >2s old after 3-4s; 5s for margin)
#   7. Poll for tier=1 stale log
#   8. Poll for tier=4 commands enqueued log (stale skips to commands)
#   9. Poll for sent counter increment (command dispatch confirmed)
#
# Sub-assertions:
#   42a: tier=1 stale log with G1-T1 scope
#   42b: tier=4 commands enqueued log with G1-T1 scope
#   42c: snmp_command_dispatched_total counter increment

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply 4-tenant snapshot fixture
# ---------------------------------------------------------------------------

log_info "SNS-02: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "SNS-02: Applying tenant-cfg05-four-tenant-snapshot.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg05-four-tenant-snapshot.yaml" > /dev/null 2>&1 || true

log_info "SNS-02: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "SNS-02: Tenant vector reload confirmed"
else
    log_warn "SNS-02: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime T1 with valid data so holders become ready and ages of data are known
# T1 OIDs: 4.1 = evaluate (Min:10), 4.2 = res1 (Min:1), 4.3 = res2 (Min:1)
# ---------------------------------------------------------------------------

log_info "SNS-02: Priming T1 with in-range values to populate fresh timestamps..."
sim_set_oid "4.1" "10"   # T1 eval in-range (>= Min:10)
sim_set_oid "4.2" "1"    # T1 res1 in-range (>= Min:1)
sim_set_oid "4.3" "1"    # T1 res2 in-range (>= Min:1)

log_info "SNS-02: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Baseline capture BEFORE stale switch
# Counter must be snapshotted before stale OID changes so delta proves only
# post-stale dispatches (not any commands from the priming phase).
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
log_info "SNS-02: Baseline sent=${BEFORE_SENT}"

# ---------------------------------------------------------------------------
# Switch T1 OIDs to stale (NoSuchInstance)
# ---------------------------------------------------------------------------

log_info "SNS-02: Switching T1 OIDs to stale..."
sim_set_oid_stale "4.1"
sim_set_oid_stale "4.2"
sim_set_oid_stale "4.3"

# ---------------------------------------------------------------------------
# Wait for staleness age-out: newest sample must be >2s old.
# Data was just refreshed at priming. After ~3s the newest sample is >2s old.
# Sleep 5s for margin.
# ---------------------------------------------------------------------------

log_info "SNS-02: Waiting 5s for staleness age-out..."
sleep 5

# ---------------------------------------------------------------------------
# Sub-scenario 42a: tier=1 stale log
# SnapshotJob detects HasStaleness=true and logs "tier=1 stale"
# ---------------------------------------------------------------------------

log_info "SNS-02: Polling for tier=1 stale log (30s timeout)..."
if poll_until_log 30 1 "e2e-tenant-G1-T1.*tier=1 stale" 15; then
    record_pass "SNS-02A: G1-T1 tier=1 stale detected" "log=tier1_stale_found"
else
    record_fail "SNS-02A: G1-T1 tier=1 stale detected" "tier=1 stale log for G1-T1 not found within 30s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 42b: tier=4 commands enqueued log
# Stale path skips to tier=4 and enqueues commands
# ---------------------------------------------------------------------------

log_info "SNS-02: Polling for tier=4 commands enqueued log (30s timeout)..."
if poll_until_log 30 1 "e2e-tenant-G1-T1.*tier=4 — commands enqueued\|e2e-tenant-G1-T1.*tier=4 -- commands enqueued" 15; then
    record_pass "SNS-02B: G1-T1 tier=4 commands enqueued after stale" "log=tier4_commands_found"
else
    record_fail "SNS-02B: G1-T1 tier=4 commands enqueued after stale" "tier=4 commands enqueued log not found within 30s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 42c: snmp_command_dispatched_total counter increment
# Proves command-holder was evaluated and dispatched (not skipped) despite staleness
# ---------------------------------------------------------------------------

log_info "SNS-02: Polling for sent counter increment (30s timeout)..."
if poll_until 30 2 "snmp_command_dispatched_total" 'device_name="E2E-SIM"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "SNS-02: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_pass "SNS-02C: Sent counter incremented after stale switch" "sent_delta=${DELTA_SENT}"
else
    AFTER_SENT=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "SNS-02: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_fail "SNS-02C: Sent counter incremented after stale switch" "sent_delta=${DELTA_SENT} expected > 0 after 30s polling"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "SNS-02: Clearing OID overrides..."
reset_oid_overrides

log_info "SNS-02: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "SNS-02: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "SNS-02: Original tenant ConfigMap snapshot not found — skipping restore"
fi
