# Phase 22: Business Metric and Unknown OID Verification - Research

**Researched:** 2026-03-09
**Domain:** Bash E2E test scenarios, Prometheus PromQL queries for snmp_gauge/snmp_info, ConfigMap snapshot/restore
**Confidence:** HIGH

## Summary

This phase adds E2E test scenarios that verify the full SNMP-to-Prometheus data path for business metrics (`snmp_gauge` and `snmp_info`), unknown OID classification (`metric_name="Unknown"`), and trap-originated metrics. The codebase investigation confirms all the infrastructure is already in place: the test harness from Phase 21 provides the lib/ utilities (prometheus.sh, kubectl.sh, common.sh, report.sh) and the scenario-per-file pattern. The E2E-SIM simulator provides deterministic static values across 5 SNMP types.

A critical finding is that the E2E-SIM devices.json config only polls the 7 mapped OIDs (.999.1.1-7). The 2 unmapped OIDs (.999.2.1 and .999.2.2) are registered in the simulator but **NOT polled**, because they are not listed in the devices ConfigMap. To test unknown OID classification, the devices ConfigMap must be mutated to add these OIDs to E2E-SIM's poll list. This is the primary use case for the ConfigMap snapshot/restore utility.

**Primary recommendation:** Build 4-5 new scenario scripts in `tests/e2e/scenarios/` following the established pattern. The ConfigMap snapshot/restore utility should live in `lib/kubectl.sh` (extending the existing `save_configmap`/`restore_configmap` functions). The unknown OID test requires a ConfigMap mutation: add the 2 unmapped OIDs to E2E-SIM's device config, wait for hot-reload + poll + export, then query for `metric_name="Unknown"`.

## Standard Stack

### Core
| Tool | Purpose | Why Standard |
|------|---------|--------------|
| bash | Scenario scripts | Established pattern from Phase 21 |
| curl + jq | Prometheus HTTP API queries | Already used in lib/prometheus.sh |
| kubectl | ConfigMap backup/restore/apply | Already used in lib/kubectl.sh |

### Not Needed
| Instead of | Why Not |
|------------|---------|
| New test framework | Phase 21 harness already provides everything needed |
| Python test scripts | Established convention is bash scenarios |
| Prometheus client library | curl + jq is sufficient and already proven |

## Architecture Patterns

### Recommended Project Structure
```
tests/e2e/
├── scenarios/
│   ├── 11-gauge-labels-e2e-sim.sh        # BIZ-01: snmp_gauge labels for E2E-SIM
│   ├── 12-gauge-labels-obp.sh            # BIZ-01: snmp_gauge labels for OBP-01
│   ├── 13-gauge-labels-npb.sh            # BIZ-01: snmp_gauge labels for NPB-01
│   ├── 14-info-labels.sh                 # BIZ-02: snmp_info labels (E2E-SIM info_test + ip_test)
│   ├── 15-unknown-oid.sh                 # BIZ-03: Unknown OID classification (mutation test)
│   ├── 16-trap-originated.sh             # BIZ-04: Trap-originated metrics in Prometheus
│   └── 17-snmp-type-labels.sh            # BIZ-01: All 5 snmp_type values verified
├── fixtures/
│   ├── fake-device-configmap.yaml        # (existing -- Phase 21)
│   ├── .original-devices-configmap.yaml  # (gitignored -- snapshot backup)
│   └── .original-oidmaps-configmap.yaml  # (gitignored -- snapshot backup)
└── lib/
    ├── kubectl.sh                        # Extended with snapshot_configmaps/restore_configmaps
    └── (other libs unchanged)
```

### Pattern 1: Label-Aware Gauge Query
**What:** Query snmp_gauge with specific label matchers, verify all expected labels are present and values are reasonable.
**When to use:** BIZ-01 gauge verification for all three devices.
**Example:**
```bash
# Query snmp_gauge for E2E-SIM gauge_test OID
result=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",metric_name="e2e_gauge_test"}')
count=$(echo "$result" | jq -r '.data.result | length')

# Check metric exists
if [ "$count" -gt 0 ]; then
    # Extract label values for verification
    device=$(echo "$result" | jq -r '.data.result[0].metric.device_name')
    metric=$(echo "$result" | jq -r '.data.result[0].metric.metric_name')
    oid=$(echo "$result" | jq -r '.data.result[0].metric.oid')
    snmp_type=$(echo "$result" | jq -r '.data.result[0].metric.snmp_type')
    value=$(echo "$result" | jq -r '.data.result[0].value[1]')

    # All 4 labels present and value is numeric > 0
fi
```

