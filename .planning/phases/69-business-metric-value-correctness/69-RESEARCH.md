# Phase 69: Business Metric Value Correctness - Research

**Researched:** 2026-03-22
**Domain:** E2E test scenarios — SNMP value correctness in Prometheus
**Confidence:** HIGH

## Summary

Phase 69 adds E2E scenarios (86–N) that assert exact numeric and string values for every mapped .999.1.x OID reach Prometheus correctly. The research confirms the full value path: simulator scenario dict → SNMP GET response → ValueExtractionBehavior (raw conversion, no transformation) → OtelMetricHandler → SnmpMetricFactory.RecordGauge / RecordInfo → Prometheus snmp_gauge / snmp_info.

The standard pattern is: query Prometheus for the series, extract `.data.result[0].value[1]` (numeric value) or `.data.result[0].metric.value` (info label), compare to the known simulator default. For the value-change scenario (MVC-08), use `sim_set_oid` with the OID suffix, wait one poll cycle (10 s) plus OTel export latency (up to 15 s), then re-query and assert. The HTTP endpoint only accepts integer values — string OIDs (.999.1.6, .999.1.7) cannot be overridden at runtime via the HTTP endpoint; their values are fixed at "E2E-TEST-VALUE" and "10.0.0.1" from the scenario dict.

**Primary recommendation:** Model new scenarios after scenario 11 (static query + exact numeric check). For MVC-08, follow the poll-with-deadline pattern from scenario 18 with a 40 s deadline (10 s poll + 15 s OTel + 15 s buffer).

---

## Standard Stack

### Core (already present — no new installs)
| Component | Version | Purpose |
|-----------|---------|---------|
| `tests/e2e/lib/common.sh` | in-repo | `record_pass`, `record_fail`, `assert_delta_eq`, `assert_delta_ge` |
| `tests/e2e/lib/prometheus.sh` | in-repo | `query_prometheus`, `query_counter`, `poll_until` |
| `tests/e2e/lib/sim.sh` | in-repo | `sim_set_oid`, `reset_oid_overrides` |
| `tests/e2e/lib/report.sh` | in-repo | `_REPORT_CATEGORIES` array, `generate_report` |

### No new dependencies needed.

---

## Architecture Patterns

### Recommended Project Structure

New scenario files follow:
```
tests/e2e/scenarios/
├── 86-mvc01-gauge32-exact.sh
├── 87-mvc02-integer32-exact.sh
├── 88-mvc03-counter32-exact.sh
├── 89-mvc04-counter64-exact.sh
├── 90-mvc05-timeticks-exact.sh
├── 91-mvc06-info-octetstring-exact.sh
├── 92-mvc07-info-ipaddress-exact.sh
└── 93-mvc08-value-change.sh
```

Scenarios start at 86 (85 is the last existing scenario).

### Pattern 1: Static Exact-Value Assertion (snmp_gauge)

Used for MVC-01 through MVC-05 (numeric types).

```bash
# Source: scenario 11 (tests/e2e/scenarios/11-gauge-labels-e2e-sim.sh) — adapted for exact value check
SCENARIO_NAME="MVC-01: snmp_gauge Gauge32 exact value (e2e_gauge_test)"

RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_gauge_test"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -eq 0 ]; then
    record_fail "$SCENARIO_NAME" "no snmp_gauge series found for e2e_gauge_test"
else
    VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].value[1]')
    VALUE_INT=$(echo "$VALUE" | cut -d. -f1)
    EVIDENCE="resolved_name=e2e_gauge_test value=${VALUE}"

    if [ "$VALUE_INT" = "42" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "expected 42 got ${VALUE_INT}. ${EVIDENCE}"
    fi
fi
```

### Pattern 2: Static Exact-Value Assertion (snmp_info)

Used for MVC-06 (OctetString) and MVC-07 (IpAddress).

