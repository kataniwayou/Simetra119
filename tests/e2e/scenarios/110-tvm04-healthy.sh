# Scenario 110: TVM-04 Healthy -- tier=3 when all metrics are in-range, P99 histogram present
# Uses tenant-cfg06-pss-single.yaml (e2e-pss-tenant: IntervalSeconds=1, GraceMultiplier=2.0)
# T2 OIDs: 5.1 = evaluate (Min:10), 5.2 = res1 (Min:1), 5.3 = res2 (Min:1)
#
# Healthy path (tier=3) fires when:
#   - Resolved holders are NOT all violated (tier=2 gate passes)
#   - Evaluate holder is NOT violated (>= Min:10)
#   => All metrics in-range: tenant is Healthy, no commands dispatched
#
# After priming (eval=10, res1=1, res2=1), no OID changes are needed.
# The primed values satisfy all tier gates -- tenant evaluates Healthy each cycle.
# tenant_evaluation_state gauge = 1 (Healthy).
#
# v2.5: All metrics recorded as percentage gauges at single exit point after state determined.
# All percentage gauges = 0 on Healthy path (no violations, no dispatch).
#
# METRIC NAME NOTE: ROADMAP uses "tenant_gauge_duration_milliseconds" -- this is a TYPO.
# The correct Prometheus histogram name is tenant_evaluation_duration_milliseconds
# (OTel instrument: tenant.evaluation.duration.milliseconds -> dot-to-underscore conversion).
#
# Sub-assertions:
#   TVM-04A: tenant_evaluation_state gauge == 1 (Healthy)
#   TVM-04B: tenant_evaluation_duration_milliseconds_count delta > 0 (evaluation ran)
#   TVM-04C: tenant_command_dispatched_percent == 0 (Healthy state, no commands)
#   TVM-04D: histogram P99 present and > 0 (full instrumentation pipeline verified)
#   TVM-04E: tenant_metric_stale_percent == 0 (no stale OIDs in Healthy path)
#   TVM-04F: tenant_metric_evaluate_percent == 0 (evaluate OID in-range)

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# ---------------------------------------------------------------------------
# Setup: save current tenants ConfigMap, apply single-tenant PSS fixture
# ---------------------------------------------------------------------------

log_info "TVM-04: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "TVM-04: Applying tenant-cfg06-pss-single.yaml..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-single.yaml" > /dev/null 2>&1 || true

log_info "TVM-04: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "TVM-04: Tenant vector reload confirmed"
else
    log_warn "TVM-04: Tenant vector reload not detected within 60s; proceeding"
fi

# ---------------------------------------------------------------------------
# Prime T2 OIDs with in-range values (tenant primed to healthy state)
# After sleep 8s, tenant is ready -- no further OID changes needed for Healthy path.
# T2 eval=10 >= Min:10 (not violated), res1=1 >= Min:1 (not violated), res2=1 (not violated)
# ---------------------------------------------------------------------------

log_info "TVM-04: Priming T2 OIDs with in-range values (tenant primed to healthy state)..."
sim_set_oid "5.1" "10"   # T2 eval in-range (>= Min:10) -- healthy
sim_set_oid "5.2" "1"    # T2 res1 in-range (>= Min:1)  -- healthy
sim_set_oid "5.3" "1"    # T2 res2 in-range (>= Min:1)  -- healthy

log_info "TVM-04: Waiting 8s for readiness grace (6s grace + 2s margin)..."
sleep 8

# ---------------------------------------------------------------------------
# Wait for Healthy evaluation -- poll for tier=3 log
# No OID changes needed: primed values are already all in-range
# ---------------------------------------------------------------------------

log_info "TVM-04: Polling for tier=3 healthy log (30s timeout)..."
if poll_until_log 30 1 "e2e-pss-tenant.*tier=3" 15; then
    log_info "TVM-04: tier=3 healthy log confirmed for e2e-pss-tenant"
    log_info "TVM-04: Sleeping 15s for Prometheus scrape to propagate gauges..."
    sleep 15
else
    log_warn "TVM-04: tier=3 log not found within 30s; sleeping 15s as fallback"
    sleep 15
fi

# ---------------------------------------------------------------------------
# Snapshot duration baseline AFTER Healthy state confirmed
# (histogram counter is still monotonic -- delta approach valid)
# ---------------------------------------------------------------------------

BEFORE_DURATION=$(snapshot_counter "tenant_evaluation_duration_milliseconds_count" 'tenant_id="e2e-pss-tenant",priority="1"')
log_info "TVM-04: Baseline -- duration_count=${BEFORE_DURATION}"

log_info "TVM-04: Waiting 5s to accumulate duration histogram increments..."
sleep 5

AFTER_DURATION=$(snapshot_counter "tenant_evaluation_duration_milliseconds_count" 'tenant_id="e2e-pss-tenant",priority="1"')
DELTA_DURATION=$((AFTER_DURATION - BEFORE_DURATION))
log_info "TVM-04: After -- duration_count=${AFTER_DURATION} delta=${DELTA_DURATION}"

# ---------------------------------------------------------------------------
# TVM-04A: tenant_evaluation_state gauge == 1 (Healthy)
# ---------------------------------------------------------------------------

STATE=$(query_prometheus 'tenant_evaluation_state{tenant_id="e2e-pss-tenant"}' \
    | jq -r '.data.result[0].value[1] // "-1"' | cut -d. -f1)
log_info "TVM-04A: tenant_evaluation_state=${STATE} (expected 1)"
if [ "$STATE" = "1" ]; then
    record_pass "TVM-04A: tenant_evaluation_state=1 (Healthy) when all metrics in-range" "state=${STATE}"
