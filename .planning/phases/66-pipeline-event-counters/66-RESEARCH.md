# Phase 66: Pipeline Event Counters - Research

**Researched:** 2026-03-22
**Domain:** E2E test scenarios, MediatR pipeline counters, Prometheus queries
**Confidence:** HIGH (all findings sourced from codebase)

---

## Summary

Phase 66 adds precise E2E verification scenarios for the four MediatR pipeline event counters:
`snmp.event.published`, `snmp.event.handled`, `snmp.event.rejected`, and `snmp.event.errors`.

The existing E2E suite (scenarios 02, 03, 08, 09) tests these counters with weak `assert_delta_gt 0`
guards — they confirm the counters exist and move, but do not verify exact counts or boundary
conditions. Phase 66 must add new numbered scenarios (69+) that assert precise delta values using
a new `assert_delta_eq` helper, and must update `report.sh` to include them in the correct report
category.

**Critical finding:** `snmp.event.rejected` is NOT incremented for unmapped OIDs. It fires only on
`ValidationBehavior` failures: invalid OID format or missing DeviceName. The phase goal says
"rejected increases for OIDs not in oidmaps.json" — but the current implementation does not do
this. The planner must decide whether to test the actual behavior (rejected = malformed OIDs only)
or whether ValidationBehavior needs a new rejection path for Unknown metric names.

**Primary recommendation:** Write tests against the actual code behavior. If the goal requires
rejection for unmapped OIDs, first add that code path to ValidationBehavior, then test it.

---

## Exact Simulator OID Counts

### E2E-SIM: OIDs served by simulator
The simulator (`simulators/e2e-sim/e2e_simulator.py`) registers and serves exactly **24 OIDs**:
- 7 mapped OIDs (`1.3.6.1.4.1.47477.999.1.x.0`) — all present in oid_metric_map
- 2 unmapped OIDs (`1.3.6.1.4.1.47477.999.2.x.0`) — NOT in oid_metric_map
- 6 test-purpose OIDs (`1.3.6.1.4.1.47477.999.4.x.0`) — all mapped
- 9 tenant OIDs (`1.3.6.1.4.1.47477.999.5.x.0`, `.6.x`, `.7.x`) — all mapped

### E2E-SIM: OIDs per poll group (from `simetra-devices.yaml`)
E2E-SIM has 4 poll groups:

| Group | Interval | OID Count | All Mapped? |
|-------|----------|-----------|-------------|
| 0     | 10s      | 7         | Yes (e2e_gauge_test..e2e_ip_test) |
| 1     | 10s      | 6         | Yes (e2e_port_utilization..e2e_agg_source_b) |
| 2     | 1s       | 9         | Yes (e2e_eval_T2..e2e_res2_T4) |
| 3     | 10s      | 2 + aggregate | Yes (e2e_agg_source_a, e2e_agg_source_b + 1 synthetic) |

**Poll group 2 fires every 1 second** — it is the fastest cycle and easiest to gate exact counts.

The 2 unmapped OIDs (`.999.2.x`) are served by the simulator but are NOT in the poll config.
They never appear in the published counter during a normal E2E run — they are only encountered
if the devices ConfigMap is mutated to include unmapped metric names (as done in scenario 15).

### Trap stream
Each valid trap carries exactly **1 varbind**: OID `1.3.6.1.4.1.47477.999.1.1.0` (e2e_gauge_test),
type Gauge32, value 42. This OID is in the oid_metric_map. So each valid trap contributes:
- published += 1
- handled += 1 (it is a mapped, valid Gauge32)
- rejected += 0
- errors += 0

Trap interval: 30s (env `TRAP_INTERVAL=30`). Bad-community traps fire every 45s but are dropped
before reaching the pipeline (no increment to any pipeline counter).

---

## Prometheus Metric Names