```bash
# Source: scenario 14 (tests/e2e/scenarios/14-info-labels.sh) — adapted for exact value check
SCENARIO_NAME="MVC-06: snmp_info OctetString exact value (e2e_info_test)"

RESPONSE=$(query_prometheus 'snmp_info{device_name="E2E-SIM",resolved_name="e2e_info_test"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -eq 0 ]; then
    record_fail "$SCENARIO_NAME" "no snmp_info series found for e2e_info_test"
else
    INFO_VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.value')
    PROM_VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].value[1]')
    EVIDENCE="value_label=${INFO_VALUE} prom_value=${PROM_VALUE}"

    if [ "$INFO_VALUE" = "E2E-TEST-VALUE" ] && [ "$PROM_VALUE" = "1" ]; then
        record_pass "$SCENARIO_NAME" "$EVIDENCE"
    else
        record_fail "$SCENARIO_NAME" "expected E2E-TEST-VALUE/1, got ${EVIDENCE}"
    fi
fi
```

### Pattern 3: Value-Change with Poll Loop (MVC-08)

Used for MVC-08. Sets gauge OID to new value via HTTP, polls Prometheus until it reflects it.

```bash
# Source: scenario 18 (tests/e2e/scenarios/18-oid-rename.sh) — adapted for value change
SCENARIO_NAME="MVC-08: snmp_gauge value updates within one poll cycle (42->99)"

# Override .999.1.1 to 99 (OID suffix "1.1" relative to E2E_PREFIX)
sim_set_oid "1.1" "99"

# Poll up to 40s (10s poll interval + 15s OTel export + 15s buffer)
DEADLINE=$(( $(date +%s) + 40 ))
FOUND=0
FINAL_VALUE=""
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    RESULT=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_gauge_test"}') || true
    VAL=$(echo "$RESULT" | jq -r '.data.result[0].value[1]' 2>/dev/null) || VAL=""
    VAL_INT=$(echo "$VAL" | cut -d. -f1)
    if [ "$VAL_INT" = "99" ]; then
        FOUND=1
        FINAL_VALUE="$VAL"
        break
    fi
    sleep 3
done

# Restore: clear override so subsequent scenarios see value=42 again
reset_oid_overrides

if [ "$FOUND" -eq 1 ]; then
    record_pass "$SCENARIO_NAME" "value updated to 99 within poll cycle. final_value=${FINAL_VALUE}"
else
    record_fail "$SCENARIO_NAME" "value did not update to 99 within 40s. last_value=${FINAL_VALUE}"
fi
```

### Anti-Patterns to Avoid

- **Asserting float equality with `=`:** Prometheus returns values as floating-point strings (e.g., `"42"`). Use `cut -d. -f1` to extract integer part, then compare as integer. Counter64 can be very large but still arrives as a plain decimal float string.
- **Calling `reset_oid_overrides` before snapshotting:** Always call `reset_oid_overrides` in cleanup AFTER assertions, never before. Otherwise all .999.4.x / .999.5.x OIDs used by other tests also get cleared mid-suite.
- **Omitting cleanup for OID overrides in MVC-08:** If `sim_set_oid "1.1" "99"` is not cleaned up, subsequent scenarios 11/17 expecting value=42 will fail.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Wait for value propagation | custom sleep | poll loop with deadline (pattern 3) | Avoids hard sleeps; deterministic |
| Extract int from Prometheus float | string arithmetic | `cut -d. -f1` then integer `[ ]` comparison | Handles "42" and "42.0" transparently |
| OID suffix to full OID | manual concatenation | `sim_set_oid "1.1" "99"` — sim.sh handles prefix | Already implemented in sim.sh |

---

## Common Pitfalls

### Pitfall 1: HTTP /oid/{value} Only Accepts Integers

**What goes wrong:** `sim_set_oid "1.6" "NEW-STRING"` fails because `post_oid_value` calls `int(request.match_info["value"])`, which raises `ValueError` for non-integer strings.

**Why it happens:** The HTTP endpoint is designed for numeric overrides. The `getSyntax().clone(integer_value)` call works for OctetString by converting the int to bytes, but will not produce the string `"NEW-STRING"`.

**How to avoid:** MVC-06 (OctetString) and MVC-07 (IpAddress) are static-only assertions. They verify the default scenario values `"E2E-TEST-VALUE"` and `"10.0.0.1"` respectively. Do not attempt runtime string override for MVC-08.

**For MVC-08:** Use the Gauge32 OID (.999.1.1 = e2e_gauge_test) for the value-change test. This is the cleanest, most reliable choice.

