# Phase 55: Advanced Scenarios - Research

**Researched:** 2026-03-17
**Domain:** E2E scenario scripting, aggregate metric pipeline, depth-3 time series evaluation
**Confidence:** HIGH

## Summary

Phase 55 delivers two bash scenario scripts (ADV-01 and ADV-02) that validate the remaining untested paths in the tenant evaluation pipeline. All infrastructure required is already deployed — the aggregate OID map entries, the devices configmap poll group with `AggregatedMetricName`, and the `tenant-cfg04-aggregate.yaml` fixture already exist in the repository. The simulator already exposes `.4.5` and `.4.6` OIDs with default value 0. No source code changes, no new fixtures, and no new simulator changes are needed. The only new work is two scenario scripts, one new simulator scenario in `e2e_simulator.py`, and a one-line patch to `report.sh`.

**Primary recommendation:** Add a single "agg_breach" scenario to the simulator (`.4.5=50, .4.6=50` → sum 100 > Max:80), write two scenario scripts (36-adv-01 and 37-adv-02), extend report.sh to `|28|37|`.

## Standard Stack

All tooling is the existing e2e test stack already in use for phases 53-54.

### Core
| Tool | Version | Purpose | Notes |
|------|---------|---------|-------|
| bash scenario scripts | — | Scenario execution | Sourced by run-all.sh; follow 35-mts-02 patterns |
| lib/sim.sh | — | `sim_set_scenario`, `reset_scenario`, `poll_until_log` | Already in place |
| lib/prometheus.sh | — | `snapshot_counter`, `query_prometheus`, `get_evidence` | Already in place |
| lib/common.sh | — | `record_pass`, `record_fail`, `assert_delta_gt` | Already in place |
| lib/kubectl.sh | — | `save_configmap`, `restore_configmap` | Already in place |
| lib/report.sh | — | `generate_report` with `_REPORT_CATEGORIES` array | Needs range extension |

### Supporting
| Asset | Where | What |
|-------|-------|------|
| `tenant-cfg04-aggregate.yaml` | `tests/e2e/fixtures/` | Already present; e2e-tenant-agg, Priority 1, evaluate=e2e_total_util, TimeSeriesSize=3, Max:80 |
| `simetra-oid-metric-map.yaml` | `deploy/k8s/snmp-collector/` | Already has .4.5 → e2e_agg_source_a, .4.6 → e2e_agg_source_b |
| `simetra-devices.yaml` | `deploy/k8s/snmp-collector/` | Already has e2e-sim aggregate poll group with AggregatedMetricName=e2e_total_util, Aggregator=sum |
| `e2e_simulator.py` | `simulators/e2e-sim/` | Needs one new scenario: "agg_breach" |

**Nothing needs to be installed.** All dependencies exist.

## Architecture Patterns

### Scenario File Numbering
The last existing scenario is `35-mts-02-advance-gate.sh`. ADV-01 maps to `36-adv-01-aggregate-evaluate.sh` and ADV-02 maps to `37-adv-02-depth3-allsamples.sh`.

### Scenario Script Structure (follows 35-mts-02 pattern exactly)
```
# Scenario N: ADV-XX description
# ...timing model comment...

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# Setup: save ConfigMap, apply fixture, wait for reload
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true
kubectl apply -f "$FIXTURES_DIR/tenant-cfg04-aggregate.yaml" > /dev/null 2>&1 || true
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "ADV-01: Tenant vector reload confirmed"
else
    log_warn "ADV-01: Tenant vector reload log not found within 60s — proceeding anyway"
fi

# Body: set scenario, baseline, assert, sub-scenarios

# Cleanup: reset simulator, restore ConfigMap
reset_scenario
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "ADV-01: Failed to restore tenant ConfigMap from snapshot"
fi
```

### report.sh Category Extension
Current:
```bash
"Snapshot Evaluation|28|34"
```
Required change (extend to cover indices 28-36, i.e., scenarios 29-37):
```bash
"Snapshot Evaluation|28|36"
```
The index is 0-based into the `SCENARIO_RESULTS` array. Scenario 37 (1-based) = index 36 (0-based). The array is clamped if fewer results exist, so extending the end index is safe.

