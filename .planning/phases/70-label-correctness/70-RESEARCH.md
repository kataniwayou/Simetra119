# Phase 70: Label Correctness - Research

**Researched:** 2026-03-22
**Domain:** E2E test scenarios — Prometheus metric label correctness (source, snmp_type, resolved_name, device_name)
**Confidence:** HIGH

## Summary

Phase 70 adds E2E scenarios (file prefix 94+) that assert every label on snmp_gauge and snmp_info is correct. Four labels are verified: `source` (poll/trap/command/synthetic), `snmp_type` (7 distinct values), `resolved_name` (matches oidmaps.json), and `device_name` (derived from community string).

The label infrastructure is already working and proven by earlier phases: scenario 11 checks all four labels for source=poll, scenario 16 checks source=trap, scenario 17 checks snmp_type for 5 numeric types. Phase 70 does NOT duplicate those general label checks. Instead, it explicitly asserts exact label values for the 4 source variants and for the 2 string types (octetstring, ipaddress) that appear only in snmp_info, and explicitly verifies resolved_name and device_name per-metric.

The critical design decision: MLC-01 (source=poll) and MLC-02 (source=trap) CAN produce two distinct time series for the same OID because `source` is a label dimension. The same OID `.999.1.1.0` (e2e_gauge_test) can appear as both `snmp_gauge{source="poll",...}` and `snmp_gauge{source="trap",...}` simultaneously.

**Primary recommendation:** Each MLC scenario is a simple static PromQL query with an exact label assertion. No setup/teardown needed for MLC-01, 02, 07, 08. MLC-03 (command) and MLC-04 (synthetic) require setup because those sources are not always active.

---

## Standard Stack

### Core (already present — no new installs)

| Component | Version | Purpose |
|-----------|---------|---------|
| `tests/e2e/lib/common.sh` | in-repo | `record_pass`, `record_fail` |
| `tests/e2e/lib/prometheus.sh` | in-repo | `query_prometheus` |
| `tests/e2e/lib/sim.sh` | in-repo | `sim_set_oid`, `reset_oid_overrides` |
| `tests/e2e/lib/kubectl.sh` | in-repo | `save_configmap`, `restore_configmap`, `kubectl apply` |
| `tests/e2e/lib/report.sh` | in-repo | `_REPORT_CATEGORIES` — needs one new entry |

### No new dependencies needed.

---

## Label Pipeline: How Labels Are Set

### source label

**Code path:** `OtelMetricHandler.cs` line 45:
```csharp
var source = notification.Source.ToString().ToLowerInvariant();
```

`SnmpSource` enum members and their resulting string values:
| Enum member | source label value |
|-------------|-------------------|
| `SnmpSource.Poll` | `"poll"` |
| `SnmpSource.Trap` | `"trap"` |
| `SnmpSource.Command` | `"command"` |
| `SnmpSource.Synthetic` | `"synthetic"` |

**Where each source is set:**
- `Poll`: `MetricPollJob` — all regular SNMP GET poll results
- `Trap`: `SnmpTrapListenerService` / `ChannelConsumerService` — trap varbinds flowing through pipeline
- `Command`: `CommandWorkerService.ExecuteCommandAsync()` line 185 — SET response varbinds
- `Synthetic`: `MetricPollJob.PublishAggregatedAsync()` line 263 — computed aggregate metrics

### snmp_type label

**Code path:** `OtelMetricHandler.cs` lines 60, 80 (both numeric and string branches):
```csharp
notification.TypeCode.ToString().ToLowerInvariant()
```

`SnmpType` enum to label string mapping (verified against `e2e_simulator.py` type assignments):
| SnmpType enum | snmp_type label | Prometheus metric | OID suffix |
|---------------|----------------|-------------------|------------|
| `Gauge32` | `"gauge32"` | snmp_gauge | .999.1.1 |
| `Integer32` | `"integer32"` | snmp_gauge | .999.1.2 |
| `Counter32` | `"counter32"` | snmp_gauge | .999.1.3 |
| `Counter64` | `"counter64"` | snmp_gauge | .999.1.4 |
| `TimeTicks` | `"timeticks"` | snmp_gauge | .999.1.5 |
| `OctetString` | `"octetstring"` | snmp_info | .999.1.6 |
| `IPAddress` | `"ipaddress"` | snmp_info | .999.1.7 |

