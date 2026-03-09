# Phase 23: OID Map Mutation and Device Lifecycle Verification - Research

**Researched:** 2026-03-09
**Domain:** E2E bash test scenarios for ConfigMap runtime mutation verification
**Confidence:** HIGH

## Summary

Phase 23 creates 6 E2E test scenarios (numbered 18-23) that verify runtime ConfigMap mutations propagate correctly to Prometheus metrics without pod restarts. The scenarios split into two groups: OID map mutations (rename, remove, add) and device lifecycle events (add, remove, modify interval). All scenarios use the snapshot/restore infrastructure from Phase 22 for isolation.

This phase requires NO C# code changes. All work is bash scenario scripts and YAML fixture files. The existing e2e test harness provides all needed utilities: `query_prometheus`, `snapshot_configmaps`/`restore_configmaps`, `snapshot_counter`/`query_counter`, `record_pass`/`record_fail`, and `assert_delta_gt`.

**Primary recommendation:** Create 6 static fixture YAML files (one per scenario) and 6 scenario shell scripts following the exact sourced-script pattern established by scenarios 01-17.

## Standard Stack

### Core
| Tool | Version | Purpose | Why Standard |
|------|---------|---------|--------------|
| bash | N/A | Test script execution | All 17 existing scenarios use bash |
| kubectl | N/A | ConfigMap apply/get | Established pattern in scenarios 06-07, 15 |
| curl + jq | N/A | Prometheus HTTP API queries | Used by prometheus.sh library |
| Prometheus HTTP API | v1 | Metric verification | `/api/v1/query` endpoint |

### Supporting
| Tool | Purpose | When to Use |
|------|---------|-------------|
| `snapshot_configmaps` / `restore_configmaps` | Per-scenario isolation | Before/after every mutation scenario |
| `query_counter` / `snapshot_counter` | Counter delta measurement | Device lifecycle scenarios (poll_executed delta) |
| `query_prometheus` | Raw PromQL queries | OID mutation scenarios (label verification) |

### Alternatives Considered
None -- the entire stack is already established and locked by Phases 21-22.

## Architecture Patterns

### Recommended Project Structure
```
tests/e2e/
  fixtures/
    oid-renamed-configmap.yaml          # MUT-01: e2e_gauge_test -> e2e_renamed_gauge
    oid-removed-configmap.yaml          # MUT-02: remove .999.1.1.0 from oidmaps
    oid-added-configmap.yaml            # MUT-03: add .999.2.1.0 to oidmaps
    device-added-configmap.yaml         # DEV-01: add E2E-SIM-2 device
    device-removed-configmap.yaml       # DEV-02: remove E2E-SIM device
    device-modified-interval-configmap.yaml  # DEV-03: E2E-SIM 10s -> 5s
  scenarios/
    18-oid-rename.sh
    19-oid-remove.sh
    20-oid-add.sh
    21-device-add.sh
    22-device-remove.sh
    23-device-modify-interval.sh
```

### Pattern 1: OID Map Mutation Scenario (oidmaps ConfigMap)
**What:** Modify simetra-oidmaps ConfigMap, verify Prometheus labels change
**When to use:** Scenarios 18-20 (MUT-01, MUT-02, MUT-03)
**Example:**
```bash
# Scenario 18: OID rename -- .999.1.1.0 renamed from e2e_gauge_test to e2e_renamed_gauge
SCENARIO_NAME="OID rename: e2e_gauge_test -> e2e_renamed_gauge"

snapshot_configmaps

kubectl apply -f "$SCRIPT_DIR/fixtures/oid-renamed-configmap.yaml" -n simetra

# Poll until new metric_name appears (OidMapWatcher ~instant + poll 10s + OTel 15s = ~30s)
DEADLINE=$(( $(date +%s) + 60 ))
FOUND=0
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    RESULT=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",metric_name="e2e_renamed_gauge"}')
    COUNT=$(echo "$RESULT" | jq -r '.data.result | length')
    if [ "$COUNT" -gt 0 ]; then FOUND=1; break; fi
    sleep 3
done

if [ "$FOUND" -eq 1 ]; then
    record_pass "$SCENARIO_NAME" "metric_name=e2e_renamed_gauge appeared"
else
    record_fail "$SCENARIO_NAME" "e2e_renamed_gauge not found within timeout"
fi

restore_configmaps

# Wait for original metric to reappear before ending
DEADLINE=$(( $(date +%s) + 60 ))
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    RESULT=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",metric_name="e2e_gauge_test"}')
    COUNT=$(echo "$RESULT" | jq -r '.data.result | length')
    if [ "$COUNT" -gt 0 ]; then break; fi
    sleep 3
done
```