### Prometheus source=synthetic Query Pattern
The `snmp_gauge` metric carries a `source` label. The `SnmpSource.Synthetic` enum renders to `"synthetic"` via `.ToString().ToLowerInvariant()` (confirmed in `OtelMetricHandler.cs` line 45). To verify a synthetic metric reached Prometheus:

```bash
query_prometheus 'snmp_gauge{resolved_name="e2e_total_util",source="synthetic"}'
```

The result's `.data.result` array will be non-empty if at least one scrape has occurred. Use `jq -r '.data.result | length'` to check count > 0.

A helper for this (reusing existing `query_prometheus`):
```bash
RESULT=$(query_prometheus 'snmp_gauge{resolved_name="e2e_total_util",source="synthetic"}')
COUNT=$(echo "$RESULT" | jq -r '.data.result | length')
if [ "$COUNT" -gt 0 ]; then
    record_pass "ADV-01: e2e_total_util has source=synthetic in Prometheus" \
        "count=${COUNT} query=snmp_gauge{resolved_name=e2e_total_util,source=synthetic}"
else
    record_fail "ADV-01: e2e_total_util has source=synthetic in Prometheus" \
        "count=0 — synthetic metric not found in Prometheus"
fi
```

Note: `assert_exists` in `common.sh` queries by `{__name__="metricname"}` which does not filter by label, so it cannot distinguish `source=synthetic`. Use `query_prometheus` directly with the label filter.

### Anti-Patterns to Avoid
- **Using assert_exists for source=synthetic check:** `assert_exists` does not accept label filters. Use `query_prometheus` with `jq` directly.
- **Querying Prometheus before OTel export delay:** The aggregate is computed and dispatched through MediatR but OTel export and Prometheus scrape interval introduce lag (~15-30s). Use `poll_until_exists`-style loop or wait at least 30s before checking Prometheus.
- **Not waiting for tenant vector reload:** The aggregate fixture configures a different tenant ID than the STS fixture. Always wait for "Tenant vector reload complete" before asserting.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Prometheus label query | Custom curl + grep | `query_prometheus` + `jq` | Already in prometheus.sh |
| ConfigMap save/restore | kubectl commands inline | `save_configmap` / `restore_configmap` | Already in kubectl.sh |
| Log pattern polling | sleep loops | `poll_until_log` | Already in sim.sh |
| Counter delta | arithmetic inline | `snapshot_counter` before/after + bash arithmetic | Already the established pattern |

## Common Pitfalls

### Pitfall 1: ADV-01 — Aggregate Scenario Does Not Exist in Simulator
**What goes wrong:** `sim_set_scenario agg_breach` returns HTTP 404 because `agg_breach` is not in the `SCENARIOS` dict.
**Why it happens:** All existing scenarios set `.4.5=0` and `.4.6=0` (default baseline). Sum is 0 < Max:80 — no breach. No existing scenario covers the aggregate path.
**How to avoid:** Add `"agg_breach": _make_scenario({f"{E2E_PREFIX}.4.5": 50, f"{E2E_PREFIX}.4.6": 50})` to the `SCENARIOS` dict in `e2e_simulator.py`. Sum = 100 > 80 → violated. Values 50+50 keep each source clearly below any individual threshold while the sum violates Max:80.
**Warning signs:** `log_error "Unexpected HTTP 404 setting scenario agg_breach"` in test output.

### Pitfall 2: ADV-01 — tier-4 Never Fires Because TimeSeriesSize=3 Requires 3 Filled Cycles
**What goes wrong:** Script asserts tier-4 log within 30s but only one poll cycle has run.
**Why it happens:** `tenant-cfg04-aggregate.yaml` has `TimeSeriesSize: 3`. All 3 slots must be violated before `AreAllEvaluateViolated` returns true. With 10s poll interval + SnapshotJob at 15s, need ~45s minimum (3 polls) plus SnapshotJob cycle timing.
**How to avoid:** Use timeout of 90s for tier-4 log assertion (matching STS-02 and MTS-01 pattern for TimeSeriesSize=3 configs).
**Warning signs:** `log=tier4_not_found — poll_until_log timed out after 30s`.

