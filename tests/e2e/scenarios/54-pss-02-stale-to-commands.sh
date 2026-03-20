# Scenario 54: PSS-02 Stale to Commands -- stale OIDs cause tier=1 skip to tier=4 command dispatch
# Uses tenant-cfg06-pss-single.yaml (e2e-pss-tenant: IntervalSeconds=1, GraceMultiplier=2.0)
#
# Staleness threshold = IntervalSeconds * GraceMultiplier = 1 * 2.0 = 2s from newest sample.
# ReadinessGrace = TimeSeriesSize * IntervalSeconds * GraceMultiplier = 3 * 1 * 2.0 = 6s.
#
# Sequence:
#   1. Apply fixture + wait for reload
#   2. Prime T2 OIDs with valid values to populate fresh timestamps + pass readiness grace
#   3. Sleep 8s (grace=6s + 2s margin) so holders are ready
#   4. Capture sent counter baseline BEFORE switching to stale
#   5. Switch T2 OIDs to stale (NoSuchInstance via sim_set_oid_stale)
#   6. Sleep 5s for staleness age-out (newest sample >2s old after 3-4s; 5s for margin)
#   7. Poll for tier=1 stale log
#   8. Poll for tier=4 commands enqueued log (stale skips to commands)
#   9. Poll for sent counter increment (command dispatch confirmed)
#
# Sub-assertions:
#   54a: tier=1 stale log with e2e-pss-tenant scope
#   54b: tier=4 commands enqueued log with e2e-pss-tenant scope
#   54c: snmp_command_sent_total counter increment

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply single-tenant PSS fixture
# ---------------------------------------------------------------------------

log_info "PSS-02: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "PSS-02: Applying tenant-cfg06-pss-single.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-single.yaml" > /dev/null 2>&1 || true

log_info "PSS-02: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "PSS-02: Tenant vector reload confirmed"
else
    log_warn "PSS-02: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime T2 OIDs with valid data so holders become ready
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# ---------------------------------------------------------------------------

log_info "PSS-02: Priming T2 OIDs with in-range values to populate fresh timestamps..."
sim_set_oid "5.1" "10"   # T2 eval in-range (>= Min:10)
sim_set_oid "5.2" "1"    # T2 res1 in-range (>= Min:1)
sim_set_oid "5.3" "1"    # T2 res2 in-range (>= Min:1)

log_info "PSS-02: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Baseline capture BEFORE stale switch
# Counter must be snapshotted before stale OID changes so delta proves only
# post-stale dispatches (not any commands from the priming phase).
# ---------------------------------------------------------------------------

BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
log_info "PSS-02: Baseline sent=${BEFORE_SENT}"

# ---------------------------------------------------------------------------
# Switch T2 OIDs to stale (NoSuchInstance)
# ---------------------------------------------------------------------------

log_info "PSS-02: Switching T2 OIDs to stale..."
sim_set_oid_stale "5.1"
sim_set_oid_stale "5.2"
sim_set_oid_stale "5.3"

# ---------------------------------------------------------------------------
# Wait for staleness age-out: newest sample must be >2s old.
# Data was just refreshed at priming. After ~3s the newest sample is >2s old.
# Sleep 5s for margin.
# ---------------------------------------------------------------------------

log_info "PSS-02: Waiting 5s for staleness age-out..."
sleep 5

# ---------------------------------------------------------------------------
# Sub-scenario 54a: tier=1 stale log
# SnapshotJob detects HasStaleness=true and logs "tier=1 stale"
# ---------------------------------------------------------------------------

log_info "PSS-02: Polling for tier=1 stale log (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-tenant.*tier=1 stale" 15; then
    record_pass "PSS-02A: e2e-pss-tenant tier=1 stale detected" "log=tier1_stale_found"
else
    record_fail "PSS-02A: e2e-pss-tenant tier=1 stale detected" "tier=1 stale log for e2e-pss-tenant not found within 30s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 54b: tier=4 commands enqueued log
# Stale path skips to tier=4 and enqueues commands
# ---------------------------------------------------------------------------

log_info "PSS-02: Polling for tier=4 commands enqueued log (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-tenant.*tier=4 — commands enqueued\|e2e-pss-tenant.*tier=4 -- commands enqueued" 15; then
    record_pass "PSS-02B: e2e-pss-tenant tier=4 commands enqueued after stale" "log=tier4_commands_found"
else
    record_fail "PSS-02B: e2e-pss-tenant tier=4 commands enqueued after stale" "tier=4 commands enqueued log not found within 30s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 54c: snmp_command_sent_total counter increment
# Proves command-holder was evaluated and dispatched (not skipped) despite staleness
# ---------------------------------------------------------------------------

log_info "PSS-02: Polling for sent counter increment (30s timeout)..."
if poll_until 30 2 "snmp_command_sent_total" 'device_name="E2E-SIM"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "PSS-02: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_pass "PSS-02C: Sent counter incremented after stale switch" "sent_delta=${DELTA_SENT}"
else
    AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "PSS-02: After: sent=${AFTER_SENT} delta_sent=${DELTA_SENT}"
    record_fail "PSS-02C: Sent counter incremented after stale switch" "sent_delta=${DELTA_SENT} expected > 0 after 30s polling"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "PSS-02: Clearing OID overrides..."
reset_oid_overrides

log_info "PSS-02: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "PSS-02: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "PSS-02: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