| Code name           | Prometheus name                    | Label   |
|---------------------|------------------------------------|---------|
| snmp.event.published| `snmp_event_published_total`       | device_name |
| snmp.event.handled  | `snmp_event_handled_total`         | device_name |
| snmp.event.rejected | `snmp_event_rejected_total`        | device_name |
| snmp.event.errors   | `snmp_event_errors_total`          | device_name |

All four counters have a single `device_name` tag. There is no pod-level label in the counter
itself; OTel resource attributes (`k8s.pod.name`, `service.instance.id`) are attached at the
resource level, not as metric labels in Prometheus by default.

**Prometheus names follow OTel → Prometheus naming convention:** dots become underscores,
`_total` suffix added. Verified in scenario files 02, 03, 08, 09.

---

## Pipeline Behavior: How Each Counter Increments

### snmp.event.published
- **Where:** `LoggingBehavior.Handle()` — outermost behavior, always fires first
- **Condition:** Any `SnmpOidReceived` message entering ISender.Send (poll or trap path)
- **NOT incremented:** aggregate synthetic messages use Source=Synthetic but still pass through
  LoggingBehavior, so synthetics DO increment published
- **Source:** `src/SnmpCollector/Pipeline/Behaviors/LoggingBehavior.cs:37`

### snmp.event.handled
- **Where:** `OtelMetricHandler.Handle()` — terminal handler
- **Condition:** Fires on recognized SNMP type codes: Integer32, Gauge32, TimeTicks, Counter32,
  Counter64, OctetString, IPAddress, ObjectIdentifier
- **NOT incremented:** unknown TypeCode (falls to default/warning branch)
- **Source:** `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs:65,87`

### snmp.event.rejected
- **Where:** `ValidationBehavior.Handle()` — fires on two conditions only:
  1. OID string fails regex `^\d+(\.\d+){1,}$`
  2. DeviceName is null
- **CRITICAL FINDING:** Unmapped OIDs (OidMapService.Unknown) do NOT increment rejected.
  Unknown OIDs pass through ValidationBehavior, through OidResolutionBehavior, and reach
  OtelMetricHandler where they are handled normally (with MetricName="Unknown").
- **Source:** `src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs:55,70`

### snmp.event.errors
- **Where:** `ExceptionBehavior.Handle()` — catches unhandled exceptions from any downstream behavior
- **Condition:** Exception thrown by ValidationBehavior, OidResolutionBehavior, ValueExtraction,
  TenantVectorFanOut, or OtelMetricHandler
- **Normal run:** 0 errors expected
- **Source:** `src/SnmpCollector/Pipeline/Behaviors/ExceptionBehavior.cs:40-48`

---

## Multi-Replica Impact on Counters

The deployment has **3 replicas**. All three pods:
- Poll the same devices on independent timers (not coordinated)
- Receive the same trap broadcasts (UDP multicast via `simetra-pods` headless service)
- Export counters independently to OTel collector

`query_counter()` in `prometheus.sh` uses `sum(metric{filter})` which **sums across all pods**.

**Impact on exact delta testing:**
- For poll-based assertions: one poll cycle across 3 pods produces `delta = 3 * (OIDs_per_group)`
  in the summed Prometheus value
- For trap-based assertions: each trap is delivered to all 3 pods (broadcast). One trap event
  produces `delta = 3 * 1 = 3` in the summed published/handled counters
- The test must either:
  (a) Assert `delta >= expected * replicas` (with tolerance), or
  (b) Filter by a specific pod using `k8s.pod.name` label if it is propagated to Prometheus, or
  (c) Divide the observed delta by the replica count and compare

**Recommendation:** Use per-pod filtering via `instance` or `k8s_pod_name` label in the PromQL
query, OR accept the replica multiplication factor and assert `delta >= N_replicas * expected_per_pod`.
Check existing scenario behavior: scenarios 02 and 03 use `device_name="OBP-01"` with `sum()` and
work fine with `assert_delta_gt 0`. For exact counting, multiplying by 3 is the safest approach.

---

## OTel Export and Scrape Timing