### Pattern 2: Device Lifecycle Scenario (devices ConfigMap)
**What:** Modify simetra-devices ConfigMap, verify poll counter behavior
**When to use:** Scenarios 21-23 (DEV-01, DEV-02, DEV-03)
**Example:**
```bash
# Scenario 21: Device add -- new E2E-SIM-2 device at same E2E simulator IP
SCENARIO_NAME="Device add: E2E-SIM-2 poll_executed increments"
METRIC="snmp_poll_executed_total"
FILTER='device_name="E2E-SIM-2"'

snapshot_configmaps

BEFORE=$(snapshot_counter "$METRIC" "$FILTER")

kubectl apply -f "$SCRIPT_DIR/fixtures/device-added-configmap.yaml" -n simetra

# DeviceWatcher reload ~5s + poll 10s + OTel 15s = ~30s
poll_until 60 3 "$METRIC" "$FILTER" "$BEFORE" || true

AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE=$(get_evidence "$METRIC" "$FILTER")
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "before=$BEFORE after=$AFTER delta=$DELTA | $EVIDENCE"

restore_configmaps
```

### Pattern 3: Counter Stagnation Assertion (Device Removal)
**What:** Verify a removed device's poll counter stops incrementing (delta = 0)
**When to use:** Scenario 22 (DEV-02)
**Important:** No `assert_delta_eq` exists in common.sh -- need inline assertion
```bash
# After removing device, snapshot poll_executed, wait, snapshot again
# The delta should be 0 (no new polls)
BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
sleep 20  # Wait 2 poll intervals (10s each)
AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))

if [ "$DELTA" -eq 0 ]; then
    record_pass "$SCENARIO_NAME" "delta=0 confirms no new polls"
else
    record_fail "$SCENARIO_NAME" "delta=$DELTA, expected 0"
fi
```

### Pattern 4: Poll Interval Change Verification (Device Modify)
**What:** Compare poll_executed deltas before and after interval change
**When to use:** Scenario 23 (DEV-03)
```bash
# Measure delta at 10s interval (original)
BEFORE_10=$(snapshot_counter "$METRIC" "$FILTER")
sleep 20
AFTER_10=$(snapshot_counter "$METRIC" "$FILTER")
DELTA_10=$((AFTER_10 - BEFORE_10))

# Change to 5s interval
kubectl apply -f "$SCRIPT_DIR/fixtures/device-modified-interval-configmap.yaml" -n simetra
sleep 10  # Allow reconciliation

# Measure delta at 5s interval
BEFORE_5=$(snapshot_counter "$METRIC" "$FILTER")
sleep 20
AFTER_5=$(snapshot_counter "$METRIC" "$FILTER")
DELTA_5=$((AFTER_5 - BEFORE_5))

# At 5s interval, should get roughly double the polls in same window
if [ "$DELTA_5" -gt "$DELTA_10" ]; then
    record_pass "..." "delta_5s=$DELTA_5 > delta_10s=$DELTA_10"
else
    record_fail "..." "delta_5s=$DELTA_5 <= delta_10s=$DELTA_10"
fi
```

