# Scenario 32: STS-04 Suppression window -- sent, then suppressed, then sent again after expiry
# Uses tenant-cfg01-suppression.yaml with SuppressionWindowSeconds=30
# Three sequential assertion windows:
#   Window 1: first SnapshotJob cycle sends the command (sent counter increments)
#   Window 2: second cycle fires within 30s window -- command is suppressed (suppressed counter increments, sent does NOT)
#   Window 3: after 30s window expires, next cycle sends again (sent counter increments again)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: apply suppression fixture (30s window) and wait for reload
# ---------------------------------------------------------------------------

log_info "STS-04: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "STS-04: Applying tenant-cfg01-suppression.yaml (SuppressionWindowSeconds=30)..."
# IMPORTANT: must use suppression fixture -- default fixture has 10s window < 15s SnapshotJob interval
# which makes it impossible to observe suppression before the window expires
kubectl apply -f "$FIXTURES_DIR/tenant-cfg01-suppression.yaml" > /dev/null 2>&1 || true

log_info "STS-04: Waiting for tenant vector reload..."
poll_until_log 60 5 "Tenant vector reload complete" 30 || \
    log_warn "STS-04: Tenant vector reload not detected within 60s; proceeding"

log_info "STS-04: Setting simulator to command_trigger scenario..."
sim_set_scenario command_trigger

# ---------------------------------------------------------------------------
# Window 1: First command -- SENT
# ---------------------------------------------------------------------------

log_info "STS-04 Window 1: Waiting for first tier=4 command dispatch..."

BEFORE_SENT_W1=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A-supp"')
log_info "STS-04 W1 baseline snmp_command_dispatched_total: ${BEFORE_SENT_W1}"

WINDOW1_LOG_FOUND=0
if poll_until_log 90 5 "tier=4 — commands enqueued" 60; then
    WINDOW1_LOG_FOUND=1
fi

# Sub-scenario 32a: tier=4 log found in Window 1
SCENARIO_NAME="STS-04: Window 1 — tier=4 command dispatch log detected"
if [ "$WINDOW1_LOG_FOUND" -eq 1 ]; then
    record_pass "$SCENARIO_NAME" "log=tier4_found"
else
    record_fail "$SCENARIO_NAME" "tier=4 log not found within 90s"
fi

# Sub-scenario 32b: sent counter incremented in Window 1
# Poll for counter — the SNMP SET round-trip + OTel export + Prometheus scrape takes time.
SCENARIO_NAME="STS-04: Window 1 — command sent counter incremented"
if poll_until 45 5 "snmp_command_dispatched_total" 'device_name="e2e-tenant-A-supp"' "$BEFORE_SENT_W1"; then
    AFTER_SENT_W1=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A-supp"')
    DELTA_SENT_W1=$(( AFTER_SENT_W1 - BEFORE_SENT_W1 ))
    log_info "STS-04 W1 sent delta: ${DELTA_SENT_W1} (before=${BEFORE_SENT_W1} after=${AFTER_SENT_W1})"
    record_pass "$SCENARIO_NAME" "sent_delta=${DELTA_SENT_W1}"
else
    AFTER_SENT_W1=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A-supp"')
    DELTA_SENT_W1=$(( AFTER_SENT_W1 - BEFORE_SENT_W1 ))
    log_info "STS-04 W1 sent delta: ${DELTA_SENT_W1} (before=${BEFORE_SENT_W1} after=${AFTER_SENT_W1})"
    record_fail "$SCENARIO_NAME" "sent_delta=${DELTA_SENT_W1} after 45s polling"
fi

# ---------------------------------------------------------------------------
# Window 2: Second cycle -- SUPPRESSED (within 30s window)
# First command was at ~T=0. SnapshotJob fires again at ~T=15s.
# 15s < 30s window -- suppression cache blocks the command.
# ---------------------------------------------------------------------------

log_info "STS-04 Window 2: Watching for suppression within the 30s window..."

BEFORE_SUPP_W2=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-tenant-A-supp"')
BEFORE_SENT_W2=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A-supp"')
log_info "STS-04 W2 baseline suppressed: ${BEFORE_SUPP_W2}  sent: ${BEFORE_SENT_W2}"