### Pitfall 3: ADV-01 — source=synthetic Not Yet Visible When Queried
**What goes wrong:** Prometheus query for `snmp_gauge{source="synthetic"}` returns empty immediately after scenario switch.
**Why it happens:** The aggregate metric flows: SNMP poll → MediatR → OTel SDK → OTel Collector → Prometheus scrape. This pipeline has delay. Prometheus scrape interval is typically 15s; OTel export interval adds more.
**How to avoid:** Query Prometheus for source=synthetic AFTER the tier-4 log assertion has already passed (ensuring at least one aggregate dispatch has occurred) and add a brief wait (e.g., 30s after tier-4 confirmed). Alternatively, use a short poll loop similar to `poll_until_exists`.
**Warning signs:** count=0 even though tier-4 fired.

### Pitfall 4: ADV-02 — Recovery Phase Fires Too Early
**What goes wrong:** tier-3 healthy log appears before clearing one sample back in-range, or the assertion window captures a log from the breach phase.
**Why it happens:** After switching to healthy/threshold_clear, the existing series still has 3 violated samples. The all-samples check will return false (healthy) only after at least one new in-range sample overwrites a slot.
**How to avoid:** After switching to recovery scenario, wait 15s (one poll + margin) before polling for tier-3 healthy log. Use `since=30s` for the log grep to focus on recent logs only.
**Warning signs:** tier-3 log detected but it's a pre-breach log from before the agg_breach scenario switch.

### Pitfall 5: ADV-02 — Partial Series Violation Test Misidentified
**What goes wrong:** The scenario intends to prove that a single in-range sample prevents tier-4 from firing, but the assertion is ambiguous.
**Why it happens:** After recovery (switching to healthy after breach), the series may have 2 violated + 1 in-range. The existing `AreAllEvaluateViolated` logic returns false immediately when any in-range sample is found. The recovery assertion (tier-3 + counter delta == 0) implicitly proves the partial violation case.
**How to avoid:** ADV-02 recovery assertions (tier-3 log + zero counter delta) already prove partial series does not fire. No separate sub-scenario needed. The CONTEXT.md success criterion 3 ("partial series violation does not fire") is proven by the same tier-3 + delta=0 assertion in recovery.

### Pitfall 6: report.sh Range Not Extended
**What goes wrong:** ADV-01 (index 35) and ADV-02 (index 36) results appear in the report without a category header, or are silently omitted.
**Why it happens:** `_REPORT_CATEGORIES` currently ends at `"Snapshot Evaluation|28|34"`. Indices 35 and 36 are outside every category range and are skipped.
**How to avoid:** Change `"Snapshot Evaluation|28|34"` to `"Snapshot Evaluation|28|36"` in `lib/report.sh`.
**Warning signs:** Report has no category section covering scenarios 36-37.

## Code Examples

### Adding agg_breach scenario to e2e_simulator.py
```python
# Source: simulators/e2e-sim/e2e_simulator.py — SCENARIOS dict
"agg_breach": _make_scenario({
    f"{E2E_PREFIX}.4.5": 50,   # e2e_agg_source_a = 50
    f"{E2E_PREFIX}.4.6": 50,   # e2e_agg_source_b = 50
    # sum = 100 > Max:80 → e2e_total_util violated
    # .4.2 and .4.3 stay at 0 (Resolved metrics: Min:1.0 → NOT violated → tier-2 passes)
}),
```

Wait — the tenant-cfg04-aggregate.yaml Resolved metrics (`e2e_channel_state` with Min:1.0 and `e2e_bypass_status` with Min:1.0) must NOT be all-violated, or we hit tier-2 (ConfirmedBad) and never reach tier-4. With `.4.2=0` (< Min:1.0 = violated) and `.4.3=0` (< Min:1.0 = violated), both Resolved slots are violated → tier-2 fires (ConfirmedBad), not tier-4.

