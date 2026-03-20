# Scenario 58: PSS-06 Suppression Window -- sent, then suppressed within 30s window
# Uses tenant-cfg06-pss-suppression.yaml (e2e-pss-tenant-supp: SuppressionWindowSeconds=30)
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
#
# Two sequential assertion windows:
#   Window 1: first SnapshotJob cycle sends the command (sent counter increments)
#   Window 2: second cycle fires within 30s window -- command is suppressed
#             (suppressed counter increments, sent does NOT)
#
# NOTE: Window 3 (sent again after expiry) is intentionally omitted.
# STS-04 (scenario 32) already covers the full 3-window suppression lifecycle.
# Skipping Window 3 saves 20+ seconds of dead wait.
#
# Counter labels:
#   snmp_command_sent_total:       device_name="E2E-SIM" (actual device name)
#   snmp_command_suppressed_total: device_name="e2e-pss-tenant-supp" (tenant ID)
#
# Sub-assertions:
#   58a: tier=4 "commands enqueued" log (Window 1)
#   58b: snmp_command_sent_total counter increment (Window 1)
#   58c: snmp_command_suppressed_total counter increment (Window 2)
#   58d: snmp_command_sent_total unchanged during Window 2

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply suppression variant fixture
# ---------------------------------------------------------------------------

log_info "PSS-06: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "PSS-06: Applying tenant-cfg06-pss-suppression.yaml (SuppressionWindowSeconds=30)..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-suppression.yaml" > /dev/null 2>&1 || true

log_info "PSS-06: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "PSS-06: Tenant vector reload confirmed"
else
    log_warn "PSS-06: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime T2 OIDs with in-range values to pass readiness grace
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# ---------------------------------------------------------------------------

log_info "PSS-06: Priming T2 OIDs with in-range values for readiness grace..."
sim_set_oid "5.1" "10"   # T2 eval in-range (>= Min:10)
sim_set_oid "5.2" "1"    # T2 res1 in-range (>= Min:1)
sim_set_oid "5.3" "1"    # T2 res2 in-range (>= Min:1)

log_info "PSS-06: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ===========================================================================
# Window 1: First command -- SENT
# ===========================================================================

log_info "PSS-06 Window 1: Triggering first tier=4 command dispatch..."

BEFORE_SENT_W1=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
log_info "PSS-06 W1 baseline sent=${BEFORE_SENT_W1}"

# Stimulus: set evaluate to violated (< Min:10)
sim_set_oid "5.1" "0"    # T2 eval violated

# ---------------------------------------------------------------------------
# Sub-scenario 58a: tier=4 commands enqueued log (Window 1)
# ---------------------------------------------------------------------------

log_info "PSS-06: Polling for tier=4 commands enqueued log (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-tenant-supp.*tier=4 — commands enqueued\|e2e-pss-tenant-supp.*tier=4 -- commands enqueued" 15; then
    record_pass "PSS-06A: e2e-pss-tenant-supp tier=4 commands enqueued (Window 1)" "log=tier4_commands_found"
else
    record_fail "PSS-06A: e2e-pss-tenant-supp tier=4 commands enqueued (Window 1)" "tier=4 commands enqueued log not found within 30s"
fi

# ---------------------------------------------------------------------------
# Sub-scenario 58b: sent counter incremented (Window 1)
# ---------------------------------------------------------------------------

log_info "PSS-06: Polling for sent counter increment (30s timeout)..."
if poll_until 30 2 "snmp_command_sent_total" 'device_name="E2E-SIM"' "$BEFORE_SENT_W1"; then
    AFTER_SENT_W1=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_SENT_W1=$((AFTER_SENT_W1 - BEFORE_SENT_W1))
    log_info "PSS-06 W1: sent=${AFTER_SENT_W1} delta_sent=${DELTA_SENT_W1}"
    record_pass "PSS-06B: Sent counter incremented (Window 1)" "sent_delta=${DELTA_SENT_W1}"
else
    AFTER_SENT_W1=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_SENT_W1=$((AFTER_SENT_W1 - BEFORE_SENT_W1))
    log_info "PSS-06 W1: sent=${AFTER_SENT_W1} delta_sent=${DELTA_SENT_W1}"
    record_fail "PSS-06B: Sent counter incremented (Window 1)" "sent_delta=${DELTA_SENT_W1} expected > 0 after 30s polling"
fi

# ===========================================================================
# Window 2: Second cycle -- SUPPRESSED (within 30s window)
# SnapshotJob fires again at ~1s interval. Within 30s suppression window,
# command is suppressed (not re-sent).
# ===========================================================================

log_info "PSS-06 Window 2: Watching for suppression within the 30s window..."

BEFORE_SUPP_W2=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-pss-tenant-supp"')
BEFORE_SENT_W2=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
log_info "PSS-06 W2 baseline suppressed=${BEFORE_SUPP_W2} sent=${BEFORE_SENT_W2}"

# Poll until suppressed counter increments (next SnapshotJob cycle)
WINDOW2_SUPP_FOUND=0
if poll_until 30 5 "snmp_command_suppressed_total" 'device_name="e2e-pss-tenant-supp"' "$BEFORE_SUPP_W2"; then
    WINDOW2_SUPP_FOUND=1
fi

AFTER_SUPP_W2=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-pss-tenant-supp"')
DELTA_SUPP_W2=$((AFTER_SUPP_W2 - BEFORE_SUPP_W2))
AFTER_SENT_W2=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
DELTA_SENT_W2=$((AFTER_SENT_W2 - BEFORE_SENT_W2))
log_info "PSS-06 W2: suppressed_delta=${DELTA_SUPP_W2} sent_delta=${DELTA_SENT_W2}"

# ---------------------------------------------------------------------------
# Sub-scenario 58c: suppressed counter incremented (Window 2)
# ---------------------------------------------------------------------------

assert_delta_gt "$DELTA_SUPP_W2" 0 \
    "PSS-06C: Suppressed counter incremented (Window 2)" \
    "$(get_evidence "snmp_command_suppressed_total" 'device_name="e2e-pss-tenant-supp"')"

# ---------------------------------------------------------------------------
# Sub-scenario 58d: sent counter unchanged during Window 2
# Command was suppressed, not re-sent
# ---------------------------------------------------------------------------

if [ "$DELTA_SENT_W2" -eq 0 ]; then
    record_pass "PSS-06D: Sent counter unchanged while suppressed (Window 2)" "sent_delta=${DELTA_SENT_W2} expected=0 suppressed_delta=${DELTA_SUPP_W2}"
else
    record_fail "PSS-06D: Sent counter unchanged while suppressed (Window 2)" "unexpected sent_delta=${DELTA_SENT_W2} expected=0; command was NOT suppressed"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "PSS-06: Clearing OID overrides..."
reset_oid_overrides

log_info "PSS-06: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "PSS-06: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "PSS-06: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