### Anti-Patterns to Avoid
- **Asserting metric absence for removal:** Prometheus retains stale metrics for 5 minutes. Never assert a metric "disappeared." Instead verify counter delta = 0 (stagnation).
- **Fixed sleeps instead of polling:** Always use `poll_until` or deadline loops with 3s intervals for positive assertions. Only use fixed sleeps for stagnation measurement windows.
- **Skipping restore after failure:** Always call `restore_configmaps` regardless of test outcome. The scenarios are sourced (not subprocesses), so use explicit restore calls at the end of each scenario.
- **Modifying only oidmaps entries in fixture:** Oidmaps fixtures must contain ALL entries (OBP + NPB + E2E-SIM) since `kubectl apply` replaces the entire ConfigMap data.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| ConfigMap backup/restore | Manual kubectl get/apply | `snapshot_configmaps`/`restore_configmaps` in kubectl.sh | Already handles both ConfigMaps, uses .gitignored temp files |
| Counter delta measurement | Manual curl + jq math | `snapshot_counter` + `query_counter` in prometheus.sh | Handles sum(), label filters, fallback to 0 |
| Polling for metric appearance | Fixed sleep | `poll_until` or manual deadline loops | 30s timeout / 3s interval prevents flakiness |
| Pass/fail recording | echo/exit codes | `record_pass`/`record_fail` in common.sh | Integrates with report generation and summary |

## Common Pitfalls

### Pitfall 1: OID Rename Staleness Window
**What goes wrong:** After renaming an OID, both old and new metric_name may coexist in Prometheus for up to 5 minutes (Prometheus staleness). Tests may see the old name alongside the new one.
**Why it happens:** Prometheus keeps series alive for 5 minutes after last scrape. The old metric_name was last reported just before the ConfigMap change.
**How to avoid:** Assert only that the NEW metric_name appears. Do NOT assert the old name is gone. The old name persisting is expected behavior, not a bug.
**Warning signs:** Test fails because it tries to assert the old metric_name doesn't exist.

### Pitfall 2: OID Remove vs Unknown Timing
**What goes wrong:** After removing an OID from oidmaps, the OID is still being polled (devices ConfigMap unchanged). It takes a poll cycle + OTel export for the OID to appear with metric_name="Unknown".
**Why it happens:** OidMapService.Resolve() returns "Unknown" immediately after UpdateMap(), but the polled value must flow through the pipeline and be exported to Prometheus.
**How to avoid:** Use 60s timeout polling for `snmp_gauge{metric_name="Unknown",oid="..."}` to appear.
**Warning signs:** Timeout too short for the OidMapWatcher -> poll -> pipeline -> OTel -> Prometheus chain.

### Pitfall 3: Device Removal Stagnation Window Too Short
**What goes wrong:** After removing a device, checking counter delta too quickly may show delta > 0 because the last poll was already in the OTel export pipeline.
**Why it happens:** The OTel exporter has a 15-second export interval. Polls that completed before device removal may still be in the export buffer.
**How to avoid:** Wait at least 20 seconds after removing the device before starting the stagnation measurement. Then measure over another 20-second window.
**Warning signs:** Intermittent delta > 0 in the removal stagnation window.

### Pitfall 4: Full ConfigMap Replacement Semantics
**What goes wrong:** Fixture YAML replaces the ENTIRE ConfigMap data, not just the modified entries. Missing an OBP or NPB entry from an oidmaps fixture would delete all OBP/NPB mappings.
**Why it happens:** `kubectl apply` for ConfigMap replaces the whole `.data` section.
**How to avoid:** Every fixture must contain ALL entries from the original ConfigMap, with only the target entries modified. Copy from `deploy/k8s/snmp-collector/simetra-oidmaps.yaml` or `simetra-devices.yaml` as the base.
**Warning signs:** Other scenarios break because their OID mappings or device entries disappeared.