Note: `IPAddress` (C# enum name) → `"ipaddress"` (lowercase, no underscore). This is confirmed by scenario 14 which already asserts `snmp_type="ipaddress"` for e2e_ip_test.

### resolved_name label

`OtelMetricHandler` passes `notification.MetricName` as `metricName` argument. This becomes the `resolved_name` label in `SnmpMetricFactory`. For the 7 mapped .999.1.x OIDs, the mapping from oidmaps.json is:

| OID (full, with .0) | resolved_name label |
|---------------------|---------------------|
| 1.3.6.1.4.1.47477.999.1.1.0 | `e2e_gauge_test` |
| 1.3.6.1.4.1.47477.999.1.2.0 | `e2e_integer_test` |
| 1.3.6.1.4.1.47477.999.1.3.0 | `e2e_counter32_test` |
| 1.3.6.1.4.1.47477.999.1.4.0 | `e2e_counter64_test` |
| 1.3.6.1.4.1.47477.999.1.5.0 | `e2e_timeticks_test` |
| 1.3.6.1.4.1.47477.999.1.6.0 | `e2e_info_test` |
| 1.3.6.1.4.1.47477.999.1.7.0 | `e2e_ip_test` |
| 1.3.6.1.4.1.47477.999.4.4.0 | `e2e_command_response` |

Synthetic aggregate for E2E-SIM (from `simetra-devices.yaml`):
`AggregatedMetricName: "e2e_total_util"` — produces `resolved_name="e2e_total_util"` with `source="synthetic"`

### device_name label

**Code path:** `CommunityStringHelper.TryExtractDeviceName`:
```csharp
// community = "Simetra.E2E-SIM"
// CommunityPrefix = "Simetra."
deviceName = community[CommunityPrefix.Length..];  // → "E2E-SIM"
```

For E2E-SIM: community `"Simetra.E2E-SIM"` → `device_name="E2E-SIM"`. This is set before pipeline dispatch in `MetricPollJob` (poll path) and via `DeviceRegistry.TryGetByIpPort` in `CommandWorkerService` (command path).

For trap path: community string in trap PDU `"Simetra.E2E-SIM"` → `TryExtractDeviceName` → `device_name="E2E-SIM"`.

---

## Architecture Patterns

### Recommended Project Structure

```
tests/e2e/scenarios/
├── 94-mlc01-source-poll.sh        # MLC-01: source="poll"
├── 95-mlc02-source-trap.sh        # MLC-02: source="trap"
├── 96-mlc03-source-command.sh     # MLC-03: source="command"
├── 97-mlc04-source-synthetic.sh   # MLC-04: source="synthetic"
├── 98-mlc05-snmptype-all-gauge.sh # MLC-05: snmp_type for 5 numeric types
├── 99-mlc06-snmptype-string.sh    # MLC-06: snmp_type for 2 string types
├── 100-mlc07-resolved-name.sh     # MLC-07: resolved_name for all 7 mapped OIDs
└── 101-mlc08-device-name.sh       # MLC-08: device_name="E2E-SIM" for community
```

File numbering: scenarios continue from 94. File prefix 100 and 101 are valid (run-all.sh globs `[0-9]*.sh` which matches any numeric prefix).

### SCENARIO_RESULTS index tracking

Per the phase context, SCENARIO_RESULTS indices for Phase 70 scenarios start at **96** (0-based). MVC-08 (scenario 93) lands at index 95. Phase 70 scenarios produce entries at indices 96-103 (if each file produces 1 result) or more if multi-assertion loops are used.

Report category for Phase 70: add `"Label Correctness|96|103"` to `_REPORT_CATEGORIES` in `report.sh`.

### Pattern 1: Static Source Label Assertion (MLC-01, MLC-02)

Used for poll and trap source verification. Both sources should already be present in Prometheus since the system continuously polls and receives traps.

```bash
# Source: adapted from scenario 11 (tests/e2e/scenarios/11-gauge-labels-e2e-sim.sh)
SCENARIO_NAME="MLC-01: source=poll label on snmp_gauge (e2e_gauge_test)"

RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_gauge_test",source="poll"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -gt 0 ]; then
    ACTUAL_SOURCE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.source')
    EVIDENCE="source=${ACTUAL_SOURCE} resolved_name=e2e_gauge_test"
    if [ "$ACTUAL_SOURCE" = "poll" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "expected source=poll got ${ACTUAL_SOURCE}. ${EVIDENCE}"
    fi
else
    record_fail "$SCENARIO_NAME" "no snmp_gauge{source=poll} found for e2e_gauge_test"
fi
```

For trap (MLC-02), use `source="trap"` — but traps only fire every 30s so use a poll loop with 45s deadline (same as scenario 16):

```bash
# Source: adapted from scenario 16 (tests/e2e/scenarios/16-trap-originated.sh)
SCENARIO_NAME="MLC-02: source=trap label on snmp_gauge (e2e_gauge_test)"

DEADLINE=$(( $(date +%s) + 45 ))
RESULT=""
COUNT=0
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    RESULT=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_gauge_test",source="trap"}') || true
    COUNT=$(echo "$RESULT" | jq -r '.data.result | length' 2>/dev/null) || COUNT=0
    [ "$COUNT" -gt 0 ] && break
    sleep 3
done

if [ "$COUNT" -gt 0 ]; then
    ACTUAL_SOURCE=$(echo "$RESULT" | jq -r '.data.result[0].metric.source')
    EVIDENCE="source=${ACTUAL_SOURCE} resolved_name=e2e_gauge_test count=${COUNT}"
    if [ "$ACTUAL_SOURCE" = "trap" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "expected source=trap got ${ACTUAL_SOURCE}. ${EVIDENCE}"
    fi
else
    record_fail "$SCENARIO_NAME" "no snmp_gauge{source=trap} found for e2e_gauge_test within 45s"
fi
```

### Pattern 2: Command Source (MLC-03)

`source="command"` requires a SnapshotJob tier=4 dispatch. The CCV-01 fixture approach reuses the PSS single-tenant fixture. After dispatch, the SET response varbind is published as `SnmpSource.Command`. The OID `.999.4.4.0` (e2e_command_response, Integer32) is the command target (CommandName: `e2e_set_bypass`, Oid: `1.3.6.1.4.1.47477.999.4.4.0`, from simetra-oid-command-map.yaml).

Setup: same as CCV-01 (apply tenant-cfg06-pss-single.yaml, prime T2 OIDs, trigger tier=4 dispatch). Then poll for `snmp_gauge{resolved_name="e2e_command_response",source="command"}`.

```bash
# Source: adapted from scenario 83 (tests/e2e/scenarios/83-ccv01-command-dispatched.sh)
# After triggering tier=4 and waiting for command dispatch:
RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_command_response",source="command"}')
COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')
```

**Key fact:** The command response OID `.999.4.4.0` is Integer32 (writable per `WritableDynamicInstance`). After SET, the response varbind has `TypeCode=Integer32` and `Source=Command`. The resulting series is `snmp_gauge{resolved_name="e2e_command_response",source="command",snmp_type="integer32",device_name="E2E-SIM"}`.

### Pattern 3: Synthetic Source (MLC-04)

`source="synthetic"` is produced by `MetricPollJob.PublishAggregatedAsync`. The E2E-SIM devices.yaml has an aggregate poll group:
```json
{
  "Metrics": [{"MetricName": "e2e_agg_source_a"}, {"MetricName": "e2e_agg_source_b"}],
  "AggregatedMetricName": "e2e_total_util",
  "Aggregator": "sum"
}
```

This runs every 10s regardless of tenant config. No setup needed — `e2e_total_util` should always be present in Prometheus from steady-state polling.

```bash
# Source: devices.yaml aggregate poll group for E2E-SIM
RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_total_util",source="synthetic"}')
COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')
```

The synthetic metric OID is sentinel `"0.0"` (not a real OID, injected by `PublishAggregatedAsync`). The `oid` label in Prometheus for synthetic metrics will be `"0.0"`.

### Pattern 4: Multi-Type Loop (MLC-05, MLC-06)

MLC-05 verifies all 5 numeric snmp_type values. MLC-06 verifies the 2 string types. Both use a for-loop pattern like scenario 17. Each loop iteration produces 1 SCENARIO_RESULTS entry.

```bash
# Source: scenario 17 (tests/e2e/scenarios/17-snmp-type-labels.sh)
# MLC-05: 5 numeric types — produces 5 SCENARIO_RESULTS entries
CHECKS=(
    "e2e_gauge_test:gauge32:snmp_gauge"
    "e2e_integer_test:integer32:snmp_gauge"
    "e2e_counter32_test:counter32:snmp_gauge"
    "e2e_counter64_test:counter64:snmp_gauge"
    "e2e_timeticks_test:timeticks:snmp_gauge"
)
for CHECK in "${CHECKS[@]}"; do
    RESOLVED_NAME="${CHECK%%:*}"
    REST="${CHECK#*:}"
    EXPECTED_TYPE="${REST%%:*}"
    METRIC="${REST#*:}"
    SCENARIO_NAME="MLC-05: snmp_type=${EXPECTED_TYPE} for ${RESOLVED_NAME}"

    RESPONSE=$(query_prometheus "${METRIC}{device_name=\"E2E-SIM\",resolved_name=\"${RESOLVED_NAME}\"}")
    COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')
    if [ "$COUNT" -gt 0 ]; then
        ACTUAL=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.snmp_type')
        if [ "$ACTUAL" = "$EXPECTED_TYPE" ]; then
            record_pass "$SCENARIO_NAME" "snmp_type=${ACTUAL}"
        else
            record_fail "$SCENARIO_NAME" "expected ${EXPECTED_TYPE} got ${ACTUAL}"
        fi
    else
        record_fail "$SCENARIO_NAME" "no ${METRIC} for ${RESOLVED_NAME}"
    fi
done
```

For MLC-06 (string types), query `snmp_info` instead:
```bash
CHECKS=(
    "e2e_info_test:octetstring"
    "e2e_ip_test:ipaddress"
)
```

### Pattern 5: Resolved Name Verification (MLC-07)

Query all 7 mapped OIDs and assert `resolved_name` matches exactly. Uses a loop. Each iteration produces 1 SCENARIO_RESULTS entry (7 total from this file).

```bash
# 7 entries: e2e_gauge_test, e2e_integer_test, e2e_counter32_test, e2e_counter64_test,
#            e2e_timeticks_test, e2e_info_test, e2e_ip_test
OID_METRIC_PAIRS=(
    "1.3.6.1.4.1.47477.999.1.1.0:e2e_gauge_test:snmp_gauge"
    "1.3.6.1.4.1.47477.999.1.2.0:e2e_integer_test:snmp_gauge"
    "1.3.6.1.4.1.47477.999.1.3.0:e2e_counter32_test:snmp_gauge"
    "1.3.6.1.4.1.47477.999.1.4.0:e2e_counter64_test:snmp_gauge"
    "1.3.6.1.4.1.47477.999.1.5.0:e2e_timeticks_test:snmp_gauge"
    "1.3.6.1.4.1.47477.999.1.6.0:e2e_info_test:snmp_info"
    "1.3.6.1.4.1.47477.999.1.7.0:e2e_ip_test:snmp_info"
)
for ENTRY in "${OID_METRIC_PAIRS[@]}"; do
    OID="${ENTRY%%:*}"
    REST="${ENTRY#*:}"
    EXPECTED_NAME="${REST%%:*}"
    METRIC="${REST#*:}"
    SCENARIO_NAME="MLC-07: resolved_name=${EXPECTED_NAME} for oid=${OID}"
    # Query by oid label to verify the mapping itself
    RESPONSE=$(query_prometheus "${METRIC}{device_name=\"E2E-SIM\",oid=\"${OID}\"}")
    COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')
    if [ "$COUNT" -gt 0 ]; then
        ACTUAL=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.resolved_name')
        if [ "$ACTUAL" = "$EXPECTED_NAME" ]; then
            record_pass "$SCENARIO_NAME" "resolved_name=${ACTUAL}"
        else
            record_fail "$SCENARIO_NAME" "expected ${EXPECTED_NAME} got ${ACTUAL}"
        fi
    else
        record_fail "$SCENARIO_NAME" "no ${METRIC} for oid=${OID}"
    fi
done
```

### Pattern 6: Device Name Verification (MLC-08)

Assert `device_name="E2E-SIM"` for all 7 mapped OIDs. Simple existence check with label filter. Single-assertion variant (not a loop) per scenario file, asserting on one representative OID.

```bash
SCENARIO_NAME="MLC-08: device_name=E2E-SIM (community Simetra.E2E-SIM)"
RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_gauge_test"}')
COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')
if [ "$COUNT" -gt 0 ]; then
    ACTUAL=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.device_name')
    if [ "$ACTUAL" = "E2E-SIM" ]; then
        record_pass "$SCENARIO_NAME" "device_name=${ACTUAL}"
    else
        record_fail "$SCENARIO_NAME" "expected E2E-SIM got ${ACTUAL}"
    fi
else
    record_fail "$SCENARIO_NAME" "no snmp_gauge for e2e_gauge_test"
fi
```

### Anti-Patterns to Avoid

- **Duplicating existing scenarios 11, 16, 17:** Those already check labels for poll/trap/snmp_type. Phase 70 scenarios must add new assertions (exact source per label, all 4 source variants, resolved_name/OID cross-check, device_name explicit). Do not re-run the same queries.
- **Asserting source with snmp_gauge when querying snmp_info types:** OctetString and IpAddress produce `snmp_info`, not `snmp_gauge`. MLC-06 must query `snmp_info`.
- **MLC-03 without tenant fixture:** source="command" is only produced when a tier=4 dispatch fires. Without a tenant config that causes dispatch, the label will never appear. Reuse the CCV-01 setup pattern.
- **MLC-04 forgetting that oid="0.0":** Synthetic metrics have a sentinel OID. Do not try to look up `snmp_gauge{oid="1.3.6.1.4.1.47477.999.4.4.0",source="synthetic"}` — that OID is the poll source. Synthetic is always `oid="0.0"`.
- **Wrong report.sh index range:** MLC-05 produces 5 SCENARIO_RESULTS entries and MLC-07 produces 7. This affects the end index for the "Label Correctness" report category. Calculate precisely.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Wait for trap | custom sleep | poll loop with 45s deadline (pattern in scenario 16) | Trap interval is 30s, need buffer |
| Trigger command dispatch | manual SNMP SET | reuse CCV-01 tenant fixture + priming | Exact same flow, proven pattern |
| OID suffix to full path | manual prefix | use full OID in query or `sim_set_oid` suffix | Already documented in Phase 69 research |
| Count results | jq custom | `.data.result \| length` | Standard across all scenarios |

---

## Common Pitfalls

### Pitfall 1: source="command" Series Not Present Without Dispatch

**What goes wrong:** Query for `snmp_gauge{source="command"}` returns 0 results.

**Why it happens:** CommandWorkerService only publishes `SnmpSource.Command` when a tier=4 dispatch fires AND the SET is acknowledged. If no tenant config is active or no tier=4 fires, the series never appears.

**How to avoid:** MLC-03 must trigger tier=4. Use `tenant-cfg06-pss-single.yaml` (same as CCV-01). Set T2 evaluate OID to violated state. Wait for dispatch signal (poll `snmp_command_dispatched_total` increment, then query command source series).

**Warning signs:** `snmp_gauge{source="command"}` returns no results even after waiting 30s.

### Pitfall 2: Synthetic Metric OID is Sentinel "0.0"

**What goes wrong:** Querying `snmp_gauge{oid="1.3.6.1.4.1.47477.999.4.4.0",source="synthetic"}` returns nothing.

**Why it happens:** `PublishAggregatedAsync` hardcodes `Oid = "0.0"` for all synthetic metrics (MetricPollJob.cs line 259). This sentinel is valid for the validation behavior regex but is NOT the real OID of any source metric.

**How to avoid:** Query synthetic by `resolved_name="e2e_total_util"` and `source="synthetic"`. Do not filter by oid.

### Pitfall 3: Multi-Entry Loop Scenarios Inflate SCENARIO_RESULTS Count

**What goes wrong:** Report category index range is wrong, causing scenarios to appear under the wrong category in the report.

**Why it happens:** MLC-05 (5 loop iterations) and MLC-07 (7 loop iterations) each append multiple entries to SCENARIO_RESULTS. The 8 scenario files do not produce 8 entries; they produce more.

**How to avoid:** Count entries per file:
- 94-mlc01-source-poll: 1 entry
- 95-mlc02-source-trap: 1 entry
- 96-mlc03-source-command: 1 entry (or 2 if setup/cleanup assertions included)
- 97-mlc04-source-synthetic: 1 entry
- 98-mlc05-snmptype-all-gauge: 5 entries (loop over 5 types)
- 99-mlc06-snmptype-string: 2 entries (loop over 2 types)
- 100-mlc07-resolved-name: 7 entries (loop over 7 OIDs)
- 101-mlc08-device-name: 1 entry

Total Phase 70 entries: 1+1+1+1+5+2+7+1 = 19 entries, indices 96-114.

Report category: `"Label Correctness|96|114"`

### Pitfall 4: MLC-02 Overlaps with Scenario 16

**What goes wrong:** MLC-02 is identical to scenario 16.

**Why it happens:** Scenario 16 already checks `source="trap"` with a 45s poll loop for the same OID.

**How to avoid:** MLC-02 should add something scenario 16 doesn't assert — specifically the explicit label check that `source` equals exactly `"trap"` AND that `resolved_name` and `device_name` are correct simultaneously. Scenario 16 already does check source="trap" by including it in the PromQL filter. The distinction: MLC-02 can be a direct static check if trap data is already in Prometheus from scenario 16 having just run. If run-all.sh runs scenarios in order, scenario 16 ran before 95-mlc02, so trap data is already present — no poll loop needed.

**Warning signs:** If MLC-02 just repeats scenario 16 exactly, it adds no value. Ensure it explicitly reads and logs the `source` label from the result.

### Pitfall 5: MLC-03 Needs Tenant Fixture Cleanup

**What goes wrong:** After MLC-03 leaves the CCV tenant config active, subsequent MLC scenarios fail because tenant metrics produce unexpected command dispatches.

**How to avoid:** MLC-03 must save and restore the tenant ConfigMap before/after (same pattern as scenarios 83-85). Use `save_configmap` / `restore_configmap` from kubectl.sh.

---

## Code Examples

### Query snmp_gauge by source label

```bash
# Source: scenario 11 pattern adapted
RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_gauge_test",source="poll"}')
COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')
ACTUAL_SOURCE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.source')
```

### Query snmp_gauge for synthetic (note oid="0.0")

```bash
# Source: MetricPollJob.cs PublishAggregatedAsync — oid sentinel is "0.0"
RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_total_util",source="synthetic"}')
COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')
ACTUAL_SOURCE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.source')
ACTUAL_OID=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.oid')
# ACTUAL_OID == "0.0"
```

### Trigger command dispatch and verify source="command"

```bash
# Source: scenario 83 (83-ccv01-command-dispatched.sh) — abbreviated
FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-single.yaml" > /dev/null 2>&1 || true
# ... prime and trigger tier=4 ...
RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_command_response",source="command"}')
# ... cleanup: reset_oid_overrides + restore_configmap ...
```

### Verify resolved_name from oid label (cross-reference)

```bash
# Source: OID map fixture (.original-oid-metric-map-configmap.yaml) confirmed full OIDs with .0 suffix
RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",oid="1.3.6.1.4.1.47477.999.1.1.0"}')
ACTUAL=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.resolved_name')
# ACTUAL == "e2e_gauge_test"
```

---

## OID and Metric Reference for Phase 70

### Source Labels — What to Query

| MLC scenario | source label | Prometheus metric | resolved_name | Notes |
|-------------|--------------|-------------------|---------------|-------|
| MLC-01 | `"poll"` | snmp_gauge | e2e_gauge_test | always present |
| MLC-02 | `"trap"` | snmp_gauge | e2e_gauge_test | poll loop 45s, traps every 30s |
| MLC-03 | `"command"` | snmp_gauge | e2e_command_response | requires tenant fixture + tier=4 |
| MLC-04 | `"synthetic"` | snmp_gauge | e2e_total_util | always present, oid="0.0" |

### snmp_type Labels — All 7 Types

| MLC scenario | snmp_type | resolved_name | Prometheus metric |
|-------------|-----------|---------------|-------------------|
| MLC-05 (x5) | gauge32 | e2e_gauge_test | snmp_gauge |
| MLC-05 (x5) | integer32 | e2e_integer_test | snmp_gauge |
| MLC-05 (x5) | counter32 | e2e_counter32_test | snmp_gauge |
| MLC-05 (x5) | counter64 | e2e_counter64_test | snmp_gauge |
| MLC-05 (x5) | timeticks | e2e_timeticks_test | snmp_gauge |
| MLC-06 (x2) | octetstring | e2e_info_test | snmp_info |
| MLC-06 (x2) | ipaddress | e2e_ip_test | snmp_info |

### resolved_name Labels — Full OID Cross-reference (MLC-07)

Same as Phase 69 value reference table. All 7 mapped .999.1.x OIDs. Query by `oid` label to verify the OID-to-name mapping holds in Prometheus.

---

## Report Category Update

Current last category in `tests/e2e/lib/report.sh`:
```bash
"Business Metric Value Correctness|88|95"
```

Phase 70 adds new category. Start index = 96 (first entry after MVC-08 at index 95). End index depends on multi-assertion loops:

Phase 70 produces entries: 1+1+1+1+5+2+7+1 = **19 entries** (indices 96 through 114 inclusive).

New entry to add:
```bash
"Label Correctness|96|114"
```

**Important:** If MLC-03 (command) uses 2 record calls (one for setup verification, one for main assertion), adjust the count. Recommend keeping all MLC scenarios to single-assertion per flow for predictability.

---

## State of the Art

| Old Coverage | Phase 70 Coverage | Impact |
|-------------|-------------------|--------|
| Scenario 11: labels present for poll source | MLC-01: explicit source="poll" exact value check | Tighter — asserts the exact string value, not just label presence |
| Scenario 16: trap label check (source="trap" in PromQL filter) | MLC-02: explicit source read and asserted from result | Tighter — reads label from metric metadata, not just query filter |
| No command source check | MLC-03: source="command" on command response | New coverage |
| No synthetic source check | MLC-04: source="synthetic" on e2e_total_util | New coverage |
| Scenario 17: 5 numeric types only | MLC-05+06: all 7 types including 2 string types | Completes coverage |
| No resolved_name/OID cross-reference | MLC-07: oid label → resolved_name cross-check | New coverage |
| No device_name explicit assertion | MLC-08: device_name="E2E-SIM" from community | Explicit coverage |

---

## Open Questions

1. **MLC-03: Use poll loop or just one shot after confirmed dispatch?**
   - What we know: After tier=4 fires, CommandWorkerService sends SET and publishes the response varbind. The series may take 15s to appear in Prometheus.
   - What's unclear: Whether to poll for `snmp_gauge{source="command"}` or to assert after knowing `snmp_command_dispatched_total` incremented.
   - Recommendation: Poll for `snmp_gauge{source="command",resolved_name="e2e_command_response"}` with a 30s deadline after confirming dispatch counter incremented. Mirrors the CCV pattern.

2. **MLC-02: Poll loop or assume data is present from scenario 16?**
   - What we know: Scenario 16 ran ~10 scenarios earlier in run-all.sh and already polled for trap data. Prometheus retains time series for at least the retention window (default 15 days).
   - What's unclear: Whether the trap series is still present at scenario 95's execution time.
   - Recommendation: Use a short poll loop (15s deadline, 3s interval) as a safety net. If data is in Prometheus, it returns immediately; if not, it waits for the next trap.

3. **MLC-07: Query by oid label vs resolved_name label**
   - What we know: Both `oid` and `resolved_name` are labels on snmp_gauge/snmp_info. Querying by `oid` verifies the mapping; querying by `resolved_name` verifies you can filter by name.
   - Recommendation: MLC-07 should query by `oid` label to cross-reference that the OID-to-name mapping is correct. This is distinct from all prior scenarios that query by `resolved_name`.

---

## Sources

### Primary (HIGH confidence)

- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` — `source = notification.Source.ToString().ToLowerInvariant()` (line 45); `snmpType = notification.TypeCode.ToString().ToLowerInvariant()` (line 60); confirmed both label derivations
- `src/SnmpCollector/Pipeline/SnmpSource.cs` — enum members Poll, Trap, Synthetic, Command → "poll", "trap", "synthetic", "command"
- `src/SnmpCollector/Pipeline/CommunityStringHelper.cs` — `TryExtractDeviceName("Simetra.E2E-SIM")` → `"E2E-SIM"`
- `src/SnmpCollector/Services/CommandWorkerService.cs` — `Source = SnmpSource.Command` (line 185) for SET response varbinds
- `src/SnmpCollector/Jobs/MetricPollJob.cs` — `Source = SnmpSource.Synthetic` (line 263) for aggregate metrics; `Oid = "0.0"` sentinel (line 259)
- `simulators/e2e-sim/e2e_simulator.py` — MAPPED_OIDS: 7 OIDs with types Gauge32/Integer32/Counter32/Counter64/TimeTicks/OctetString/IpAddress; TEST_OIDS: .999.4.4 is Integer32 writable (e2e_command_response); TRAP_OID = `{E2E_PREFIX}.3.1`, trap varbind uses GAUGE_OID = `.999.1.1.0`
- `tests/e2e/fixtures/.original-oid-metric-map-configmap.yaml` — all OID-to-MetricName mappings confirmed; `.999.4.4.0` → `e2e_command_response`
- `deploy/k8s/snmp-collector/simetra-oid-command-map.yaml` — `e2e_set_bypass` command maps to `1.3.6.1.4.1.47477.999.4.4.0`
- `deploy/k8s/snmp-collector/simetra-devices.yaml` — E2E-SIM aggregate poll group: `AggregatedMetricName: "e2e_total_util"`, Aggregator: sum, sources: e2e_agg_source_a + e2e_agg_source_b
- `tests/e2e/lib/report.sh` — current last category `"Business Metric Value Correctness|88|95"` — Phase 70 adds `"Label Correctness|96|114"`
- `tests/e2e/scenarios/16-trap-originated.sh` — poll-with-deadline pattern for trap source; confirms `source="trap"` and `snmp_type="gauge32"` already asserted
- `tests/e2e/scenarios/17-snmp-type-labels.sh` — for-loop pattern for multi-type assertions; confirms gauge32/integer32/counter32/counter64/timeticks already verified
- `tests/e2e/scenarios/83-ccv01-command-dispatched.sh` — full command dispatch setup/cleanup pattern; tenant fixture, OID priming, dispatch wait, restore

### Secondary (MEDIUM confidence)

- `tests/e2e/scenarios/14-info-labels.sh` — confirms `snmp_type="octetstring"` and `snmp_type="ipaddress"` label values for snmp_info; confirms `INFO_VALUE` extraction via `.data.result[0].metric.value`
- `tests/e2e/scenarios/11-gauge-labels-e2e-sim.sh` — base pattern for static label assertions

---

## Metadata

**Confidence breakdown:**
- source label values (poll/trap/command/synthetic): HIGH — read directly from SnmpSource.cs enum + OtelMetricHandler.cs `.ToString().ToLowerInvariant()` transform
- snmp_type label values (all 7 types): HIGH — OtelMetricHandler.cs + scenario 14/17 already confirm these exact strings
- resolved_name → OID mapping: HIGH — oid-metric-map-configmap.yaml confirmed
- device_name derivation: HIGH — CommunityStringHelper.TryExtractDeviceName source confirmed
- command OID (.999.4.4 → e2e_command_response): HIGH — oid-command-map.yaml + oid-metric-map confirmed
- synthetic metric OID sentinel "0.0": HIGH — MetricPollJob.cs line 259 confirmed
- SCENARIO_RESULTS start index (96): MEDIUM — derived from phase context statement; actual count depends on all prior scenario runs
- Report category end index (114): MEDIUM — depends on exact assertion counts per file; 19-entry total is computed from planned single-vs-loop structure

**Research date:** 2026-03-22
**Valid until:** 2026-04-22 (stable codebase; simulator types and OID mappings are constants)
