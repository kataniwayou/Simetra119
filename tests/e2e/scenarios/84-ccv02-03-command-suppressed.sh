# Scenario 84: CCV-02/03 Suppression Window -- suppressed within window, dispatched unchanged
# Uses tenant-cfg06-pss-suppression.yaml (e2e-pss-tenant-supp: SuppressionWindowSeconds=30)
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
#
# Two sequential assertion windows:
#   Window 1: first tier=4 cycle dispatches the command (dispatched increments)
#   Window 2: second cycle fires within 30s window -- command is suppressed (suppressed increments),
#             but dispatched ALSO fires on every tier=4 enqueue
#             (both dispatched and suppressed increment simultaneously)
#
# Counter labels:
#   snmp_command_dispatched_total:  device_name="e2e-pss-tenant-supp"
#   snmp_command_suppressed_total:  device_name="e2e-pss-tenant-supp"
#
# Sub-assertions:
#   84a: CCV-02A: command.dispatched increments on first tier=4 (Window 1)
#   84b: CCV-02B: command.suppressed increments within suppression window (Window 2)
#   84c: CCV-03:  command.dispatched AND command.suppressed both fire during suppression (Window 2)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply suppression variant fixture
# ---------------------------------------------------------------------------

log_info "CCV-02/03: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "CCV-02/03: Applying tenant-cfg06-pss-suppression.yaml (SuppressionWindowSeconds=30)..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-suppression.yaml" > /dev/null 2>&1 || true

log_info "CCV-02/03: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "CCV-02/03: Tenant vector reload confirmed"
else
    log_warn "CCV-02/03: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime T2 OIDs with in-range values to pass readiness grace
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
# ---------------------------------------------------------------------------

log_info "CCV-02/03: Priming T2 OIDs with in-range values for readiness grace..."
sim_set_oid "5.1" "10"   # T2 eval in-range (>= Min:10)
sim_set_oid "5.2" "1"    # T2 res1 in-range (>= Min:1)
sim_set_oid "5.3" "1"    # T2 res2 in-range (>= Min:1)

log_info "CCV-02/03: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ===========================================================================
# Window 1: First command -- DISPATCHED
# ===========================================================================

log_info "CCV-02/03 Window 1: Triggering first tier=4 command dispatch..."

BEFORE_SENT_W1=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant-supp"')
log_info "CCV-02/03 W1 baseline dispatched=${BEFORE_SENT_W1}"

# Stimulus: set evaluate to violated (< Min:10)
sim_set_oid "5.1" "0"    # T2 eval violated

# ---------------------------------------------------------------------------
# Sub-assertion 84a: CCV-02A -- dispatched increments on first tier=4 (Window 1)
# ---------------------------------------------------------------------------

log_info "CCV-02/03: Polling for dispatched counter increment (30s timeout)..."
if poll_until 30 2 "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant-supp"' "$BEFORE_SENT_W1"; then
    AFTER_SENT_W1=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant-supp"')
    DELTA_SENT_W1=$((AFTER_SENT_W1 - BEFORE_SENT_W1))
    log_info "CCV-02/03 W1: dispatched=${AFTER_SENT_W1} delta=${DELTA_SENT_W1}"
    record_pass "CCV-02A: command.dispatched increments on first tier=4 (Window 1)" \
        "dispatched_delta=${DELTA_SENT_W1}"
else
    AFTER_SENT_W1=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant-supp"')
    DELTA_SENT_W1=$((AFTER_SENT_W1 - BEFORE_SENT_W1))
    log_info "CCV-02/03 W1: dispatched=${AFTER_SENT_W1} delta=${DELTA_SENT_W1}"
    record_fail "CCV-02A: command.dispatched increments on first tier=4 (Window 1)" \
        "dispatched_delta=${DELTA_SENT_W1} expected > 0 after 30s polling"
fi

# ===========================================================================
# Window 2: Second cycle -- SUPPRESSED (within 30s window)
# SnapshotJob fires again at ~1s interval. Within 30s suppression window,
# command is suppressed (not re-dispatched).
# ===========================================================================

log_info "CCV-02/03 Window 2: Stabilizing counters (15s) before suppression observation..."
sleep 15

BEFORE_SUPP_W2=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-pss-tenant-supp"')
BEFORE_SENT_W2=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant-supp"')
log_info "CCV-02/03 W2 baseline suppressed=${BEFORE_SUPP_W2} dispatched=${BEFORE_SENT_W2}"

# Poll until suppressed counter increments (next SnapshotJob cycle within suppression window)
WINDOW2_SUPP_FOUND=0
if poll_until 30 5 "snmp_command_suppressed_total" 'device_name="e2e-pss-tenant-supp"' "$BEFORE_SUPP_W2"; then
    WINDOW2_SUPP_FOUND=1
fi

AFTER_SUPP_W2=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-pss-tenant-supp"')
DELTA_SUPP_W2=$((AFTER_SUPP_W2 - BEFORE_SUPP_W2))
AFTER_SENT_W2=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant-supp"')
DELTA_SENT_W2=$((AFTER_SENT_W2 - BEFORE_SENT_W2))
log_info "CCV-02/03 W2: suppressed_delta=${DELTA_SUPP_W2} dispatched_delta=${DELTA_SENT_W2}"

# ---------------------------------------------------------------------------
# Sub-assertion 84b: CCV-02B -- suppressed counter incremented (Window 2)
# ---------------------------------------------------------------------------

assert_delta_gt "$DELTA_SUPP_W2" 0 \
    "CCV-02B: command.suppressed increments within suppression window (Window 2)" \
    "$(get_evidence "snmp_command_suppressed_total" 'device_name="e2e-pss-tenant-supp"')"

# ---------------------------------------------------------------------------
# Sub-assertion 84c: CCV-03 -- dispatched AND suppressed both fire during Window 2
# SnapshotJob calls TryWrite (dispatched++) then TrySuppress (suppressed++).
# Both counters increment on every suppressed cycle -- they are NOT mutually exclusive.
# ---------------------------------------------------------------------------

if [ "$DELTA_SENT_W2" -gt 0 ] && [ "$DELTA_SUPP_W2" -gt 0 ]; then
    record_pass "CCV-03: dispatched and suppressed both fire during suppression window (Window 2)" \
        "dispatched_delta=${DELTA_SENT_W2} suppressed_delta=${DELTA_SUPP_W2} (both > 0 proves simultaneous firing)"
else
    record_fail "CCV-03: dispatched and suppressed both fire during suppression window (Window 2)" \
        "dispatched_delta=${DELTA_SENT_W2} suppressed_delta=${DELTA_SUPP_W2} expected both > 0"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "CCV-02/03: Clearing OID overrides..."
reset_oid_overrides

log_info "CCV-02/03: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "CCV-02/03: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "CCV-02/03: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