### Pattern 2: Info Metric Value Label Check
**What:** Query snmp_info and verify the `value` label exists and is non-empty.
**When to use:** BIZ-02 snmp_info verification.
**Example:**
```bash
result=$(query_prometheus 'snmp_info{device_name="E2E-SIM",metric_name="e2e_info_test"}')
value_label=$(echo "$result" | jq -r '.data.result[0].metric.value // empty')
# Verify value_label is non-empty (don't assert exact string)
```

### Pattern 3: Unknown OID Mutation Test
**What:** Add unmapped OIDs to device poll config, wait for data to appear, query metric_name="Unknown".
**When to use:** BIZ-03 unknown OID classification.
**Example:**
```bash
# 1. Snapshot current ConfigMaps
snapshot_configmaps

# 2. Apply mutated devices ConfigMap with unmapped OIDs added to E2E-SIM
kubectl apply -f fixtures/e2e-sim-with-unmapped-configmap.yaml

# 3. Wait for DeviceWatcher hot-reload + poll cycle + OTel export
# DeviceWatcher: ~5s, poll: 10s, OTel: 15s = ~30s minimum
poll_until_label 45 3 'snmp_gauge{device_name="E2E-SIM",metric_name="Unknown"}' || true

# 4. Verify both unmapped OIDs appear
result=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",metric_name="Unknown"}')
# Check .999.2.1.0 appears (gauge type, value 99)

result=$(query_prometheus 'snmp_info{device_name="E2E-SIM",metric_name="Unknown"}')
# Check .999.2.2.0 appears (octetstring type, value "UNMAPPED")

# 5. Restore original ConfigMaps
restore_configmaps
```

### Pattern 4: Trap-Originated Metric Verification
**What:** Query snmp_gauge with source="trap" and device_name="E2E-SIM" to prove trap data reaches Prometheus.
**When to use:** BIZ-04 trap-to-Prometheus path.
**Example:**
```bash
# E2E-SIM sends valid traps every 30s with varbind .999.1.1.0 (Gauge32=42)
# These should appear as snmp_gauge{source="trap",device_name="E2E-SIM"}
result=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",source="trap"}')
count=$(echo "$result" | jq -r '.data.result | length')
# Should find trap-originated gauge_test metric
```

### Anti-Patterns to Avoid
- **Asserting exact values for random-walk simulators:** OBP and NPB use random walk for power/counter values. Only assert non-zero or within reasonable range. E2E-SIM uses static values (42 for gauge_test) but still be cautious since Prometheus stores the last-reported value.
- **Querying without device_name filter:** Always filter by device_name to avoid cross-contamination between devices.
- **Fixed fixture for unmapped OIDs:** Don't hardcode the full devices ConfigMap in a fixture file. Instead, build a mutation fixture that adds only the 2 unmapped OIDs to E2E-SIM's existing config. BUT this is complex -- a simpler approach is to maintain a full ConfigMap fixture with the unmapped OIDs added.
- **Forgetting to restore ConfigMaps:** Always use the snapshot/restore pattern. If the test runner exits abnormally (Ctrl+C, error), the EXIT trap must restore ConfigMaps.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| ConfigMap backup | Custom kubectl export parsing | `kubectl get configmap -o yaml > file` | Standard kubectl output, already in lib/kubectl.sh |
| Prometheus label checking | String grep on raw response | `jq -r '.data.result[0].metric.LABEL'` | JSON path is reliable; grep breaks on special chars |
| Waiting for metric appearance | Fixed sleep | `poll_until_exists` from lib/prometheus.sh | Already implemented, handles OTel export latency |
| Port-forward lifecycle | Manual start/stop | Existing lib/kubectl.sh functions | Already handles PID tracking and EXIT trap cleanup |

## Common Pitfalls

### Pitfall 1: Unmapped OIDs Not Being Polled
**What goes wrong:** Query for `metric_name="Unknown"` returns empty because the OIDs aren't polled.
**Why it happens:** The E2E-SIM device config only lists 7 mapped OIDs. The 2 unmapped OIDs (.999.2.1.0, .999.2.2.0) exist in the simulator but are NOT in the devices.json poll list.
**How to avoid:** The unknown OID test MUST mutate the devices ConfigMap to add the unmapped OIDs to E2E-SIM's poll config. This is a ConfigMap mutation test, not a passive observation.
**Warning signs:** `snmp_gauge{metric_name="Unknown"}` returns 0 results.

