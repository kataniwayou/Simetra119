# Phase 75: E2E Validation Scenarios - Research

**Researched:** 2026-03-23
**Domain:** Bash E2E scenario scripting, SnapshotJob evaluation paths, TenantMetricService OTel instruments
**Confidence:** HIGH

## Summary

The existing E2E suite (scenarios 01-106) uses a consistent pattern: each scenario is a sourced Bash script that calls lib/ helper functions, drives the simulator via HTTP, and queries Prometheus for assertion. Scenarios that manipulate tenant config apply a fixture YAML, wait for reload, prime OID values via `sim_set_oid`, then assert on log output (`poll_until_log`) and Prometheus counter deltas (`snapshot_counter` / `poll_until`). The pattern is well-established and directly reusable for Phase 75.

The tenant metric instruments (8 total) live in `TenantMetricService`, on the `SnmpCollector.Tenant` meter which is exported by ALL instances — this is the key difference from the leader-gated `SnmpCollector.Leader` meter. Prometheus metric names follow OTel dot-to-underscore conversion. The existing PSS fixture `tenant-cfg06-pss-single.yaml` (tenant `e2e-pss-tenant`, Priority=1) and its T2 OID suffixes (5.1 eval, 5.2 res1, 5.3 res2) are the proven way to drive each evaluation path.

The 4 evaluation paths (NotReady, Resolved, Healthy, Unresolved) each require specific OID states. The new scenarios start at 107 and must add a "Tenant Metric Validation" category in `report.sh`. The smoke test scenario (instrument presence check) should run first.

**Primary recommendation:** Use `tenant-cfg06-pss-single.yaml` and existing T2 OID control for all 4 path scenarios. Add a new fixture `tenant-cfg09-tvm-single.yaml` if the existing PSS fixture name is ambiguous, but reuse it directly to avoid test environment churn. Verify per-pod export by querying `{k8s_pod_name="$POD"}` label, following the pattern in scenario 106.

## Standard Stack

### Core
| Tool/Library | Version | Purpose | Why Standard |
|---|---|---|---|
| bash | system | Scenario script language | All 106 existing scenarios use bash |
| curl | system | HTTP calls to Prometheus + simulator | Used in all lib/ helpers |
| kubectl | cluster version | Pod listing, log polling, ConfigMap apply | Used in kubectl.sh and scenario cleanup |
| jq | system | JSON response parsing from Prometheus API | Used throughout prometheus.sh |

### Lib Functions Available
| Function | File | Purpose |
|---|---|---|
| `query_prometheus <promql>` | prometheus.sh | Raw PromQL query, returns JSON |
| `query_counter <metric> [filter]` | prometheus.sh | Sum a counter, returns integer |
| `snapshot_counter <metric> [filter]` | prometheus.sh | Alias for query_counter |
| `poll_until <timeout> <interval> <metric> <filter> <baseline>` | prometheus.sh | Wait for counter to exceed baseline |
| `poll_until_exists <timeout> <interval> <metric>` | prometheus.sh | Wait for any series to appear |
| `assert_delta_gt <delta> <threshold> <name> <evidence>` | common.sh | Assert delta > threshold |
| `assert_delta_eq <delta> <expected> <name> <evidence>` | common.sh | Assert delta == expected |
| `assert_delta_ge <delta> <min> <name> <evidence>` | common.sh | Assert delta >= min |
| `assert_exists <metric> <name> <evidence>` | common.sh | Assert metric has >= 1 series |
| `record_pass <name> <evidence>` | common.sh | Record a pass |
| `record_fail <name> <evidence>` | common.sh | Record a fail |
| `poll_until_log <timeout> <interval> <pattern> [since_s]` | sim.sh | Poll pod logs for pattern |
| `sim_set_oid <suffix> <value>` | sim.sh | POST /oid/{suffix}/{value} |
| `sim_set_oid_stale <suffix>` | sim.sh | POST /oid/{suffix}/stale |
| `reset_oid_overrides` | sim.sh | DELETE /oid/overrides |
| `save_configmap <name> <ns> <file>` | kubectl.sh | Save ConfigMap to file |
| `restore_configmap <file>` | kubectl.sh | kubectl apply -f file |
| `get_evidence <metric> [filter]` | prometheus.sh | Returns formatted evidence string |

**Installation:** No new dependencies. All libs are pre-existing.

## Architecture Patterns