### Pitfall 5: Scenario 20 (OID Add) Needs Two-Step Setup
**What goes wrong:** Adding an OID mapping to oidmaps doesn't do anything unless the OID is actually being polled.
**Why it happens:** The oidmap maps OIDs to names, but the device must be configured to poll that OID for data to flow.
**How to avoid:** Scenario 20 needs a two-step process: (1) apply devices fixture with unmapped OID added to poll list (reuse e2e-sim-unmapped-configmap.yaml from Phase 22), (2) wait for Unknown to appear, then (3) apply oidmaps fixture with the new mapping. This proves the transition from Unknown to named.
**Warning signs:** Test passes trivially because the OID was never polled.

### Pitfall 6: E2E-SIM-2 Community String
**What goes wrong:** Adding a new device "E2E-SIM-2" pointing at the E2E simulator fails SNMP auth because community string doesn't match.
**Why it happens:** The E2E simulator only accepts community string "Simetra.E2E-SIM" (configured via COMMUNITY env var). A device named "E2E-SIM-2" would default to "Simetra.E2E-SIM-2" (based on device name convention) or whatever the collector generates.
**How to avoid:** Check whether the collector derives community strings from device name or from a separate config field. The `DeviceOptions` does NOT appear to have a CommunityString field in the base config (scenarios 06-07 patched it via jq). Looking at the device config structure, there is no explicit CommunityString in the standard device fixture. The collector likely uses a convention or fallback. Verify by checking MetricPollJob.cs or SnmpPoller for community string handling.
**Warning signs:** E2E-SIM-2 polls fail with auth errors, poll_unreachable increments instead of poll_executed.

## Code Examples

### Fixture: OID Renamed ConfigMap (oid-renamed-configmap.yaml)
```yaml
# Full simetra-oidmaps ConfigMap with .999.1.1.0 renamed from e2e_gauge_test to e2e_renamed_gauge
# All other entries identical to deploy/k8s/snmp-collector/simetra-oidmaps.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-oidmaps
  namespace: simetra
data:
  oidmaps.json: |
    {
      ... all OBP entries unchanged ...
      ... all NPB entries unchanged ...
      "1.3.6.1.4.1.47477.999.1.1.0": "e2e_renamed_gauge",  <-- RENAMED
      "1.3.6.1.4.1.47477.999.1.2.0": "e2e_integer_test",
      ... rest of E2E entries unchanged ...
    }
```

### Fixture: OID Removed ConfigMap (oid-removed-configmap.yaml)
```yaml
# Full simetra-oidmaps ConfigMap WITHOUT .999.1.1.0 entry
# OID is still polled (devices unchanged), so it becomes metric_name="Unknown"
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-oidmaps
  namespace: simetra
data:
  oidmaps.json: |
    {
      ... all OBP entries unchanged ...
      ... all NPB entries unchanged ...
      # "1.3.6.1.4.1.47477.999.1.1.0": "e2e_gauge_test",  <-- REMOVED
      "1.3.6.1.4.1.47477.999.1.2.0": "e2e_integer_test",
      ... rest of E2E entries unchanged ...
    }
```

### Fixture: OID Added ConfigMap (oid-added-configmap.yaml)
```yaml
# Full simetra-oidmaps ConfigMap WITH .999.2.1.0 mapped to e2e_unmapped_gauge
# The unmapped OID must already be polled (via e2e-sim-unmapped-configmap.yaml devices fixture)
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-oidmaps
  namespace: simetra
data:
  oidmaps.json: |
    {
      ... all existing entries unchanged ...
      "1.3.6.1.4.1.47477.999.2.1.0": "e2e_unmapped_gauge"  <-- ADDED
    }
```