### Pitfall 2: ConfigMap Mutation Timing
**What goes wrong:** Query returns empty results even after mutating ConfigMap.
**Why it happens:** Three latency stages: DeviceWatcher hot-reload (~5s), first poll cycle (up to 10s for 10s interval), OTel export (up to 15s).
**How to avoid:** Use poll_until with at least 45s timeout after ConfigMap apply. 5s (watcher) + 10s (poll) + 15s (export) = 30s minimum, with 50% safety margin = 45s.
**Warning signs:** Flaky test that sometimes passes, sometimes fails.

### Pitfall 3: snmp_gauge vs snmp_info for Different SNMP Types
**What goes wrong:** Querying snmp_gauge for OctetString/IpAddress OIDs returns nothing.
**Why it happens:** OctetString, IpAddress, and ObjectIdentifier types are recorded as `snmp_info` (value=1.0 with a `value` label), NOT `snmp_gauge`. See OtelMetricHandler.cs switch statement.
**How to avoid:** Use the correct metric name per SNMP type:
- `snmp_gauge`: Integer32, Gauge32, TimeTicks, Counter32, Counter64 (5 types)
- `snmp_info`: OctetString, IpAddress, ObjectIdentifier (3 types)
**Warning signs:** Missing metrics for string-typed OIDs.

### Pitfall 4: Trap Varbind OID for E2E-SIM
**What goes wrong:** Searching for trap-originated metrics by the wrong OID.
**Why it happens:** E2E-SIM traps use trap OID `.999.3.1` but include varbind `.999.1.1.0` (Gauge32=42). The varbind OID is what appears as `snmp_gauge`, not the trap OID. The trap OID is SNMPv2 metadata.
**How to avoid:** Query `snmp_gauge{device_name="E2E-SIM",source="trap",oid="1.3.6.1.4.1.47477.999.1.1.0"}` to find the trap-originated metric. This is the same OID as the poll-originated version, so filter by `source="trap"`.
**Warning signs:** Cannot find trap-originated metrics when searching by trap notification OID.

### Pitfall 5: Leader-Only Business Metric Export
**What goes wrong:** snmp_gauge/snmp_info metrics don't appear in Prometheus.
**Why it happens:** Business metrics (snmp_gauge, snmp_info) are registered on the leader-gated meter. Only the leader pod exports them. If the leader pod changed recently or leader election is unstable, metrics may be temporarily absent.
**How to avoid:** Pre-flight should verify snmp_gauge exists in Prometheus before running business metric scenarios. If absent, the cluster may need time for leader election to stabilize.
**Warning signs:** Pipeline counters exist but snmp_gauge/snmp_info don't.

### Pitfall 6: Prometheus Staleness for Removed Metrics After Restore
**What goes wrong:** After restoring the original ConfigMap (removing unmapped OIDs from poll list), querying for `metric_name="Unknown"` still returns results.
**Why it happens:** Prometheus has a 5-minute staleness window. Stale series remain queryable until staleness timeout.
**How to avoid:** This is not a problem for this phase -- we only need to verify metrics APPEAR (not disappear). But be aware that the unknown OID test should run BEFORE the restore, and subsequent tests should not depend on absence of unknown metrics.

### Pitfall 7: E2E-SIM Unmapped OID Values -- Context Doc Mismatch
**What goes wrong:** Test asserts value=99999 for the unmapped gauge, but actual value is 99.
**Why it happens:** The CONTEXT.md mentions "value 99999" for .999.2.1.0, but the actual simulator code sets the value to 99 (`v2c.Gauge32, 99`).
**How to avoid:** Use the actual simulator values from `e2e_simulator.py`:
- `.999.2.1.0`: Gauge32 value=99 (not 99999)
- `.999.2.2.0`: OctetString value="UNMAPPED" (not "unmapped_string_value")

## Code Examples