### Recommended Project Structure
```
tests/e2e/scenarios/
├── 107-tvm01-smoke.sh         # Smoke: all 8 instruments present
├── 108-tvm02-notready.sh      # NotReady path: state gauge + duration only
├── 109-tvm03-resolved.sh      # Resolved path: tier1/tier2 counters
├── 110-tvm04-healthy.sh       # Healthy path: tier1/tier2/tier3 counters
├── 111-tvm05-unresolved.sh    # Unresolved path: all tiers + command counters
├── 112-tvm06-all-instances.sh # All-instances export: tenant_state on each pod
```

### Pattern 1: Fixture-Prime-Assert Sequence
**What:** Apply tenant ConfigMap, prime OIDs to pass readiness grace, assert metrics.
**When to use:** All scenarios that need a specific evaluation path.
**Example (from scenarios 53-58):**
```bash
FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"
SCENARIO_NAME="TVM-XX: description"

save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-single.yaml" > /dev/null 2>&1 || true

if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "Tenant vector reload confirmed"
else
    log_warn "Tenant vector reload not detected within 60s; proceeding"
fi

# Prime T2 OIDs for readiness grace (6s grace + 2s margin = 8s)
sim_set_oid "5.1" "10"   # T2 eval in-range (>= Min:10)
sim_set_oid "5.2" "1"    # T2 res1 in-range (>= Min:1)
sim_set_oid "5.3" "1"    # T2 res2 in-range (>= Min:1)
sleep 8

# [OID mutation to trigger path]
# [assertion]

# Cleanup
reset_oid_overrides
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "Failed to restore tenant ConfigMap"
fi
```

### Pattern 2: Counter Delta Verification
**What:** Snapshot before/after, assert delta meets condition.
**When to use:** Counter increment verification (tier1/tier2/tier3, command counters).
```bash
BEFORE=$(snapshot_counter "tenant_tier3_evaluate_total" 'tenant_id="e2e-pss-tenant",priority="1"')
sleep 15
AFTER=$(snapshot_counter "tenant_tier3_evaluate_total" 'tenant_id="e2e-pss-tenant",priority="1"')
DELTA=$((AFTER - BEFORE))
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "delta=${DELTA}"
```

### Pattern 3: Gauge Point-in-Time Query
**What:** Query current gauge value, assert equals expected state enum integer.
**When to use:** `tenant_state` verification.
```bash
STATE=$(query_prometheus 'tenant_state{tenant_id="e2e-pss-tenant"}' \
    | jq -r '.data.result[0].value[1] // "-1"' | cut -d. -f1)
if [ "$STATE" = "3" ]; then
    record_pass "$SCENARIO_NAME" "tenant_state=3 (Unresolved). state=${STATE}"
else
    record_fail "$SCENARIO_NAME" "tenant_state expected=3 actual=${STATE}"
fi
```

### Pattern 4: Per-Pod Metric Presence
**What:** Iterate all 3 replica pods, assert metric series exists (or is absent) per pod.
**When to use:** All-instances export verification (scenario 112).
**Example (from scenario 106):**
```bash
POD_NAMES=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{range .items[*]}{.metadata.name}{"\n"}{end}')
for POD in $POD_NAMES; do
    COUNT=$(query_prometheus "tenant_state{k8s_pod_name=\"${POD}\"}" \
        | jq -r '.data.result | length')
    # assert COUNT > 0
done
```

### Pattern 5: Histogram Existence Check
**What:** Assert histogram bucket series are present and the _count series has value > 0.
**When to use:** `tenant_evaluation_duration_milliseconds` verification.
```bash
BUCKET_COUNT=$(query_prometheus '{__name__=~"tenant_evaluation_duration_milliseconds_bucket"}' \
    | jq -r '.data.result | length')
# assert BUCKET_COUNT > 0
DURATION_COUNT=$(query_counter "tenant_evaluation_duration_milliseconds_count" \
    'tenant_id="e2e-pss-tenant"')
# assert DURATION_COUNT > 0
```

### Anti-Patterns to Avoid
- **Querying by tenantId (camelCase):** Label keys are `tenant_id` and `priority` (snake_case). Use exactly these names.
- **Asserting specific counter delta == 1:** Counters increment by holder count per cycle. Use `assert_delta_gt 0` or `assert_delta_ge N` where N = expected holder count.
- **Not waiting for ConfigMap reload:** Always `poll_until_log` for "Tenant vector reload complete" before priming OIDs.
- **Skipping OID reset in cleanup:** Always call `reset_oid_overrides` before restoring ConfigMap to prevent OID state leaking into subsequent scenarios.
- **Querying tenant_state without tenant_id filter:** Multiple tenants may be active; always filter by `tenant_id`.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---|---|---|---|
| Counter snapshot | Custom curl | `snapshot_counter` | Already handles sum + or vector(0) fallback |
| Wait for metric | Sleep loop | `poll_until` | Handles timeout, interval, baseline comparison |
| Wait for log | Sleep + kubectl | `poll_until_log` | Handles multi-pod search + since window |
| Evidence string | Manual formatting | `get_evidence` | Consistent format across all scenarios |
| Prometheus query | Direct curl | `query_prometheus` | Error handling + URL management |