### Pitfall 2: OID Full-Path vs Suffix in sim_set_oid

**What goes wrong:** Calling `sim_set_oid "1.3.6.1.4.1.47477.999.1.1" "99"` or `sim_set_oid ".999.1.1" "99"` — the sim prepends `E2E_PREFIX` unconditionally: `full_oid = f"{E2E_PREFIX}.{oid_suffix.lstrip('.')}"`. So `"1.3.6.1.4.1.47477.999.1.1"` becomes `1.3.6.1.4.1.47477.999.1.3.6.1.4.1.47477.999.1.1` which is wrong.

**How to avoid:** Always pass the suffix relative to `1.3.6.1.4.1.47477.999.` — e.g., `"1.1"` for `.999.1.1`. The existing scenarios 29-68 all use short suffixes like `"4.1"`, `"5.1"`, etc.

### Pitfall 3: Counter32/Counter64 Are Recorded as Raw Gauges (not rates)

**What goes wrong:** Expecting `snmp_gauge{resolved_name="e2e_counter32_test"}` to show a rate or delta.

**Why it happens:** `OtelMetricHandler` handles all 5 numeric SNMP types (`Integer32`, `Gauge32`, `TimeTicks`, `Counter32`, `Counter64`) identically by calling `RecordGauge` with the raw extracted value. There is no rate/delta applied. The OTel instrument is `Gauge<double>`.

**How to avoid:** MVC-03 (Counter32) asserts value = 5000; MVC-04 (Counter64) asserts value = 1000000. Compare with `cut -d. -f1` for robustness.

### Pitfall 4: OID Note — The Full OID in Prometheus Has Trailing `.0`

**What goes wrong:** Querying `oid="1.3.6.1.4.1.47477.999.1.1"` (no `.0`) returns 0 results.

**Why it happens:** The SNMP OID for a scalar instance always ends in `.0`. The oidmap and all SNMP poll results use `1.3.6.1.4.1.47477.999.1.1.0`. Scenario 11 confirms: `OID=$(echo … .metric.oid)` is `1.3.6.1.4.1.47477.999.1.1.0`.

**How to avoid:** PromQL label filter by `resolved_name` rather than `oid` — that is simpler and avoids the trailing `.0` issue entirely.

### Pitfall 5: Report Category Indexing

**What goes wrong:** Adding a new category to `_REPORT_CATEGORIES` in `report.sh` with wrong 0-based start index causes scenarios to be mis-attributed.

**Why it happens:** Categories use 0-based SCENARIO_RESULTS indices. The last existing category is `"Command Counter Verification|82|88"` = 0-based indices 82–88, meaning scenarios 83–89 (1-based). Phase 69 scenarios are 0-based indices 85–92 (scenarios 86–93).

**How to avoid:** See the "Report Category" section below.

---

## Code Examples

### Query snmp_gauge by resolved_name, extract exact numeric value

```bash
# Source: tests/e2e/scenarios/11-gauge-labels-e2e-sim.sh
RESPONSE=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",resolved_name="e2e_gauge_test"}')
VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].value[1]')
VALUE_INT=$(echo "$VALUE" | cut -d. -f1)
# Compare: [ "$VALUE_INT" = "42" ]
```

### Query snmp_info for string value label

```bash
# Source: tests/e2e/scenarios/14-info-labels.sh
RESPONSE=$(query_prometheus 'snmp_info{device_name="E2E-SIM",resolved_name="e2e_info_test"}')
INFO_VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].metric.value')
PROM_VALUE=$(echo "$RESPONSE" | jq -r '.data.result[0].value[1]')
# INFO_VALUE == "E2E-TEST-VALUE", PROM_VALUE == "1"
```

### Set OID override and restore

```bash
# Source: tests/e2e/lib/sim.sh
sim_set_oid "1.1" "99"      # sets 1.3.6.1.4.1.47477.999.1.1 = 99
# ... wait and assert ...
reset_oid_overrides          # clear all overrides, revert to default scenario
```

---

## Value Reference: Default Scenario Values

All 7 mapped .999.1.x OIDs in the `default` scenario:

