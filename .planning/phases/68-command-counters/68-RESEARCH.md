# Phase 68: Command Counters - Research

**Researched:** 2026-03-22
**Domain:** E2E scenario shell scripts, SNMP SET command lifecycle counters
**Confidence:** HIGH

---

## Summary

Phase 68 adds E2E scenarios 83-86 that verify the three SNMP SET command lifecycle counters: `snmp.command.dispatched`, `snmp.command.suppressed`, and `snmp.command.failed`. All counter mechanics have been read directly from source. The E2E infrastructure already exercises these counters in PSS-04 (scenario 56) and PSS-06 (scenario 58) — Phase 68 is a focused, labelled re-exercise in the CCV framework with dedicated report category.

All four CCV requirements are directly implementable with existing fixtures and E2E infrastructure. CCV-01 and CCV-02 (dispatched and suppressed) mirror the PSS-04 and PSS-06 patterns exactly. CCV-03 (suppression detail: dispatched does NOT increment during suppression window) is the PSS-06D pattern. CCV-04 (command.failed via unmapped command name) requires a new fixture variant that references a non-existent CommandName, causing CommandWorkerService line 107 to fire.

The critical insight for CCV-04: `snmp.command.failed` fires in `CommandWorkerService.ExecuteCommandAsync` at line 107 (OID not found in command map), not in SnapshotJob. SnapshotJob's only failure path for commands is channel-full (line 208), which is impractical to trigger in E2E. The OID-not-found path is the reliable CCV-04 trigger.

**Primary recommendation:** Create four scenario files (83-86) covering CCV-01 through CCV-04. CCV-01 and CCV-02 are near-verbatim copies of PSS-04 and PSS-06 patterns. CCV-03 follows PSS-06D. CCV-04 requires a new fixture `tenant-cfg09-ccv-failed.yaml` with an unmapped CommandName.

---

## Standard Stack

All infrastructure already exists. No new libraries or tools needed.

### Core (all present)
| Component | Location | Purpose |
|-----------|----------|---------|
| `PipelineMetricService` | `src/SnmpCollector/Telemetry/PipelineMetricService.cs` | Three command counters |
| `SnapshotJob` | `src/SnmpCollector/Jobs/SnapshotJob.cs` | Tier=4 dispatch + suppression |
| `CommandWorkerService` | `src/SnmpCollector/Services/CommandWorkerService.cs` | CommandFailed on OID-not-found, timeout |
| `SuppressionCache` | `src/SnmpCollector/Pipeline/SuppressionCache.cs` | TrySuppress logic |
| `CommandMapService` | `src/SnmpCollector/Pipeline/CommandMapService.cs` | ResolveCommandOid (null = failure) |
| `CommandChannel` | `src/SnmpCollector/Pipeline/CommandChannel.cs` | Bounded capacity=16, DropWrite |
| `tenant-cfg06-pss-single.yaml` | `tests/e2e/fixtures/` | Working tier=4 trigger, SuppressionWindowSeconds=10 |
| `tenant-cfg06-pss-suppression.yaml` | `tests/e2e/fixtures/` | SuppressionWindowSeconds=30 |
| E2E lib (common, prometheus, kubectl, sim) | `tests/e2e/lib/` | All test utilities |

### Prometheus metric names (OTel dots become underscores)
| C# instrument name | Prometheus name |
|--------------------|-----------------|
| `snmp.command.dispatched` | `snmp_command_dispatched_total` |
| `snmp.command.suppressed` | `snmp_command_suppressed_total` |
| `snmp.command.failed` | `snmp_command_failed_total` |

---

## Architecture Patterns

### Counter Semantics (exact, verified from source)

**`snmp.command.dispatched`** — incremented in `SnapshotJob.EvaluateTenant` at line 201 when `_commandChannel.Writer.TryWrite(request)` returns true. Fires once per successfully enqueued command per tier=4 evaluation cycle. Tag: `device_name = tenant.Id` (the tenant name, e.g., `"e2e-pss-tenant"`). This is the TENANT ID, not the device IP.

**`snmp.command.suppressed`** — incremented in `SnapshotJob.EvaluateTenant` at line 191 when `_suppressionCache.TrySuppress(suppressionKey, tenant.SuppressionWindowSeconds)` returns true. The suppression key is `"{tenant.Id}:{cmd.Ip}:{cmd.Port}:{cmd.CommandName}"`. Tag: `device_name = tenant.Id`. IMPORTANT: The suppression window stamp is set only on the FIRST call (when TrySuppress returns false). Subsequent calls within the window return true but do NOT refresh the stamp — the window is NOT extended.