**Corrected agg_breach scenario:** must set `.4.2` and `.4.3` to in-range values:
```python
"agg_breach": _make_scenario({
    f"{E2E_PREFIX}.4.2": 2,    # e2e_channel_state = 2 (>= Min:1.0 → in-range)
    f"{E2E_PREFIX}.4.3": 2,    # e2e_bypass_status = 2 (>= Min:1.0 → in-range)
    f"{E2E_PREFIX}.4.5": 50,   # e2e_agg_source_a = 50
    f"{E2E_PREFIX}.4.6": 50,   # e2e_agg_source_b = 50
}),
```
This mirrors the `command_trigger` pattern (which already sets `.4.2=2, .4.3=2` to put Resolved in-range) plus the aggregate source values.

**Recovery scenario (for ADV-02):** switch back to `healthy` after breach — `.4.1=5 (e2e_port_utilization)`, but note the Evaluate metric for e2e-tenant-agg is `e2e_total_util` (the aggregate), not `e2e_port_utilization`. To recover the aggregate, set `.4.5=0` and `.4.6=0` (sum = 0 < Max:80 → in-range). Use a new "agg_healthy" scenario or simply call `sim_set_scenario healthy` which sets all to baseline (`.4.5=0, .4.6=0`). However the `healthy` scenario sets `.4.1=5, .4.2=2, .4.3=2` — the aggregate sources stay at 0 (default baseline). So `healthy` already works for recovery: sum(0,0) = 0 < 80 → e2e_total_util not violated → tier-3.

### Querying source=synthetic in Prometheus
```bash
# Source: lib/prometheus.sh query_prometheus pattern
SYNTHETIC_RESULT=$(query_prometheus 'snmp_gauge{resolved_name="e2e_total_util",source="synthetic"}')
SYNTHETIC_COUNT=$(echo "$SYNTHETIC_RESULT" | jq -r '.data.result | length')
if [ "$SYNTHETIC_COUNT" -gt 0 ]; then
    record_pass "ADV-01: e2e_total_util source=synthetic visible in Prometheus" \
        "count=${SYNTHETIC_COUNT}"
else
    record_fail "ADV-01: e2e_total_util source=synthetic visible in Prometheus" \
        "count=0 — metric not found; OTel export delay or aggregate not computing"
fi
```

### ADV-02 Timing Model (depth-3 fill)
```
Fixture: tenant-cfg04-aggregate.yaml — TimeSeriesSize=3, poll interval 10s, SnapshotJob 15s
T=0:    sim_set_scenario agg_breach
        Poll 1: MetricPollJob fires, agg computed → slot[0] = 100 (violated)
T=10s:  Poll 2 → slot[1] = 100 (violated)
T=20s:  Poll 3 → slot[2] = 100 (violated) — series full, all 3 violated
T=~30s: SnapshotJob fires: AreAllEvaluateViolated returns true → tier=4 fires
T=~45s: (with jitter, 90s timeout is safe)

Recovery:
T=breach_confirmed: sim_set_scenario healthy  (.4.5=0, .4.6=0 → sum=0 < 80)
T=+10s:  Poll overwrites one slot with 0 (in-range)
T=+15s:  SnapshotJob: one in-range sample → AreAllEvaluateViolated returns false → tier=3
```

## State of the Art

| Area | Current State | Impact |
|------|---------------|--------|
| Existing STS/MTS patterns | All prior scenarios use identical setup/assert/cleanup structure | ADV-01 and ADV-02 are straightforward extensions |
| tenant-cfg04-aggregate.yaml | Fixture exists; uses e2e_total_util as Evaluate with TimeSeriesSize=3 | No new fixture needed |
| K8s OID map + devices | e2e_agg_source_a/b OIDs mapped; aggregate poll group configured | Pipeline already works; scenario scripts activate the behavior |
| Simulator scenario gap | No "agg_breach" scenario exists | Must add to e2e_simulator.py |