| OID Suffix | Full OID | MetricName | SNMP Type | Default Value | Prometheus Metric | Expected Value in Prometheus |
|------------|----------|------------|-----------|---------------|-------------------|------------------------------|
| .999.1.1 | 1.3.6.1.4.1.47477.999.1.1.0 | e2e_gauge_test | gauge32 | 42 | snmp_gauge | 42 |
| .999.1.2 | 1.3.6.1.4.1.47477.999.1.2.0 | e2e_integer_test | integer32 | 100 | snmp_gauge | 100 |
| .999.1.3 | 1.3.6.1.4.1.47477.999.1.3.0 | e2e_counter32_test | counter32 | 5000 | snmp_gauge | 5000 |
| .999.1.4 | 1.3.6.1.4.1.47477.999.1.4.0 | e2e_counter64_test | counter64 | 1000000 | snmp_gauge | 1000000 |
| .999.1.5 | 1.3.6.1.4.1.47477.999.1.5.0 | e2e_timeticks_test | timeticks | 360000 | snmp_gauge | 360000 |
| .999.1.6 | 1.3.6.1.4.1.47477.999.1.6.0 | e2e_info_test | octetstring | "E2E-TEST-VALUE" | snmp_info | value label = "E2E-TEST-VALUE", numeric = 1 |
| .999.1.7 | 1.3.6.1.4.1.47477.999.1.7.0 | e2e_ip_test | ipaddress | "10.0.0.1" | snmp_info | value label = "10.0.0.1", numeric = 1 |

---

## Report Category

Current `_REPORT_CATEGORIES` in `tests/e2e/lib/report.sh`:

```bash
_REPORT_CATEGORIES=(
    "Pipeline Counters|0|9"
    "Business Metrics|10|22"
    "OID Mutations|23|25"
    "Device Lifecycle|26|27"
    "Snapshot Evaluation|28|39"
    "Snapshot State Suite|40|51"
    "Progressive Snapshot Suite|52|67"
    "Pipeline Counter Verification|68|81"
    "Command Counter Verification|82|88"
)
```

The last category ends at index 88 (0-based) = scenario 89 (1-based). Phase 69 starts at scenario 86 (0-based index 85). The 3 scenarios 86–88 fall inside the existing `"Command Counter Verification|82|88"` range — those are the 3 existing CCV scenarios (83, 84, 85).

Phase 69 adds scenarios 86–93 (8 scenarios, 0-based indices 85–92). To avoid overrunning the last category, add a new category entry:

```bash
"Business Metric Value Correctness|89|96"
```

(Using a slightly wider range to accommodate up to 8 sub-scenarios, all 1-indexed 90–97. The exact end index depends on whether MVC-08 uses 1 or 2 sub-assertions.)

**Important note on sub-assertions:** Scenarios 83–85 (CCV-01 through CCV-04) each call `record_pass`/`record_fail` multiple times. Each call appends one entry to `SCENARIO_RESULTS`. If Phase 69 scenarios use sub-assertions (e.g., 93a, 93b), they each add to SCENARIO_RESULTS independently. Plan the category end index accordingly.

The simplest approach: one `record_pass`/`record_fail` call per scenario file, giving exactly 8 results (indices 85–92). Then add:

```bash
"Business Metric Value Correctness|85|92"
```

---

## Timing Analysis: Value Change Propagation (MVC-08)

For MVC-08, the path from simulator HTTP override to Prometheus:

| Step | Time |
|------|------|
| HTTP `POST /oid/1.1/99` completes | immediate |
| Next SNMP poll of .999.1.x group (IntervalSeconds=10) | 0–10 s |
| OTel collector scrape interval (from config) | 0–15 s |
| Prometheus scrape interval (from config) | 0–5 s |
| **Worst case** | **~30 s** |

**Recommendation:** Use a 40 s deadline with 3 s poll interval. This matches the pattern in scenario 18 (60 s deadline for ConfigMap propagation, which is slower). The value-change path is faster since it doesn't require ConfigMap reload.

---

## State of the Art

| Old Pattern | Current Pattern | Impact |
|-------------|----------------|--------|
| Scenarios 11–17 check labels and "is numeric" | Phase 69 checks exact expected value | Tighter correctness guarantee |
| No value-change scenarios existed | MVC-08 adds runtime mutation test | Closes gap on dynamic value propagation |