### Querying snmp_gauge with Full Label Verification
```bash
# Source: Prometheus HTTP API + project's lib/prometheus.sh pattern
verify_gauge_labels() {
    local device="$1"
    local metric_name="$2"
    local expected_oid="$3"
    local expected_snmp_type="$4"
    local scenario_name="$5"

    local result
    result=$(query_prometheus "snmp_gauge{device_name=\"${device}\",metric_name=\"${metric_name}\"}")
    local count
    count=$(echo "$result" | jq -r '.data.result | length')

    if [ "$count" -eq 0 ]; then
        record_fail "$scenario_name" "No snmp_gauge found for device=${device} metric=${metric_name}"
        return
    fi

    local actual_oid actual_type actual_source actual_value
    actual_oid=$(echo "$result" | jq -r '.data.result[0].metric.oid')
    actual_type=$(echo "$result" | jq -r '.data.result[0].metric.snmp_type')
    actual_source=$(echo "$result" | jq -r '.data.result[0].metric.source')
    actual_value=$(echo "$result" | jq -r '.data.result[0].value[1]')

    local evidence="oid=${actual_oid} snmp_type=${actual_type} source=${actual_source} value=${actual_value}"

    # Verify expected labels
    if [ "$actual_oid" = "$expected_oid" ] && [ "$actual_type" = "$expected_snmp_type" ]; then
        record_pass "$scenario_name" "$evidence"
    else
        record_fail "$scenario_name" "Label mismatch: expected oid=${expected_oid} type=${expected_snmp_type}, got ${evidence}"
    fi
}
```

### Querying snmp_info with Value Label Check
```bash
verify_info_labels() {
    local device="$1"
    local metric_name="$2"
    local expected_snmp_type="$3"
    local scenario_name="$4"

    local result
    result=$(query_prometheus "snmp_info{device_name=\"${device}\",metric_name=\"${metric_name}\"}")
    local count
    count=$(echo "$result" | jq -r '.data.result | length')

    if [ "$count" -eq 0 ]; then
        record_fail "$scenario_name" "No snmp_info found for device=${device} metric=${metric_name}"
        return
    fi

    local value_label snmp_type
    value_label=$(echo "$result" | jq -r '.data.result[0].metric.value // empty')
    snmp_type=$(echo "$result" | jq -r '.data.result[0].metric.snmp_type')

    if [ -n "$value_label" ] && [ "$snmp_type" = "$expected_snmp_type" ]; then
        record_pass "$scenario_name" "value_label=${value_label} snmp_type=${snmp_type}"
    else
        record_fail "$scenario_name" "value_label='${value_label}' snmp_type='${snmp_type}' (expected non-empty value and type=${expected_snmp_type})"
    fi
}
```

### ConfigMap Snapshot/Restore Functions
```bash
# Extension to lib/kubectl.sh
FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

snapshot_configmaps() {
    log_info "Snapshotting ConfigMaps for mutation testing..."
    save_configmap "simetra-devices" "simetra" "$FIXTURES_DIR/.original-devices-configmap.yaml"
    save_configmap "simetra-oidmaps" "simetra" "$FIXTURES_DIR/.original-oidmaps-configmap.yaml"
    log_info "ConfigMap snapshots saved"
}

restore_configmaps() {
    log_info "Restoring ConfigMaps from snapshots..."
    if [ -f "$FIXTURES_DIR/.original-devices-configmap.yaml" ]; then
        restore_configmap "$FIXTURES_DIR/.original-devices-configmap.yaml"
    fi
    if [ -f "$FIXTURES_DIR/.original-oidmaps-configmap.yaml" ]; then
        restore_configmap "$FIXTURES_DIR/.original-oidmaps-configmap.yaml"
    fi
    log_info "ConfigMaps restored"
}
```

## Key Data Reference

### E2E-SIM OID-to-Label-to-Type Mapping (Authoritative)

| OID (suffix) | Full OID | Metric Name | SNMP Type | Prometheus Metric | Static Value |
|-------------|----------|-------------|-----------|-------------------|--------------|
| .999.1.1.0 | 1.3.6.1.4.1.47477.999.1.1.0 | e2e_gauge_test | gauge32 | snmp_gauge | 42 |
| .999.1.2.0 | 1.3.6.1.4.1.47477.999.1.2.0 | e2e_integer_test | integer32 | snmp_gauge | 100 |
| .999.1.3.0 | 1.3.6.1.4.1.47477.999.1.3.0 | e2e_counter32_test | counter32 | snmp_gauge | 5000 |
| .999.1.4.0 | 1.3.6.1.4.1.47477.999.1.4.0 | e2e_counter64_test | counter64 | snmp_gauge | 1000000 |
| .999.1.5.0 | 1.3.6.1.4.1.47477.999.1.5.0 | e2e_timeticks_test | timeticks | snmp_gauge | 360000 |
| .999.1.6.0 | 1.3.6.1.4.1.47477.999.1.6.0 | e2e_info_test | octetstring | snmp_info | "E2E-TEST-VALUE" |
| .999.1.7.0 | 1.3.6.1.4.1.47477.999.1.7.0 | e2e_ip_test | ipaddress | snmp_info | "10.0.0.1" |
| .999.2.1.0 | 1.3.6.1.4.1.47477.999.2.1.0 | Unknown | gauge32 | snmp_gauge | 99 |
| .999.2.2.0 | 1.3.6.1.4.1.47477.999.2.2.0 | Unknown | octetstring | snmp_info | "UNMAPPED" |