**Key insight:** The lib/ functions handle all the boilerplate. New scenarios should be purely declarative: OID stimulus + assertion. No custom HTTP or polling logic.

## Common Pitfalls

### Pitfall 1: NotReady Path Records No Tier/Command Counters
**What goes wrong:** Asserting tier1/tier2/tier3 or command counters increment during NotReady path.
**Why it happens:** SnapshotJob's NotReady early-return path calls only `RecordAndReturn` (state + duration). No tier or command counters are recorded.
**How to avoid:** For the NotReady scenario, only assert `tenant_state == 0` and `tenant_evaluation_duration_milliseconds_count > 0`. Do not assert any tier counter increment.
**Warning signs:** Counter delta stays 0 during NotReady assertion and test fails.

### Pitfall 2: Counter Increments by Holder Count, Not by 1
**What goes wrong:** Asserting `delta == 1` for a counter that fires per-holder-per-cycle.
**Why it happens:** The count-then-loop pattern calls `IncrementTier1Stale` once per stale holder. For `e2e-pss-tenant` (1 eval + 2 resolved = 3 holders), tier2_resolved increments by 2 when both resolved holders are non-violated.
**How to avoid:** Use `assert_delta_gt 0` or `assert_delta_ge N` where N matches expected holder count. For command counters, use `assert_delta_gt 0` (dispatched increments by 1 per command per cycle).
**Warning signs:** `assert_delta_eq 1` fails when delta is 2.

### Pitfall 3: Stale OID Triggers Tier 1, Not NotReady
**What goes wrong:** Confusing NotReady (grace window) with tier=1 (post-grace staleness).
**Why it happens:** `sim_set_oid_stale` makes OID return NoSuchInstance. If the tenant is already past grace, the stale OID triggers tier=1 (staleness detected), which proceeds to tier=4 Unresolved — NOT NotReady.
**How to avoid:** To trigger NotReady, do NOT prime OIDs. Apply the fixture and immediately query — tenant will be in grace window. To trigger tier=1/Unresolved via stale, prime first (pass grace), then stale.
**Warning signs:** Expected NotReady state but got Unresolved.

### Pitfall 4: ConfigMap Restore Leaves Stale OID State
**What goes wrong:** OID overrides from one scenario bleed into the next.
**Why it happens:** If cleanup order is wrong (restore ConfigMap before reset OIDs), new tenant may query stale OIDs set for old tenant.
**How to avoid:** Always `reset_oid_overrides` BEFORE `restore_configmap`. Follow the exact cleanup order in existing PSS scenarios.

### Pitfall 5: Per-Pod Query Requires k8s_pod_name Label
**What goes wrong:** `tenant_state{k8s_pod_name="..."}` returns empty results.
**Why it happens:** `resource_to_telemetry_conversion.enabled: true` is required in the OTel collector config to convert k8s.pod.name resource attribute to a Prometheus label. Scenario 106 already validates this for `snmp_gauge`.
**How to avoid:** Add a preflight check (like 106) to verify `k8s_pod_name` label exists on `tenant_state` before iterating pods.

### Pitfall 6: Histogram in Prometheus Has Different Name from OTel
**What goes wrong:** Querying `tenant.evaluation.duration.milliseconds` (OTel name) instead of `tenant_evaluation_duration_milliseconds_bucket` (Prometheus name).
**Why it happens:** OTel dot-to-underscore conversion plus Prometheus histogram suffix appending.
**How to avoid:** Prometheus metric name for histograms: `tenant_evaluation_duration_milliseconds_bucket`, `_count`, `_sum`. Query bucket series with `{__name__=~"tenant_evaluation_duration_milliseconds_bucket"}`.

### Pitfall 7: tenant_state is a Gauge (Not a Counter)
**What goes wrong:** Using `snapshot_counter` / `poll_until` for `tenant_state`.
**Why it happens:** `snapshot_counter` applies `sum()` which works but masks per-instance values; `poll_until` checks for monotone increase which won't work for a gauge.
**How to avoid:** Query `tenant_state` directly with `query_prometheus` and extract `.data.result[0].value[1]`. For per-pod, iterate pods with `k8s_pod_name` filter.