The complete chain from counter increment to Prometheus availability:
1. OTel SDK batches counters in-memory
2. `PeriodicExportingMetricReader` pushes to OTel collector every **15 seconds**
   (`exportIntervalMilliseconds: 15_000` in `ServiceCollectionExtensions.cs:105`)
3. OTel collector processes immediately (batch interval: 1s in `otel-collector-configmap.yaml:28`)
4. Prometheus scrapes OTel collector's Prometheus exporter every **5 seconds**
   (`scrape_interval: 5s` in `prometheus.yaml:13`)

**Total worst-case lag:** 15s (OTel export) + 5s (Prometheus scrape) = **~20 seconds**

Existing scenarios use `POLL_TIMEOUT=30` and `POLL_INTERVAL=3`. For exact counter scenarios
that need to wait for a known number of events AND then wait for export, use timeout 45-60s
to account for worst-case lag.

---

## Existing Scenarios That Test These Counters

| Scenario | File | Assertion |
|----------|------|-----------|
| 02 | `02-event-published.sh` | `assert_delta_gt 0` for OBP-01 — weak, any increment passes |
| 03 | `03-event-handled.sh` | `assert_delta_gt 0` for OBP-01 — weak |
| 08 | `08-event-rejected.sh` | Sentinel check — passes whether counter exists or not |
| 09 | `09-event-errors.sh` | Sentinel check — passes whether counter exists or not |

None of the existing 68 scenarios test exact deltas, poll-to-publish correlation, or the
rejection boundary condition. Phase 66 adds these.

---

## Report System and Scenario Numbering

### How run-all.sh executes scenarios
- Globs `scenarios/[0-9]*.sh` in filename order
- Each file is `source`d — all lib functions are available without re-import
- Scenarios push to `SCENARIO_RESULTS[]` via `record_pass` / `record_fail`
- Results are 0-indexed in `SCENARIO_RESULTS`

### Current scenario count
68 scenarios exist (01 through 68). New scenarios start at **69**.

### Report categories in `report.sh`
```
"Pipeline Counters|0|9"       # scenarios 01-10 (0-indexed 0-9)
"Business Metrics|10|22"      # scenarios 11-23
"OID Mutations|23|25"         # scenarios 24-26
"Device Lifecycle|26|27"      # scenarios 27-28
"Snapshot Evaluation|28|39"   # scenarios 29-40
"Snapshot State Suite|40|51"  # scenarios 41-52
"Progressive Snapshot Suite|52|67"  # scenarios 53-68
```

**Critical:** New scenarios at indices 68+ (scenario numbers 69+) fall outside all existing
categories. The planner must either:
1. Add a new category `"Pipeline Counter Verification|68|74"` (or similar range) to `report.sh`, or
2. Extend the existing "Pipeline Counters" category bounds

### Cross-Stage PSS Summary in run-all.sh
`run-all.sh` contains hardcoded PSS stage index ranges (lines 151-153). If scenarios are inserted
before the PSS scenarios, these indices shift. Adding new scenarios at the END (69+) avoids
this problem.

---

## Assertion Helpers Available

Current `common.sh` provides:
- `assert_delta_gt DELTA THRESHOLD NAME EVIDENCE` — passes if `DELTA > THRESHOLD`
- `assert_exists METRIC_NAME NAME EVIDENCE` — passes if metric series count > 0
- `record_pass / record_fail` — direct result recording

**Missing for Phase 66:** `assert_delta_eq` (exact equality) and `assert_delta_ge` (≥ threshold).
These need to be added to `common.sh`.

### Pattern for delta capture with wait

```bash
# Standard pattern (from existing scenarios 01-04)
BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
poll_until "$POLL_TIMEOUT" "$POLL_INTERVAL" "$METRIC" "$FILTER" "$BEFORE" || true
AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
```

For exact-count tests, after triggering the known event (e.g., waiting for exactly one poll cycle),
snapshot before and after and compare to `N_replicas * expected_per_cycle`.

---