else
    record_fail "TVM-04A: tenant_evaluation_state=1 (Healthy) when all metrics in-range" "state=${STATE} expected=1"
fi

# ---------------------------------------------------------------------------
# TVM-04B: tenant_evaluation_duration_milliseconds_count delta > 0
# Proves evaluation ran during the Healthy path.
#
# NOTE: tier3_evaluate_total only increments when CountEvaluateViolated > 0,
# i.e. when an evaluate holder IS violated. In the Healthy path the evaluate
# holder is NOT violated, so tier3_evaluate does NOT increment -- asserting
# delta>0 on that counter for a Healthy tenant is incorrect. Instead, we
# assert that the evaluation duration histogram count advanced, which confirms
# evaluation ran regardless of the outcome tier.
# ---------------------------------------------------------------------------

assert_delta_gt "$DELTA_DURATION" 0 \
    "TVM-04B: evaluation_duration_count increments during Healthy path (proves evaluation ran)" \
    "delta=${DELTA_DURATION}"

# ---------------------------------------------------------------------------
# TVM-04C: tenant_command_dispatched_percent == 0
# Healthy state means no tier=4, no evaluate violation, no commands dispatched.
# ---------------------------------------------------------------------------

DISPATCHED_PCT=$(query_prometheus 'tenant_command_dispatched_percent{tenant_id="e2e-pss-tenant"}' \
    | jq -r '.data.result[0].value[1] // "-1"')
log_info "TVM-04C: dispatched_percent=${DISPATCHED_PCT}"
if echo "$DISPATCHED_PCT" | awk '{exit ($1 == 0 || $1 == "0") ? 0 : 1}'; then
    record_pass "TVM-04C: dispatched_percent=0 during Healthy path (no commands)" "dispatched_percent=${DISPATCHED_PCT}"
else
    record_fail "TVM-04C: dispatched_percent=0 during Healthy path (no commands)" "dispatched_percent=${DISPATCHED_PCT} expected=0"
fi

# ---------------------------------------------------------------------------
# TVM-04D: Histogram P99 present and > 0
# Verifies the full instrumentation pipeline: SnapshotJob -> OTel -> Prometheus.
# Uses tenant_evaluation_duration_milliseconds (NOT "tenant_gauge_duration_milliseconds" -- ROADMAP typo).
# P99 is a float; handle NaN and +Inf edge cases.
# ---------------------------------------------------------------------------

P99=$(query_prometheus 'histogram_quantile(0.99, rate(tenant_evaluation_duration_milliseconds_bucket{tenant_id="e2e-pss-tenant"}[5m]))' \
    | jq -r '.data.result[0].value[1] // "0"')
log_info "TVM-04D: histogram P99=${P99}ms"
if [ -n "$P99" ] && [ "$P99" != "0" ] && [ "$P99" != "NaN" ] && [ "$P99" != "+Inf" ]; then
    record_pass "TVM-04D: tenant_evaluation_duration_milliseconds P99 present and > 0" "p99=${P99}ms"
else
    record_fail "TVM-04D: tenant_evaluation_duration_milliseconds P99 present and > 0" "p99=${P99} expected > 0"
fi

# ---------------------------------------------------------------------------
# TVM-04E: tenant_metric_stale_percent == 0
# No stale OIDs in Healthy path (all OIDs recently polled and in-range)
# ---------------------------------------------------------------------------

STALE_PCT=$(query_prometheus 'tenant_metric_stale_percent{tenant_id="e2e-pss-tenant"}' \
    | jq -r '.data.result[0].value[1] // "-1"')
log_info "TVM-04E: stale_percent=${STALE_PCT}"
if echo "$STALE_PCT" | awk '{exit ($1 == 0 || $1 == "0") ? 0 : 1}'; then
    record_pass "TVM-04E: stale_percent=0 during Healthy path (no stale OIDs)" "stale_percent=${STALE_PCT}"
else
    record_fail "TVM-04E: stale_percent=0 during Healthy path (no stale OIDs)" "stale_percent=${STALE_PCT} expected=0"
fi

# ---------------------------------------------------------------------------
# TVM-04F: tenant_metric_evaluate_percent == 0
# Evaluate OID is in-range (value 10 >= Min:10), no evaluate violations
# ---------------------------------------------------------------------------

EVAL_PCT=$(query_prometheus 'tenant_metric_evaluate_percent{tenant_id="e2e-pss-tenant"}' \
    | jq -r '.data.result[0].value[1] // "-1"')
log_info "TVM-04F: evaluate_percent=${EVAL_PCT}"
if echo "$EVAL_PCT" | awk '{exit ($1 == 0 || $1 == "0") ? 0 : 1}'; then
    record_pass "TVM-04F: evaluate_percent=0 during Healthy path (evaluate in-range)" "evaluate_percent=${EVAL_PCT}"
else
    record_fail "TVM-04F: evaluate_percent=0 during Healthy path (evaluate in-range)" "evaluate_percent=${EVAL_PCT} expected=0"
fi

# ---------------------------------------------------------------------------
# Cleanup: clear OID overrides, restore original ConfigMap
# ---------------------------------------------------------------------------

log_info "TVM-04: Clearing OID overrides..."
reset_oid_overrides

log_info "TVM-04: Restoring original tenant ConfigMap..."
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "TVM-04: Failed to restore tenant ConfigMap from snapshot"
else
    log_warn "TVM-04: Original tenant ConfigMap snapshot not found -- skipping restore"
fi