**`snmp.command.failed`** — incremented in three places in `CommandWorkerService.ExecuteCommandAsync`:
  1. Line 87 (outer catch): unhandled exception from the ExecuteCommandAsync method itself — tag: `device_name = "{req.Ip}:{req.Port}"`
  2. Line 107: OID not found in command map — tag: `device_name = "{req.Ip}:{req.Port}"` (IP:port string, not friendly name)
  3. Line 117: Device not found in device registry — tag: `device_name = "{req.Ip}:{req.Port}"`
  4. Line 159: SET timeout — tag: `device_name = device.Name` (friendly device name from registry)

  Also incremented in `SnapshotJob.EvaluateTenant` at line 208 when `TryWrite` returns false (channel full) — tag: `device_name = tenant.Id`. This channel-full path is NOT reliable to trigger in E2E (channel capacity=16 with IntervalSeconds=1).

### Label values — critical for Prometheus queries

| Counter | Increment site | device_name value |
|---------|----------------|-------------------|
| `snmp_command_dispatched_total` | SnapshotJob line 201 | tenant.Id (e.g., `"e2e-pss-tenant"`) |
| `snmp_command_suppressed_total` | SnapshotJob line 191 | tenant.Id (e.g., `"e2e-pss-tenant"`) |
| `snmp_command_failed_total` | CommandWorkerService line 107 (OID not found) | `"{req.Ip}:{req.Port}"` — e.g., `"e2e-simulator.simetra.svc.cluster.local:161"` |
| `snmp_command_failed_total` | CommandWorkerService line 117 (device not found) | `"{req.Ip}:{req.Port}"` |
| `snmp_command_failed_total` | CommandWorkerService line 87 (exception) | `"{req.Ip}:{req.Port}"` |
| `snmp_command_failed_total` | CommandWorkerService line 159 (timeout) | `device.Name` (friendly name) |
| `snmp_command_failed_total` | SnapshotJob line 208 (channel full) | tenant.Id |

**CCV-04 consequence**: The `snmp_command_failed_total` counter for the OID-not-found path uses `device_name="e2e-simulator.simetra.svc.cluster.local:161"`, not the tenant name. Prometheus query must use this label or query without filter using `sum(snmp_command_failed_total)`.

### Tier=4 trigger pattern (verified from SnapshotJob source)

Tier=4 fires when:
- Pre-tier: ALL holders are past grace window (IsReady = true)
- Tier 1: No staleness detected (OR stale — stale skips directly to tier=4)
- Tier 2: NOT all Resolved holders violated (gate passes when at least one is in-range)
- Tier 3: ALL Evaluate holders violated

Standard test pattern from PSS-04/PSS-06:
1. Apply tenant fixture, wait for reload
2. Prime T2 OIDs with in-range values (`5.1=10, 5.2=1, 5.3=1`)
3. Wait 8s for readiness grace (grace = 3 * 1 * 2.0 = 6s, +2s margin)
4. Snapshot baseline counter
5. Set evaluate OID to violated (`5.1=0`, value < Min:10)
6. Poll for counter increment

### Suppression window pattern (verified from SuppressionCache source)

The suppression key format: `"{tenant.Id}:{cmd.Ip}:{cmd.Port}:{cmd.CommandName}"`

Example: `"e2e-pss-tenant-supp:e2e-simulator.simetra.svc.cluster.local:161:e2e_set_bypass"`

The stamp is set ONCE when TrySuppress returns false (first call = dispatch). All calls within windowSeconds return true (suppress) WITHOUT refreshing the stamp. This means:
- Window 1: First tier=4 cycle → dispatched increments (TrySuppress returns false, stamps now)
- Window 2: All subsequent cycles within windowSeconds → suppressed increments each cycle
- After window expires: dispatch fires again

For E2E with IntervalSeconds=1 and SuppressionWindowSeconds=30: after the first dispatch, subsequent 1s cycles will ALL suppress for 30 seconds. PSS-06 uses a 15s wait between W1 and W2 baseline, then polls for the suppressed increment.

### CCV-04: Triggering command.failed via unmapped CommandName

The reliable path: configure a tenant's Commands block with a `CommandName` that does NOT exist in `simetra-oid-command-map`. When SnapshotJob reaches tier=4 and calls `_commandChannel.Writer.TryWrite(request)`, the command IS dispatched (dispatched increments). Then CommandWorkerService dequeues it, calls `_commandMapService.ResolveCommandOid(req.CommandName)` — returns null — increments `_commandFailed` with tag `device_name="{req.Ip}:{req.Port}"`.