## Architecture Patterns for New Scenarios

### Pattern A: Poll-cycle exact count (MCV-01)
1. Snapshot `snmp_event_published_total{device_name="E2E-SIM"}` before
2. Wait for exactly 1 poll cycle of the 1s-interval group (poll_until + sleep 2s to confirm single cycle)
3. Snapshot after
4. Assert `delta == 3 * 9` (3 replicas, 9 OIDs per 1s-interval group) using `assert_delta_eq`

**Simpler approach:** Assert `delta >= 9` (minimum: one replica completed one cycle)
and `delta % 9 == 0` (always a multiple of OIDs per cycle). This tolerates any number of replicas
and race conditions between cycle boundaries.

### Pattern B: Trap exact count (MCV-02)
1. Snapshot `snmp_event_published_total{device_name="E2E-SIM"}` before
2. Wait for 1 trap (poll_until, timeout 45s)
3. Also snapshot `snmp_trap_received_total` for evidence
4. Assert: for each trap received, published should increase by the trap varbind count (1)
   With 3 replicas and 1 trap broadcast: `delta_published = 3 * 1 = 3`

### Pattern C: Handled vs published parity (MCV-03/04)
- Snapshot both `published` and `handled` for E2E-SIM
- Wait for N cycles
- Assert `delta_published == delta_handled` (all E2E-SIM OIDs are mapped)

### Pattern D: Rejected stays 0 for valid OIDs (MCV-06)
- Snapshot `rejected{device_name="E2E-SIM"}`
- Wait 30s (3 poll cycles minimum across all groups)
- Assert `delta_rejected == 0`

### Pattern E: Errors stays 0 (MCV-07)
- Snapshot `errors{device_name="E2E-SIM"}`
- Wait 30s
- Assert `delta_errors == 0`

### Pattern F: Rejected increments for unmapped (MCV-05) — REQUIRES CODE CHANGE FIRST
**Current behavior:** ValidationBehavior only rejects malformed OID format or null DeviceName.
Unknown metric names (unmapped OIDs) are handled, not rejected.

To test this per the phase goal, either:
1. Add a rejection path in ValidationBehavior for `MetricName == OidMapService.Unknown` after
   OidResolutionBehavior runs — but ValidationBehavior runs BEFORE OidResolutionBehavior
2. Add a new behavior after OidResolutionBehavior that rejects Unknown OIDs
3. Reinterpret the test: test that the unmapped fixture (scenario 15 pattern) does NOT increment
   rejected, but DOES increment published+handled with MetricName="Unknown"

The planner must resolve this scope question before writing the test.

---

## Common Pitfalls

### Pitfall 1: OTel counter not yet exported
**What goes wrong:** Counter increments before `BEFORE` snapshot or after export cycle, causing
`DELTA = 0` even though events occurred.
**Prevention:** Use `poll_until` to wait for the counter to actually move before taking AFTER snapshot.

### Pitfall 2: Replica multiplication factor
**What goes wrong:** Asserting `delta == N` without accounting for 3 replicas.
**Prevention:** Assert `delta >= N` or `delta == 3 * N` and document the replica factor explicitly.

### Pitfall 3: Synthetic aggregates also increment published
**What goes wrong:** E2E-SIM poll group 3 has an AggregatedMetricName. The aggregate is dispatched
as a synthetic `SnmpOidReceived` with `Source=Synthetic`. LoggingBehavior fires for ALL
SnmpOidReceived messages including synthetics. Published count per group-3 cycle = 2 + 1 = 3.
**Prevention:** Use group-2 (the 1s interval group, 9 OIDs, no aggregate) for clean exact counts.

### Pitfall 4: Rejected counter never increments in normal run
**What goes wrong:** OTel counters only appear in Prometheus after their first `Add()` call.
If `snmp_event_rejected_total` never fires, `snapshot_counter` returns 0 (via `or vector(0)`).
Delta after 0-before 0 = 0, which is correct for MCV-07-adjacent tests.
**Prevention:** The existing scenario 08 already handles this correctly as a sentinel check.
Phase 66 tests can assert `delta_rejected == 0` using `assert_delta_eq` without worrying about
metric non-existence.