### Fixture: Device Added ConfigMap (device-added-configmap.yaml)
```yaml
# Full simetra-devices ConfigMap with E2E-SIM-2 added
# Points to same E2E simulator IP, same OIDs as E2E-SIM
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-devices
  namespace: simetra
data:
  devices.json: |
    [
      ... OBP-01 unchanged ...
      ... NPB-01 unchanged ...
      ... E2E-SIM unchanged ...
      {
        "Name": "E2E-SIM-2",
        "IpAddress": "e2e-simulator.simetra.svc.cluster.local",
        "Port": 161,
        "MetricPolls": [
          {
            "IntervalSeconds": 10,
            "Oids": [
              "1.3.6.1.4.1.47477.999.1.1.0"
            ]
          }
        ]
      }
    ]
```

### Polling Pattern for Label-Based Assertions
```bash
# Source: tests/e2e/scenarios/15-unknown-oid.sh (established pattern)
DEADLINE=$(( $(date +%s) + 60 ))
FOUND=0
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    RESULT=$(query_prometheus 'snmp_gauge{device_name="E2E-SIM",metric_name="e2e_renamed_gauge"}')
    COUNT=$(echo "$RESULT" | jq -r '.data.result | length' 2>/dev/null) || COUNT=0
    if [ "$COUNT" -gt 0 ]; then
        FOUND=1
        break
    fi
    sleep 3
done
```

## Key Data Points

### E2E-SIM OID Inventory
| OID Suffix | Full OID | Metric Name | SNMP Type | Status |
|------------|----------|-------------|-----------|--------|
| .999.1.1.0 | 1.3.6.1.4.1.47477.999.1.1.0 | e2e_gauge_test | Gauge32 (42) | Mapped |
| .999.1.2.0 | 1.3.6.1.4.1.47477.999.1.2.0 | e2e_integer_test | Integer32 (100) | Mapped |
| .999.1.3.0 | 1.3.6.1.4.1.47477.999.1.3.0 | e2e_counter32_test | Counter32 (5000) | Mapped |
| .999.1.4.0 | 1.3.6.1.4.1.47477.999.1.4.0 | e2e_counter64_test | Counter64 (1000000) | Mapped |
| .999.1.5.0 | 1.3.6.1.4.1.47477.999.1.5.0 | e2e_timeticks_test | TimeTicks (360000) | Mapped |
| .999.1.6.0 | 1.3.6.1.4.1.47477.999.1.6.0 | e2e_info_test | OctetString | Mapped |
| .999.1.7.0 | 1.3.6.1.4.1.47477.999.1.7.0 | e2e_ip_test | IpAddress | Mapped |
| .999.2.1.0 | 1.3.6.1.4.1.47477.999.2.1.0 | (unmapped) | Gauge32 (99) | Unmapped |
| .999.2.2.0 | 1.3.6.1.4.1.47477.999.2.2.0 | (unmapped) | OctetString | Unmapped |

### Propagation Timing Budget
| Stage | Latency | Notes |
|-------|---------|-------|
| K8s watch API event delivery | ~1-2s | Watcher receives Modified event |
| OidMapService.UpdateMap() | <1ms | Atomic FrozenDictionary swap |
| DeviceWatcherService reload + DynamicPollScheduler reconcile | ~2-3s | Async DNS + Quartz job diff |
| Next poll cycle | 0-10s | Depends on where in interval |
| MediatR pipeline + OTel recording | <1ms | Synchronous within poll |
| OTel export to Prometheus | 0-15s | 15s export interval |
| Prometheus scrape | already scraped | Push-based via OTel |
| **Total worst case** | **~30s** | Use 60s timeout for safety |

### ConfigMap File Mappings
| ConfigMap Name | Data Key | Watcher Service | Reload Target |
|----------------|----------|-----------------|---------------|
| simetra-oidmaps | oidmaps.json | OidMapWatcherService | OidMapService.UpdateMap() |
| simetra-devices | devices.json | DeviceWatcherService | DeviceRegistry.ReloadAsync() + DynamicPollScheduler.ReconcileAsync() |

### Scenario Plan