# Poll until suppressed counter increments (next SnapshotJob cycle, ~15s)
WINDOW2_SUPP_FOUND=0
if poll_until 30 5 "snmp_command_suppressed_total" 'device_name="e2e-tenant-A-supp"' "$BEFORE_SUPP_W2"; then
    WINDOW2_SUPP_FOUND=1
fi

AFTER_SUPP_W2=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-tenant-A-supp"')
DELTA_SUPP_W2=$(( AFTER_SUPP_W2 - BEFORE_SUPP_W2 ))
AFTER_SENT_W2=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A-supp"')
DELTA_SENT_W2=$(( AFTER_SENT_W2 - BEFORE_SENT_W2 ))
log_info "STS-04 W2 suppressed delta: ${DELTA_SUPP_W2}  sent delta: ${DELTA_SENT_W2}"

# Sub-scenario 32c: suppressed counter incremented in Window 2
SCENARIO_NAME="STS-04: Window 2 — command suppressed within 30s window"
assert_delta_gt "$DELTA_SUPP_W2" 0 "$SCENARIO_NAME" \
    "$(get_evidence "snmp_command_suppressed_total" 'device_name="e2e-tenant-A-supp"')"

# Sub-scenario 32d: sent counter did NOT increment during Window 2
SCENARIO_NAME="STS-04: Window 2 — sent counter unchanged while suppressed"
if [ "$DELTA_SENT_W2" -eq 0 ]; then
    record_pass "$SCENARIO_NAME" "sent_delta=${DELTA_SENT_W2} expected=0 suppressed_delta=${DELTA_SUPP_W2}"
else
    record_fail "$SCENARIO_NAME" "unexpected sent_delta=${DELTA_SENT_W2} expected=0; command was NOT suppressed"
fi

# ---------------------------------------------------------------------------
# Window 3: Suppression window expires -- sent AGAIN
# Sleep 20s to guarantee the 30s window has fully expired from Window 1.
# This is the only fixed sleep in the Phase 53 suite -- unavoidable because
# there is no log event emitted when a suppression window expires.
# ---------------------------------------------------------------------------

log_info "STS-04 Window 3: Waiting 20s for suppression window to expire..."
sleep 20

BEFORE_SENT_W3=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A-supp"')
log_info "STS-04 W3 baseline sent: ${BEFORE_SENT_W3}"

WINDOW3_LOG_FOUND=0
if poll_until_log 60 5 "tier=4 — commands enqueued" 30; then
    WINDOW3_LOG_FOUND=1
fi

# Sub-scenario 32e: tier=4 log found again after window expiry
SCENARIO_NAME="STS-04: Window 3 — tier=4 log detected after suppression window expired"
if [ "$WINDOW3_LOG_FOUND" -eq 1 ]; then
    record_pass "$SCENARIO_NAME" "log=tier4_found_after_expiry"
else
    record_fail "$SCENARIO_NAME" "tier=4 log not found within 60s after window expiry"
fi

# Sub-scenario 32f: sent counter incremented again after window expiry
# Poll for counter — same timing as W1.
SCENARIO_NAME="STS-04: Window 3 — command sent again after suppression window expired"
if poll_until 45 5 "snmp_command_dispatched_total" 'device_name="e2e-tenant-A-supp"' "$BEFORE_SENT_W3"; then
    AFTER_SENT_W3=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A-supp"')
    DELTA_SENT_W3=$(( AFTER_SENT_W3 - BEFORE_SENT_W3 ))
    log_info "STS-04 W3 sent delta: ${DELTA_SENT_W3} (before=${BEFORE_SENT_W3} after=${AFTER_SENT_W3})"
    record_pass "$SCENARIO_NAME" "sent_delta=${DELTA_SENT_W3}"
else
    AFTER_SENT_W3=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-tenant-A-supp"')
    DELTA_SENT_W3=$(( AFTER_SENT_W3 - BEFORE_SENT_W3 ))
    log_info "STS-04 W3 sent delta: ${DELTA_SENT_W3} (before=${BEFORE_SENT_W3} after=${AFTER_SENT_W3})"
    record_fail "$SCENARIO_NAME" "sent_delta=${DELTA_SENT_W3} after 45s polling"
fi

# ---------------------------------------------------------------------------
# Cleanup
# ---------------------------------------------------------------------------

log_info "STS-04: Resetting simulator scenario..."
reset_scenario

log_info "STS-04: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "STS-04: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "STS-04: Original tenant ConfigMap snapshot not found; skipping restore"
fi