### Trap Varbinds from E2E-SIM

| Trap OID | Varbind OID | Varbind Type | Varbind Value | Community |
|----------|-------------|--------------|---------------|-----------|
| .999.3.1 | .999.1.1.0 | Gauge32 | 42 | Simetra.E2E-SIM |

The trap varbind .999.1.1.0 maps to `e2e_gauge_test` in oidmaps.json, so trap-originated metrics appear as `snmp_gauge{metric_name="e2e_gauge_test",device_name="E2E-SIM",source="trap"}`.

### OBP-01 and NPB-01 SNMP Types Available in Prometheus

| Device | SNMP Types (from oidmaps) | snmp_info OIDs | Notes |
|--------|--------------------------|----------------|-------|
| OBP-01 | integer32 (link_state, channel, power) | NMU OIDs (.60.x) -- NOT in oidmaps | NMU OIDs appear as Unknown if polled |
| NPB-01 | gauge32, timeticks, integer32, counter64 | .100.1.{5,6,7}.0 -- NOT in oidmaps | Info OIDs appear as Unknown if polled |
| E2E-SIM | gauge32, integer32, counter32, counter64, timeticks, octetstring, ipaddress | .999.1.{6,7}.0 -- in oidmaps | Full type coverage for verification |

### snmp_type Label Values (from OtelMetricHandler.cs)

| SnmpType Enum | snmp_type Label | Prometheus Metric |
|---------------|-----------------|-------------------|
| Integer32 | "integer32" | snmp_gauge |
| Gauge32 | "gauge32" | snmp_gauge |
| TimeTicks | "timeticks" | snmp_gauge |
| Counter32 | "counter32" | snmp_gauge |
| Counter64 | "counter64" | snmp_gauge |
| OctetString | "octetstring" | snmp_info |
| IPAddress | "ipaddress" | snmp_info |
| ObjectIdentifier | "objectidentifier" | snmp_info |

### Devices ConfigMap Mutation for Unknown OID Test

The current `simetra-devices` ConfigMap has E2E-SIM polling only 7 mapped OIDs. To test unknown OID classification, add the 2 unmapped OIDs to the poll list:

```json
{
  "Name": "E2E-SIM",
  "IpAddress": "e2e-simulator.simetra.svc.cluster.local",
  "Port": 161,
  "MetricPolls": [
    {
      "IntervalSeconds": 10,
      "Oids": [
        "1.3.6.1.4.1.47477.999.1.1.0",
        "1.3.6.1.4.1.47477.999.1.2.0",
        "1.3.6.1.4.1.47477.999.1.3.0",
        "1.3.6.1.4.1.47477.999.1.4.0",
        "1.3.6.1.4.1.47477.999.1.5.0",
        "1.3.6.1.4.1.47477.999.1.6.0",
        "1.3.6.1.4.1.47477.999.1.7.0",
        "1.3.6.1.4.1.47477.999.2.1.0",
        "1.3.6.1.4.1.47477.999.2.2.0"
      ]
    }
  ]
}
```

The fixture needs to be a FULL devices ConfigMap (all devices), not just the E2E-SIM entry, because `kubectl apply` replaces the entire ConfigMap.

## ConfigMap Snapshot/Restore Design Decision

**Recommendation: Extend `lib/kubectl.sh`** (not a new `lib/configmap.sh`).

Rationale:
- `save_configmap` and `restore_configmap` already exist in `lib/kubectl.sh`
- Adding `snapshot_configmaps` and `restore_configmaps` (plural) as wrappers is a natural extension
- A separate file would require an additional `source` call in run-all.sh for minimal benefit
- The snapshot/restore pattern is kubectl-centric, not ConfigMap-specific