| # | Name | Fixture Type | ConfigMap | Assertion Method |
|---|------|-------------|-----------|-----------------|
| 18 | OID Rename | oidmaps | simetra-oidmaps | Poll for new metric_name label |
| 19 | OID Remove | oidmaps | simetra-oidmaps | Poll for metric_name="Unknown" on specific OID |
| 20 | OID Add | oidmaps + devices | Both | Two-step: devices first (poll unmapped), then oidmaps (add mapping) |
| 21 | Device Add | devices | simetra-devices | poll_executed delta > 0 for new device_name |
| 22 | Device Remove | devices | simetra-devices | poll_executed delta = 0 after removal |
| 23 | Device Modify Interval | devices | simetra-devices | poll_executed delta comparison (5s should be ~2x of 10s) |

## Open Questions

### 1. E2E-SIM-2 Community String Authentication
- **What we know:** The E2E simulator accepts community "Simetra.E2E-SIM". Device configs seen so far don't include an explicit CommunityString field in the standard structure (only added via jq patch in scenario 07).
- **What's unclear:** How does MetricPollJob derive the community string? Does it use a convention like "Simetra.{DeviceName}"? If so, "E2E-SIM-2" would try "Simetra.E2E-SIM-2" and fail auth.
- **Recommendation:** During planning, read MetricPollJob.cs to determine community string derivation. If it derives from device name, the device-added fixture needs a CommunityString field, or the device should be named exactly "E2E-SIM" (but that creates a name collision). Alternatively, name it something that matches the simulator's accepted community, or add CommunityString to the DeviceOptions model if it already supports it.

### 2. Device Modify Interval: Statistical Reliability
- **What we know:** At 10s interval, 20s window should yield ~2 polls. At 5s interval, 20s window should yield ~4 polls. The ratio should be roughly 2x.
- **What's unclear:** With OTel 15s export batching, the exact count in a 20s window is noisy. Short windows may not reliably show the ratio.
- **Recommendation:** Use a longer measurement window (30s) and assert only that delta_5s > delta_10s (not exact ratio). This is robust enough while still proving the interval change took effect.

## Sources

### Primary (HIGH confidence)
- Codebase inspection: `tests/e2e/` -- all 17 existing scenarios, lib/, fixtures/
- Codebase inspection: `src/SnmpCollector/Services/OidMapWatcherService.cs` -- K8s watch API, UpdateMap() call
- Codebase inspection: `src/SnmpCollector/Services/DeviceWatcherService.cs` -- K8s watch API, DeviceRegistry + DynamicPollScheduler reconcile
- Codebase inspection: `src/SnmpCollector/Pipeline/OidMapService.cs` -- FrozenDictionary swap, "Unknown" fallback
- Codebase inspection: `src/SnmpCollector/Services/DynamicPollScheduler.cs` -- Quartz job reconciliation (add/remove/reschedule)
- Codebase inspection: `deploy/k8s/snmp-collector/simetra-oidmaps.yaml` -- 109 OID entries (OBP + NPB + E2E-SIM)
- Codebase inspection: `deploy/k8s/snmp-collector/simetra-devices.yaml` -- 3 devices (OBP-01, NPB-01, E2E-SIM)
- Codebase inspection: `simulators/e2e-sim/e2e_simulator.py` -- 9 OIDs served, static values, community string

### Secondary (MEDIUM confidence)
- Prometheus staleness: 5-minute default, documented in Prometheus official docs. Verified by project's existing test design (scenarios never assert metric absence).

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Entire stack is established by Phases 21-22, no new tools needed
- Architecture: HIGH - All 6 scenarios follow patterns identical to existing scenarios 01-17
- Pitfalls: HIGH - All pitfalls derived from direct codebase inspection and existing test patterns
- Open question (community string): MEDIUM - Needs MetricPollJob.cs inspection during planning

**Research date:** 2026-03-09
**Valid until:** 2026-04-09 (stable -- no external dependencies, all codebase-internal)