**No deprecated approaches apply.** All patterns used in phases 53-54 are current.

## Open Questions

1. **Does the simetra-oid-metric-map.yaml (K8s deployed version) match the local src/ version?**
   - What we know: `deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml` has `.4.5` and `.4.6` entries. The local `src/SnmpCollector/config/oid_metric_map.json` does NOT have these entries.
   - What's unclear: Which configmap is currently deployed in the cluster (the K8s yaml or the local dev fallback)?
   - Recommendation: The e2e test runner uses the K8s configmap (applied via `kubectl apply`), not the local dev fallback. The K8s yaml in `deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml` is the ground truth for e2e. The scenario's prerequisite is that the cluster has been set up with the full K8s configmaps. If ever in doubt, the scenario can apply the oid-metric-map fixture before running, but prior scenarios do not do this, so it is assumed already deployed.

2. **Does Prometheus OTel export expose snmp_gauge with source label as `"synthetic"` or capitalized?**
   - What we know: `OtelMetricHandler.cs` line 45: `notification.Source.ToString().ToLowerInvariant()` → `"synthetic"` (lowercase, confirmed).
   - What's unclear: OTel Collector may normalize label values. This is unlikely but untested.
   - Recommendation: Use `source="synthetic"` (lowercase) in the PromQL query. If the label assertion fails even after tier-4 fires, a fallback check would be `source=~"(?i)synthetic"`.

3. **What exact tenant log pattern does e2e-tenant-agg emit?**
   - What we know: SnapshotJob logs `"Tenant {TenantId} priority={Priority} tier=4 — commands enqueued, count={CommandCount}"`. TenantId for tenant-cfg04-aggregate.yaml is `"e2e-tenant-agg"`.
   - Recommendation: Pattern `"e2e-tenant-agg.*tier=4 — commands enqueued"` (consistent with 35-mts-02 patterns for tenant IDs).

## Sources

### Primary (HIGH confidence)
- `simulators/e2e-sim/e2e_simulator.py` — SCENARIOS dict, OID definitions, all existing scenarios
- `tests/e2e/fixtures/tenant-cfg04-aggregate.yaml` — fixture definition, TimeSeriesSize=3, e2e_total_util evaluate, Max:80
- `tests/e2e/lib/*.sh` — full API surface of all test library functions
- `tests/e2e/scenarios/30-sts-02-evaluate-violated.sh` — STS-02 reference pattern
- `tests/e2e/scenarios/35-mts-02-advance-gate.sh` — MTS-02 reference pattern (most complex prior scenario)
- `src/SnmpCollector/Jobs/SnapshotJob.cs` — exact tier evaluation logic, all-samples check, log messages
- `src/SnmpCollector/Jobs/MetricPollJob.cs` — aggregate dispatch, SnmpSource.Synthetic, sentinel OID "0.0"
- `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` — snmp_gauge label set including `source`
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` — source label rendered as `.ToString().ToLowerInvariant()`
- `deploy/k8s/snmp-collector/simetra-devices.yaml` — aggregate poll group config with AggregatedMetricName
- `deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml` — .4.5 and .4.6 OID entries
- `tests/e2e/lib/report.sh` — `_REPORT_CATEGORIES` array, current range `"Snapshot Evaluation|28|34"`

## Metadata

**Confidence breakdown:**
- New simulator scenario needed: HIGH — all existing scenarios confirmed; "agg_breach" gap is definitive
- Resolved metrics must be in-range for tier-4 path: HIGH — SnapshotJob tier-2 logic verified in source
- source=synthetic label value: HIGH — OtelMetricHandler line 45 confirmed lowercase
- Recovery uses "healthy" scenario: HIGH — baseline .4.5=0, .4.6=0 confirmed in simulator
- Timing (90s timeout for depth-3): HIGH — consistent with STS-02, MTS-01 for identical TimeSeriesSize=3

**Research date:** 2026-03-17
**Valid until:** 2026-04-17 (stable, source code changes slowly)
