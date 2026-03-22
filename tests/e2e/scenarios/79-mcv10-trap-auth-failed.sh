# Scenario 79: MCV-10 — trap.auth_failed increments for bad-community traps
# SnmpTrapListenerService fires IncrementTrapAuthFailed("unknown") when
# CommunityStringHelper.TryExtractDeviceName returns false (no "Simetra." prefix).
# E2E-SIM sends bad traps every 45s with community="BadCommunity".
# Empty filter (sum() captures device_name="unknown") matches scenario 05 exactly.
SCENARIO_NAME="MCV-10: trap.auth_failed increments for bad-community traps"
METRIC="snmp_trap_auth_failed_total"
FILTER=""

BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
poll_until 75 "$POLL_INTERVAL" "$METRIC" "$FILTER" "$BEFORE" || true
AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE="before=$BEFORE after=$AFTER delta=$DELTA (bad traps every 45s community=BadCommunity)"
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "$EVIDENCE"