The `.gitignore` in `tests/e2e/` already ignores `.original-devices-configmap.yaml`. Add `.original-oidmaps-configmap.yaml` to match the pattern for both ConfigMaps.

## Scenario Numbering and Fixture Strategy

### Scenario Numbers
Phase 21 scenarios use numbers 01-10. Phase 22 should continue sequentially: 11, 12, 13, etc. This ensures `run-all.sh`'s glob pattern `scenarios/[0-9]*.sh` picks them up in correct order.

### Fixture Strategy for Mutation Test
The unknown OID test needs a full `simetra-devices` ConfigMap fixture with the 2 unmapped OIDs added to E2E-SIM's poll list. Two options:

1. **Static fixture file** (e.g., `fixtures/e2e-sim-unmapped-configmap.yaml`): Simple, explicit, but duplicates the entire devices.json and could get stale if devices change.

2. **Dynamic patch via kubectl/jq**: More robust but complex. Would need to `kubectl get configmap simetra-devices -o json | jq '.data["devices.json"] | fromjson | ... | tojson' | kubectl apply -f -`.

**Recommendation: Static fixture file.** The devices ConfigMap is stable (it changed last in Phase 20 and is unlikely to change again soon). A static fixture is simpler, more readable, and easier to debug. It mirrors the pattern already used for `fake-device-configmap.yaml`.

## Open Questions

1. **Source Label Differentiation for Trap vs Poll**
   - What we know: The `source` label is set to "poll" or "trap" by the pipeline (SnmpSource enum, lowercased in OtelMetricHandler). E2E-SIM trap varbind uses OID .999.1.1.0, which is also polled. So both `source="poll"` and `source="trap"` series should exist for this OID.
   - What's unclear: Whether Prometheus correctly differentiates these as separate time series via the `source` label. Should be fine since labels create distinct series.
   - Recommendation: Query with `source="trap"` filter to verify trap-originated metrics specifically. If no results, check if OTel collector or Prometheus is merging/dropping the source label.

2. **OBP-01 SNMP Types for Verification**
   - What we know: OBP simulator uses Integer32 for all poll OIDs (link_state, channel, power). No Gauge32, Counter, or TimeTicks.
   - What's unclear: Whether verifying OBP-01 adds meaningful coverage beyond E2E-SIM (which already has all types).
   - Recommendation: Verify OBP-01 has snmp_gauge metrics with correct device_name and metric_name labels, but don't duplicate the full type-coverage verification. E2E-SIM provides the definitive type coverage.

## Sources

### Primary (HIGH confidence)
- `simulators/e2e-sim/e2e_simulator.py` -- Authoritative OID definitions, static values, SNMP types, trap varbinds
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` -- snmp_gauge/snmp_info recording logic, snmp_type label values
- `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` -- Exact label names: metric_name, oid, device_name, ip, source, snmp_type, value
- `src/SnmpCollector/Pipeline/OidMapService.cs` -- `Unknown` constant, OID resolution logic
- `deploy/k8s/snmp-collector/simetra-oidmaps.yaml` -- Authoritative OID-to-metric-name mapping (7 E2E-SIM entries)
- `deploy/k8s/snmp-collector/simetra-devices.yaml` -- Authoritative device poll config (E2E-SIM polls only 7 mapped OIDs)
- `tests/e2e/lib/*.sh` -- Existing test harness utilities (query_prometheus, save_configmap, etc.)
- `tests/e2e/scenarios/01-10` -- Existing scenario pattern to follow
- `.planning/phases/21-test-harness-and-pipeline-counter-verification/21-RESEARCH.md` -- Phase 21 research with established patterns

### Secondary (MEDIUM confidence)
- Prometheus HTTP API query format (stable, well-documented)
- K8s ConfigMap hot-reload behavior via DeviceWatcherService (confirmed in Phase 21 research)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- reusing Phase 21 infrastructure entirely, no new tools
- Architecture: HIGH -- scenario patterns derived from existing codebase, OID/label data from source code
- Data reference: HIGH -- all OID values, types, and labels read directly from source code
- Pitfalls: HIGH -- derived from codebase analysis (mutation requirement, type routing, timing)
- ConfigMap design: HIGH -- extending existing patterns with minimal additions

**Research date:** 2026-03-09
**Valid until:** 2026-04-09 (stable -- bash patterns and established harness)
