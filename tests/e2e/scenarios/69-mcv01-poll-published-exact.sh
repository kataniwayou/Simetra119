# Scenario 69: MCV-01 — poll published increments exactly per OID count
# E2E-SIM poll group 2: 9 OIDs at 1s interval, all mapped, no aggregates
# With 3 replicas polling independently: minimum delta = 9 per cycle per replica
SCENARIO_NAME="MCV-01: poll published increments by at least 9 OIDs per cycle (E2E-SIM group 2)"
METRIC="snmp_event_published_total"
FILTER='device_name="E2E-SIM"'

BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
# Wait for at least one poll cycle to complete and export (1s poll + up to 20s OTel lag)
poll_until 45 "$POLL_INTERVAL" "$METRIC" "$FILTER" "$BEFORE" || true
AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE="before=$BEFORE after=$AFTER delta=$DELTA (expect >= 9: 9 OIDs * at least 1 replica * at least 1 cycle)"
# E2E-SIM has 4 poll groups across all replicas; group 2 alone contributes 9 per replica per cycle.
# Minimum observable delta = 9 (one replica, one cycle of group 2 only).
# In practice delta is much higher (all groups, all replicas, multiple cycles during OTel lag window).
assert_delta_ge "$DELTA" 9 "$SCENARIO_NAME" "$EVIDENCE"