---

## Open Questions

1. **IpAddress formatting in snmp_info value label**
   - What we know: Simulator has `"10.0.0.1"` as the IpAddress value; `ValueExtractionBehavior` calls `msg.Value.ToString()` on the pysnmp IpAddress object.
   - What's unclear: Does SharpSnmpLib's `IpAddress.ToString()` return `"10.0.0.1"` or something like `"IpAddress: 10.0.0.1"`?
   - Recommendation: Scenario 14 currently only checks `INFO_VALUE` is non-empty and non-null, not the exact string. MVC-07 should start with an existence check, then log the actual value as evidence before asserting exact equality. If the format is different, the scenario will catch it.

2. **OctetString HTTP override integer encoding**
   - What we know: `_oid_overrides[full_oid] = int(value)` stores integer; `getSyntax().clone(int_val)` on an OctetString converts int to bytes representation.
   - What's unclear: What exact string Prometheus shows if someone accidentally overrides e2e_info_test with `sim_set_oid "1.6" "0"`.
   - Recommendation: Do not call `sim_set_oid` on .999.1.6 or .999.1.7. MVC-06 and MVC-07 are read-only assertions of default scenario values.

---

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` — RecordGauge records raw `double value` with no transformation; RecordInfo records string with 128-char truncation; snmp_info numeric value is always `1.0`
- `src/SnmpCollector/Pipeline/Behaviors/ValueExtractionBehavior.cs` — all 5 numeric types use raw `.ToInt32()` / `.ToUInt32()` / `.ToUInt64()` casts; no scaling or transformation
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` — all 5 numeric SnmpTypes → `RecordGauge(ExtractedValue)`; OctetString/IPAddress/ObjectIdentifier → `RecordInfo(ExtractedStringValue)`
- `simulators/e2e-sim/e2e_simulator.py` — complete simulator source; HTTP `/oid/{oid}/{value}` accepts `int()` only; default scenario values for all 7 .999.1.x OIDs confirmed
- `deploy/k8s/snmp-collector/simetra-devices.yaml` — E2E-SIM .999.1.x poll group uses `IntervalSeconds: 10`
- `deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml` — all 7 .999.1.x OIDs mapped to e2e_*_test metric names confirmed
- `tests/e2e/lib/report.sh` — existing `_REPORT_CATEGORIES` array; last scenario index = 88 (Command Counter Verification)
- `tests/e2e/scenarios/11-gauge-labels-e2e-sim.sh` — query pattern for snmp_gauge; value extracted as `.data.result[0].value[1]`
- `tests/e2e/scenarios/14-info-labels.sh` — query pattern for snmp_info; value label extracted as `.data.result[0].metric.value`; numeric value always "1"
- `tests/e2e/scenarios/17-snmp-type-labels.sh` — all 5 numeric types queried; confirms all appear in snmp_gauge for E2E-SIM
- `tests/e2e/lib/sim.sh` — `sim_set_oid` implementation; OID suffix is relative to E2E_PREFIX; `reset_oid_overrides` available

### Secondary (MEDIUM confidence)
- `tests/e2e/scenarios/18-oid-rename.sh` — poll-with-deadline pattern, 60 s, 3 s interval; value-change should be faster (40 s)
- `tests/e2e/scenarios/85-ccv04-command-failed.sh` — multi-step scenario pattern with setup/cleanup/assertions; confirms report category index arithmetic

---

## Metadata

**Confidence breakdown:**
- Value recording path (RecordGauge, RecordInfo): HIGH — source code read directly
- Default simulator values: HIGH — e2e_simulator.py baseline dict read directly
- sim_set_oid suffix format: HIGH — sim.sh and post_oid_value source read directly
- Poll timing (10 s interval): HIGH — simetra-devices.yaml read directly
- Report category indexing: HIGH — report.sh read directly, arithmetic verified
- IpAddress exact string format in Prometheus: MEDIUM — Value.ToString() behavior not verified end-to-end; scenario 14 confirms non-empty but not exact value
- String OID HTTP override limitation: HIGH — `int(request.match_info["value"])` confirmed in Python source

**Research date:** 2026-03-22
**Valid until:** 2026-04-22 (stable codebase; simulator values are constants)