## Code Examples

Verified patterns from official sources (codebase):

### Prometheus Metric Names (OTel dot-to-underscore)
```
OTel instrument name              → Prometheus name
tenant.tier1.stale               → tenant_tier1_stale_total
tenant.tier2.resolved            → tenant_tier2_resolved_total
tenant.tier3.evaluate            → tenant_tier3_evaluate_total
tenant.command.dispatched        → tenant_command_dispatched_total
tenant.command.failed            → tenant_command_failed_total
tenant.command.suppressed        → tenant_command_suppressed_total
tenant.state                     → tenant_state
tenant.evaluation.duration.milliseconds → tenant_evaluation_duration_milliseconds_bucket/count/sum
```
**Source:** TenantMetricService.cs (instrument names) + OTel Prometheus exporter convention.

### Label Values on All Tenant Metrics
```
tenant_id   = tenant Name from ConfigMap (e.g., "e2e-pss-tenant")
priority    = integer as string (e.g., "1")
```
**Source:** TenantMetricService.cs lines 61-89 — tag keys are `"tenant_id"` and `"priority"` (snake_case).

### TenantState Gauge Values
```
0 = NotReady
1 = Healthy
2 = Resolved
3 = Unresolved
```
**Source:** TenantState.cs.

### OID Suffix Map for e2e-pss-tenant (tenant-cfg06-pss-single.yaml)
```
5.1  → MetricName: e2e_eval_T2   (Role: Evaluate, Threshold Min:10)
5.2  → MetricName: e2e_res1_T2   (Role: Resolved,  Threshold Min:1)
5.3  → MetricName: e2e_res2_T2   (Role: Resolved,  Threshold Min:1)
```
**Source:** tenant-cfg06-pss-single.yaml + existing scenarios 53-58.

### How to Trigger Each Evaluation Path
```
NotReady:   Apply fixture, do NOT prime OIDs. Query within grace window (first ~6s).
            tenant_state = 0

Tier=1/Unresolved via stale:
            Prime OIDs → sleep 8s → sim_set_oid_stale "5.1"/"5.2"/"5.3"
            tenant_state = 3, tier1_stale increments per stale holder count

Resolved (tier=2):
            Prime OIDs → sleep 8s → sim_set_oid "5.2" "0" + sim_set_oid "5.3" "0"
            (both resolved violated, eval stays in-range)
            tenant_state = 2, tier2_resolved increments per non-violated resolved count

Healthy (tier=3):
            Prime OIDs → sleep 8s → no changes (eval in-range, resolved not all violated)
            tenant_state = 1, tier3_evaluate increments (evaluate not violated count)

Unresolved via evaluate (tier=4):
            Prime OIDs → sleep 8s → sim_set_oid "5.1" "0"
            (eval violated, resolved in-range → tier=2 passes, tier=4 fires)
            tenant_state = 3, command_dispatched increments
```
**Source:** SnapshotJob.cs EvaluateTenant logic + existing PSS scenarios.

### Simulator HTTP Control API
```bash
POST http://localhost:8080/oid/{oid_suffix}/{value}    # set OID to value
POST http://localhost:8080/oid/{oid_suffix}/stale       # set OID to NoSuchInstance
DELETE http://localhost:8080/oid/overrides              # clear all overrides
POST http://localhost:8080/scenario/{name}              # set scenario
GET  http://localhost:8080/scenario                     # get current scenario
```
**Source:** sim.sh.

### Smoke Test: Verify All 8 Instruments Present
```bash
# Source: assert_exists in common.sh
for metric in \
    "tenant_tier1_stale_total" \
    "tenant_tier2_resolved_total" \
    "tenant_tier3_evaluate_total" \
    "tenant_command_dispatched_total" \
    "tenant_command_failed_total" \
    "tenant_command_suppressed_total" \
    "tenant_state" \
    "tenant_evaluation_duration_milliseconds_count"; do
    assert_exists "$metric" "TVM-01A: $metric present" ""
done
```