### Pitfall 5: Scenario numbering vs report category
**What goes wrong:** Adding scenarios 69-75 but not updating `_REPORT_CATEGORIES` in `report.sh`
causes them to silently not appear in the Markdown report.
**Prevention:** Add a new category entry or extend Pipeline Counters bounds in `report.sh`.

---

## Code Examples

### Adding assert_delta_eq to common.sh
```bash
# Source: pattern derived from existing assert_delta_gt in tests/e2e/lib/common.sh
assert_delta_eq() {
    local delta="$1"
    local expected="$2"
    local scenario_name="$3"
    local evidence="${4:-}"

    if [ "$delta" -eq "$expected" ]; then
        record_pass "$scenario_name" "delta=${delta} == expected=${expected}. ${evidence}"
    else
        record_fail "$scenario_name" "delta=${delta} != expected=${expected}. ${evidence}"
    fi
}

assert_delta_ge() {
    local delta="$1"
    local minimum="$2"
    local scenario_name="$3"
    local evidence="${4:-}"

    if [ "$delta" -ge "$minimum" ]; then
        record_pass "$scenario_name" "delta=${delta} >= minimum=${minimum}. ${evidence}"
    else
        record_fail "$scenario_name" "delta=${delta} < minimum=${minimum}. ${evidence}"
    fi
}
```

### Example scenario using exact delta (MCV-03: handled parity)
```bash
# Scenario 69: published/handled parity for E2E-SIM during normal poll
# All E2E-SIM mapped OIDs should show published == handled
SCENARIO_NAME="event_published equals event_handled for E2E-SIM (no unmapped in poll config)"
PUB_METRIC="snmp_event_published_total"
HDL_METRIC="snmp_event_handled_total"
FILTER='device_name="E2E-SIM"'

PUB_BEFORE=$(snapshot_counter "$PUB_METRIC" "$FILTER")
HDL_BEFORE=$(snapshot_counter "$HDL_METRIC" "$FILTER")
poll_until "$POLL_TIMEOUT" "$POLL_INTERVAL" "$PUB_METRIC" "$FILTER" "$PUB_BEFORE" || true
# Extra wait for OTel export lag
sleep 20
PUB_AFTER=$(snapshot_counter "$PUB_METRIC" "$FILTER")
HDL_AFTER=$(snapshot_counter "$HDL_METRIC" "$FILTER")
PUB_DELTA=$((PUB_AFTER - PUB_BEFORE))
HDL_DELTA=$((HDL_AFTER - HDL_BEFORE))
EVIDENCE="published_delta=${PUB_DELTA} handled_delta=${HDL_DELTA}"
assert_delta_eq "$PUB_DELTA" "$HDL_DELTA" "$SCENARIO_NAME" "$EVIDENCE"
```

### PromQL for per-metric sum with device filter
```bash
# Source: tests/e2e/lib/prometheus.sh query_counter()
# sum() aggregates across all 3 replica pods
query_counter "snmp_event_published_total" 'device_name="E2E-SIM"'
# Produces: sum(snmp_event_published_total{device_name="E2E-SIM"}) or vector(0)
```

---

## MCV Requirements vs. Implementation Reality