Sequence: dispatch increments THEN failed increments (they're separate events). The scenario must:
1. Snapshot both baselines
2. Trigger tier=4 (evaluate violated)
3. Poll for dispatched counter increment (SnapshotJob writes to channel)
4. Poll for failed counter increment (CommandWorkerService processes it)
5. Assert dispatched delta >= 1 AND failed delta >= 1

Fixture needed: `tenant-cfg09-ccv-failed.yaml` — use tenant name `"e2e-ccv-failed"` with CommandName `"e2e_set_unknown"` (not in command map). Same T2 OID structure as cfg06.

The `e2e-simulator.simetra.svc.cluster.local:161` label query: The device IS registered (e2e-simulator is a real device), so device-not-found path will NOT fire. The OID-not-found path fires because `e2e_set_unknown` is absent from the reverse map in CommandMapService. The Prometheus query for this counter uses `device_name="e2e-simulator.simetra.svc.cluster.local:161"` OR a label-free sum.

### Existing tenant fixtures for tier=4
| Fixture | Tenant Name | SuppressionWindowSeconds | Notes |
|---------|-------------|--------------------------|-------|
| `tenant-cfg06-pss-single.yaml` | `e2e-pss-tenant` | 10 | Used by PSS-04 |
| `tenant-cfg06-pss-suppression.yaml` | `e2e-pss-tenant-supp` | 30 | Used by PSS-06 |
| `tenant-cfg01-single.yaml` | `e2e-tenant-A` | 10 | T1 OIDs (.999.4.x) |

All existing command fixtures use `CommandName: "e2e_set_bypass"`, which IS in the command map at OID `1.3.6.1.4.1.47477.999.4.4.0` (`e2e_command_response`).

### Report category update

Current categories in `tests/e2e/lib/report.sh`:
```
"Pipeline Counter Verification|68|81"
```

This covers 0-based indices 68-81 = scenario files 69-82.

Phase 68 scenarios are files 83-86 = 0-based indices 82-85. A new category must be appended:
```
"Command Counter Verification|82|85"
```

The exact end index depends on how many sub-assertions each scenario records. Since each CCV scenario records 1 sub-assertion, and Phase 68 has 4 scenarios (CCV-01, CCV-02, CCV-03, CCV-04), indices 82-85 is correct if each scenario file produces exactly one `record_pass/fail` entry. If scenarios produce multiple sub-assertions (like PSS-06 which produces 58a/58b/58c/58d each as separate records), the end index will be higher.

Looking at PSS-06 (scenario 58) — it records 4 sub-assertions. If CCV scenarios follow the same pattern, the index range will need to be wider. Use `82|89` as a safe upper bound for 4 scenarios with up to 2 sub-assertions each, or just use a large number like `82|99` since the report.sh clamps to actual results.

### Command channel (verified from CommandChannel.cs)
- Bounded capacity: 16
- FullMode: `DropWrite` (TryWrite returns false, no callback fired)
- SingleWriter: false (multiple SnapshotJob tenant evaluations write concurrently)
- SingleReader: true (CommandWorkerService drains)
- Channel-full path: SnapshotJob line 208 → `IncrementCommandFailed(tenant.Id)` — NOT practical for E2E testing

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Suppression lifecycle | New suppression fixture | Use `tenant-cfg06-pss-suppression.yaml` + PSS-06 pattern | Already tested, proven to work |
| Tier=4 trigger | Custom tenant | Use cfg06 fixtures with T2 OIDs | Same OID prefix (5.x), same thresholds |
| command.failed | Killing CommandWorkerService | Use unmapped CommandName | Clean, deterministic, no side effects |

---

## Common Pitfalls

### Pitfall 1: Wrong device_name label for command.failed
**What goes wrong:** Querying `snmp_command_failed_total{device_name="e2e-ccv-failed"}` returns nothing.
**Why it happens:** The OID-not-found path uses `device_name="{req.Ip}:{req.Port}"` = `"e2e-simulator.simetra.svc.cluster.local:161"`, not the tenant name.
**How to avoid:** Query without label filter: `sum(snmp_command_failed_total) or vector(0)` or use the exact IP:port string label.
**Warning signs:** Counter never increments despite logs showing "Command not found in command map".

### Pitfall 2: Confusing dispatched and failed timing for CCV-04
**What goes wrong:** Snapshotting failed baseline before dispatched increments, getting a stale baseline.
**Why it happens:** Dispatched fires in SnapshotJob (synchronous), failed fires in CommandWorkerService (async drain). Both need to be polled, not assumed to fire simultaneously.
**How to avoid:** Snapshot both baselines before triggering tier=4. Poll for dispatched first (faster, fires within 2s), then wait for failed (async drain may take another 1-2 cycles).

### Pitfall 3: Suppression window refreshing assumption
**What goes wrong:** Expecting suppressed to stop incrementing because "the window was extended."
**Why it happens:** SuppressionCache stamps ONLY on the non-suppressed (first) call. Suppressed calls do NOT refresh the stamp.
**How to avoid:** Trust the PSS-06 pattern as ground truth. The window starts from the FIRST dispatch time and never resets on suppression.

### Pitfall 4: Grace window not elapsed before tier=4 trigger
**What goes wrong:** Tier=4 never fires because the tenant is still in the grace window.
**Why it happens:** The readiness check requires `holder.IsReady = true` for all holders. With IntervalSeconds=1, GraceMultiplier=2.0, TimeSeriesSize=3: grace = 6 seconds.
**How to avoid:** Always prime OIDs in-range and wait 8s before taking the baseline counter snapshot.

---

## Code Examples

### CCV-01: Dispatched counter (mirrors PSS-04 pattern)
```bash
# Source: tests/e2e/scenarios/56-pss-04-unresolved.sh
BEFORE=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"')
sim_set_oid "5.1" "0"    # T2 eval violated (< Min:10)
if poll_until 30 2 "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"' "$BEFORE"; then
    AFTER=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"')
    DELTA=$((AFTER - BEFORE))
    assert_delta_ge "$DELTA" 1 "CCV-01: command.dispatched increments at tier=4" "$(get_evidence "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant"')"
fi
```

### CCV-02: Suppressed counter (mirrors PSS-06 Window 2 pattern)
```bash
# Source: tests/e2e/scenarios/58-pss-06-suppression.sh
# After first dispatch fires, wait 15s then snapshot suppressed baseline
sleep 15
BEFORE_SUPP=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-pss-tenant-supp"')
poll_until 30 5 "snmp_command_suppressed_total" 'device_name="e2e-pss-tenant-supp"' "$BEFORE_SUPP" || true
AFTER_SUPP=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-pss-tenant-supp"')
DELTA_SUPP=$((AFTER_SUPP - BEFORE_SUPP))
assert_delta_gt "$DELTA_SUPP" 0 "CCV-02: command.suppressed increments within window" "$(get_evidence "snmp_command_suppressed_total" 'device_name="e2e-pss-tenant-supp"')"
```

### CCV-03: Dispatched unchanged during suppression (mirrors PSS-06D pattern)
```bash
# Source: tests/e2e/scenarios/58-pss-06-suppression.sh (58d sub-assertion)
BEFORE_SENT_W2=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant-supp"')
# ... wait and poll for suppressed ...
AFTER_SENT_W2=$(snapshot_counter "snmp_command_dispatched_total" 'device_name="e2e-pss-tenant-supp"')
DELTA_SENT=$((AFTER_SENT_W2 - BEFORE_SENT_W2))
assert_delta_eq "$DELTA_SENT" 0 "CCV-03: command.dispatched unchanged while suppressed" "delta=${DELTA_SENT} expected=0"
```

### CCV-04: Failed counter via unmapped CommandName
```bash
# New fixture: tenant-cfg09-ccv-failed.yaml
# CommandName: "e2e_set_unknown" (not in simetra-oid-command-map)
# device_name label: "e2e-simulator.simetra.svc.cluster.local:161"
BEFORE_FAILED=$(snapshot_counter "snmp_command_failed_total" '')
sim_set_oid "5.1" "0"    # T2 eval violated -> tier=4 -> dispatch -> command worker -> OID not found -> failed
poll_until 30 2 "snmp_command_failed_total" '' "$BEFORE_FAILED" || true
AFTER_FAILED=$(snapshot_counter "snmp_command_failed_total" '')
DELTA_FAILED=$((AFTER_FAILED - BEFORE_FAILED))
assert_delta_ge "$DELTA_FAILED" 1 "CCV-04: command.failed increments for unmapped command" "$(get_evidence "snmp_command_failed_total" '')"
```

### Fixture: tenant-cfg09-ccv-failed.yaml
```yaml
# New file: tests/e2e/fixtures/tenant-cfg09-ccv-failed.yaml
# Tenant with CommandName not in command map (triggers command.failed)
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-tenants
  namespace: simetra
data:
  tenants.json: |
    [
      {
        "Name": "e2e-ccv-failed",
        "Priority": 1,
        "SuppressionWindowSeconds": 5,
        "Metrics": [
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "MetricName": "e2e_eval_T2",
            "TimeSeriesSize": 3,
            "GraceMultiplier": 2.0,
            "Role": "Evaluate",
            "Threshold": { "Min": 10.0 }
          },
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "MetricName": "e2e_res1_T2",
            "Role": "Resolved",
            "Threshold": { "Min": 1.0 }
          },
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "MetricName": "e2e_res2_T2",
            "Role": "Resolved",
            "Threshold": { "Min": 1.0 }
          }
        ],
        "Commands": [
          {
            "Ip": "e2e-simulator.simetra.svc.cluster.local",
            "Port": 161,
            "CommandName": "e2e_set_unknown",
            "Value": "0",
            "ValueType": "Integer32"
          }
        ]
      }
    ]
```

---

## State of the Art

| Old (PSS scenarios) | New (CCV scenarios) | Change | Impact |
|---------------------|---------------------|--------|--------|
| Command counter tested as side effect of tier=4 logic | Dedicated CCV scenarios with MCV-style labels | Phase 68 | Clean separation of concerns |
| PSS-06 tests both suppressed and dispatched-unchanged | CCV-02 and CCV-03 are separate labeled assertions | Phase 68 | Individual traceability per requirement |

---

## Open Questions

1. **CCV-04 label filter strategy**
   - What we know: OID-not-found path uses `device_name="{req.Ip}:{req.Port}"` = `"e2e-simulator.simetra.svc.cluster.local:161"` as a raw string
   - What's unclear: Whether this label value renders in Prometheus with the full hostname (may be too long), or if the collector truncates it
   - Recommendation: Use a label-free `sum(snmp_command_failed_total)` query as the primary assertion, with the IP:port filter as an optional secondary check. This avoids brittleness if the hostname is long.

2. **CCV-01 scope — deduplicate with PSS-04?**
   - What we know: PSS-04 (scenario 56) already proves dispatched increments at tier=4 for `e2e-pss-tenant`
   - What's unclear: Whether CCV-01 should reuse the same tenant fixture (PSS-04 pattern) or use a fresh CCV-specific tenant name
   - Recommendation: Use the same `tenant-cfg06-pss-single.yaml` and same tenant name `"e2e-pss-tenant"` for CCV-01. No new fixture needed. This is a re-exercise in the MCV framework, not an independent test.

---

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — exact metric names and tag signatures verified
- `src/SnmpCollector/Jobs/SnapshotJob.cs` — tier=4 dispatch path, suppression key format, dispatched/suppressed/failed increment sites verified
- `src/SnmpCollector/Services/CommandWorkerService.cs` — all command.failed paths verified (lines 87, 107, 117, 159)
- `src/SnmpCollector/Pipeline/SuppressionCache.cs` — stamp-only-on-first-call behavior verified
- `src/SnmpCollector/Pipeline/CommandChannel.cs` — capacity=16, DropWrite, SingleWriter=false verified
- `src/SnmpCollector/Pipeline/CommandMapService.cs` — ResolveCommandOid returns null for missing key verified
- `deploy/k8s/snmp-collector/simetra-oid-command-map.yaml` — `e2e_set_bypass` IS in the map, confirms OID `1.3.6.1.4.1.47477.999.4.4.0`
- `tests/e2e/scenarios/56-pss-04-unresolved.sh` — CCV-01 source pattern verified
- `tests/e2e/scenarios/58-pss-06-suppression.sh` — CCV-02 and CCV-03 source pattern verified
- `tests/e2e/lib/common.sh` — `assert_delta_eq`, `assert_delta_ge`, `assert_delta_gt` all present
- `tests/e2e/lib/report.sh` — current category ranges verified; `"Pipeline Counter Verification|68|81"` covers scenarios 69-82

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all files read directly from source
- Architecture: HIGH — all counter increment sites read from source, no inference
- Pitfalls: HIGH — derived from source code mechanics, not speculation
- CCV-04 label: MEDIUM — label value is known from source (`{req.Ip}:{req.Port}`), but Prometheus rendering of a long hostname string is unverified in live cluster

**Research date:** 2026-03-22
**Valid until:** 2026-04-22 (stable infrastructure, no expected changes)