### Per-Pod Export Verification (from scenario 106 pattern)
```bash
POD_NAMES=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{range .items[*]}{.metadata.name}{"\n"}{end}')
for POD in $POD_NAMES; do
    [ -z "$POD" ] && continue
    COUNT=$(query_prometheus "tenant_state{k8s_pod_name=\"${POD}\"}" \
        | jq -r '.data.result | length')
    if [ "$COUNT" -gt 0 ]; then
        record_pass "TVM-06: tenant_state present on pod ${POD}" "k8s_pod_name=${POD} series_count=${COUNT}"
    else
        record_fail "TVM-06: tenant_state present on pod ${POD}" "k8s_pod_name=${POD} series_count=0"
    fi
done
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|---|---|---|---|
| Log-only proof of evaluation path | Prometheus tenant metrics + logs | Phase 73 | Can assert state and counters via PromQL |
| No per-tenant instrumentation | 8 instruments in TenantMetricService | Phase 73 | Full observability of evaluation paths |
| snmp_command_dispatched (pipeline) | tenant_command_dispatched (per-tenant) | Phase 73 | Can filter by tenant_id + priority |

**Note:** The `snmp_command_dispatched_total` (from PipelineMetricService, `device_name` label) is a different metric from `tenant_command_dispatched_total` (from TenantMetricService, `tenant_id` + `priority` labels). Scenarios 56/58 used the pipeline metric. Phase 75 scenarios use the tenant metric.

## Open Questions

1. **NotReady trigger timing**
   - What we know: Grace window is `IntervalSeconds * GraceMultiplier`. For `e2e-pss-tenant`, IntervalSeconds=1, GraceMultiplier=2.0 → 2s grace. But scenarios use `sleep 8` to clear grace, implying actual grace > 2s.
   - What's unclear: The actual effective grace window. Scenario comments say "6s grace + 2s margin" but the config says 2s. Possibly the IntervalSeconds polling period adds latency.
   - Recommendation: For NotReady scenario, apply fixture and query immediately (within first 2-3s) before any sleep. This guarantees in-grace-window state.

2. **Smoke test tenant identity**
   - What we know: Smoke test must find tenant_state series. This requires a tenant to have run at least one cycle.
   - What's unclear: Whether tenant_state will be present at smoke test time (scenario 107) if no tenant fixture was applied yet.
   - Recommendation: Run smoke test AFTER applying `tenant-cfg06-pss-single.yaml` and waiting for at least one cycle, then restore. Or rely on the existing tenant ConfigMap from the running cluster.

3. **report.sh category update**
   - What we know: report.sh uses hardcoded index ranges. New scenarios 107-112 map to result indices 106-111. The "Negative Proofs" category currently spans indices 104-108 which will include the new scenarios.
   - What's unclear: The exact current end index for "Negative Proofs" (106 scenarios = indices 0-105, so 104-108 extends beyond current count).
   - Recommendation: Add a new category "Tenant Metric Validation|106|111" (indices for scenarios 107-112). Adjust "Negative Proofs" end to 105 (scenarios 105-106 = indices 104-105).

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Telemetry/TenantMetricService.cs` — 8 instrument definitions, tag key names, meter name
- `src/SnmpCollector/Jobs/SnapshotJob.cs` — 4-tier evaluation logic, tier counter call sites, RecordAndReturn pattern
- `src/SnmpCollector/Pipeline/TenantState.cs` — enum values (0-3)
- `src/SnmpCollector/Telemetry/TelemetryConstants.cs` — meter names, export gates
- `tests/e2e/lib/prometheus.sh` — all prometheus helper functions
- `tests/e2e/lib/common.sh` — assertion functions, result tracking
- `tests/e2e/lib/sim.sh` — simulator HTTP API functions
- `tests/e2e/lib/kubectl.sh` — ConfigMap save/restore, pod listing
- `tests/e2e/lib/report.sh` — category index map

### Secondary (HIGH confidence)
- `tests/e2e/scenarios/53-pss-01-not-ready.sh` — fixture-prime-assert pattern, OID suffix map
- `tests/e2e/scenarios/55-pss-03-resolved.sh` — Resolved path, negative assertion pattern
- `tests/e2e/scenarios/56-pss-04-unresolved.sh` — Unresolved path, command counter delta
- `tests/e2e/scenarios/57-pss-05-healthy.sh` — Healthy path
- `tests/e2e/scenarios/58-pss-06-suppression.sh` — Suppressed counter, counter label clarification
- `tests/e2e/scenarios/106-mnp05-follower-no-snmp-gauge.sh` — Per-pod k8s_pod_name query pattern
- `tests/e2e/fixtures/tenant-cfg06-pss-single.yaml` — canonical single-tenant fixture

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all libs are in codebase, no external dependencies
- Architecture: HIGH — patterns extracted directly from 6 existing scenarios
- Pitfalls: HIGH — derived from source code (counter logic, enum values, meter names)
- OID suffix map: HIGH — verified from fixture YAML + scenario comments

**Research date:** 2026-03-23
**Valid until:** Stable — no external dependencies; valid until SnapshotJob or TenantMetricService is changed