| Req | Goal Statement | Actual Behavior | Test Strategy |
|-----|---------------|-----------------|---------------|
| MCV-01 | published += N per poll cycle | True — LoggingBehavior fires per varbind | Assert delta >= (N per group) per cycle |
| MCV-02 | published += M per trap (M varbinds) | True — M=1 per E2E-SIM trap | Assert delta >= 1 after trap received |
| MCV-03 | handled only for mapped OIDs reaching terminal handler | True — OtelMetricHandler increments on success | Compare handled/published parity |
| MCV-04 | handled NOT for unmapped/rejected | Partially true — "unmapped" OIDs ARE handled with MetricName="Unknown" because poll config doesn't include them normally. Rejected OIDs (malformed) don't reach handler. | Test that handled == published for E2E-SIM (since all polled OIDs are mapped) |
| MCV-05 | rejected only for OIDs not in oidmaps | **MISMATCH** — rejected fires for malformed OID format, NOT for unknown metric names | Either (a) redefine test scope, (b) add rejection path for Unknown metric names |
| MCV-06 | rejected NOT for mapped OIDs | True — mapped OIDs in normal poll do not trigger ValidationBehavior rejection | Assert rejected delta == 0 during normal run |
| MCV-07 | errors == 0 after normal run | True — errors only on pipeline exceptions | Assert errors delta == 0 |

---

## Open Questions

1. **MCV-05 scope:** Does Phase 66 require adding a new code path to reject Unknown metric names,
   or should the test verify current behavior (rejected only for malformed OID format)?
   - What we know: ValidationBehavior only rejects on invalid OID format or null DeviceName
   - What's unclear: whether the phase goal requires behavior change or just test documentation
   - Recommendation: Clarify with a task that documents current behavior, then decide

2. **Exact vs. approximate delta:** With 3 replicas polling independently and OTel batching at 15s,
   exact counts are hard to guarantee in a single assertion window.
   - What we know: Using `assert_delta_ge` (at least N) is more reliable than `assert_delta_eq`
   - What's unclear: whether MCV-01/02 require strict equality or sufficiency
   - Recommendation: Use `assert_delta_ge` for all MCV scenarios; document expected minimum

3. **Aggregate synthetic published count:** Group 3 for E2E-SIM dispatches 2 poll OIDs + 1 synthetic.
   Published count is 3 per cycle, not 2. This affects exact counting for that group.
   - Recommendation: Use group 2 (9 OIDs, no aggregates, 1s interval) as the reference group

---

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — all four counter definitions and tags
- `src/SnmpCollector/Pipeline/Behaviors/LoggingBehavior.cs` — published increment location
- `src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs` — rejected increment conditions
- `src/SnmpCollector/Pipeline/Behaviors/ExceptionBehavior.cs` — errors increment conditions
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` — handled increment conditions
- `simulators/e2e-sim/e2e_simulator.py` — 24 OIDs, trap varbind count, trap interval
- `deploy/k8s/snmp-collector/simetra-devices.yaml` — E2E-SIM poll groups and intervals
- `tests/e2e/fixtures/.original-oid-metric-map-configmap.yaml` — all mapped OIDs
- `tests/e2e/lib/prometheus.sh` — query_counter, snapshot_counter, poll_until patterns
- `tests/e2e/lib/common.sh` — assert_delta_gt, record_pass/fail, missing assert_delta_eq
- `tests/e2e/lib/report.sh` — category bounds, "Pipeline Counters|0|9"
- `tests/e2e/run-all.sh` — scenario glob, PSS hardcoded indices
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs:105` — 15s OTel export interval
- `deploy/k8s/monitoring/otel-collector-configmap.yaml:28` — 1s batch interval
- `deploy/k8s/production/prometheus.yaml:13` — 5s Prometheus scrape interval
- `deploy/k8s/snmp-collector/deployment.yaml` — 3 replicas confirmed

---

## Metadata

**Confidence breakdown:**
- Counter names and increment conditions: HIGH — direct source code read
- Simulator OID counts: HIGH — direct Python source
- Poll group OID counts: HIGH — direct devices.json
- Trap varbind count: HIGH — trap send code sends exactly 1 varbind
- OTel export interval: HIGH — hardcoded in ServiceCollectionExtensions.cs
- Replica multiplication factor: HIGH — deployment.yaml replicas: 3
- MCV-05 behavior mismatch: HIGH — verified rejected is only called in ValidationBehavior

**Research date:** 2026-03-22
**Valid until:** 2026-04-22 (stable codebase, no external dependencies)
