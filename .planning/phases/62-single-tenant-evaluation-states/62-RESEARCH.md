# Phase 62: Single Tenant Evaluation States - Research

**Researched:** 2026-03-20
**Domain:** E2E snapshot evaluation testing (bash scenario scripts, K8s fixtures, simulator control)
**Confidence:** HIGH

## Summary

Phase 62 creates a progressive E2E snapshot suite (PSS) that tests every SnapshotJob evaluation outcome with a single-tenant fixture. This is structurally identical to the existing v2.1 SNS suite (scenarios 41-52) but uses a dedicated single-tenant fixture instead of the 4-tenant cfg05 fixture.

The existing codebase provides all necessary infrastructure: `sim.sh` (per-OID control, stale support), `kubectl.sh` (ConfigMap save/restore with annotation stripping), `prometheus.sh` (counter snapshot, polling), `common.sh` (record_pass/fail), and `report.sh` (categorized reports). The SnapshotJob evaluation logic is well-understood from source code analysis: pre-tier readiness, tier-1 staleness (excluding Trap/Command sources), tier-2 resolved gate, tier-3 evaluate gate, tier-4 command dispatch with suppression.

**Primary recommendation:** Create a new single-tenant fixture (tenant-cfg06-pss-single.yaml) with 1 Evaluate + 2 Resolved metrics mapped to existing .999.4.x OIDs, IntervalSeconds=1, SuppressionWindowSeconds=10. Scenarios numbered 53-62. Reuse e2e_total_util (synthetic aggregate) for PSS-03. Defer trap/command immunity (PSS-04/05) to use the existing e2e_command_response OID as a command-sourced Resolved holder.

## Standard Stack

### Core Infrastructure (REUSE -- do not create new)

| Component | Location | Purpose | Verified |
|-----------|----------|---------|----------|
| sim.sh | tests/e2e/lib/sim.sh | sim_set_oid, sim_set_oid_stale, reset_oid_overrides, reset_scenario | YES - read source |
| kubectl.sh | tests/e2e/lib/kubectl.sh | save_configmap (annotation strip), restore_configmap | YES - read source |
| prometheus.sh | tests/e2e/lib/prometheus.sh | snapshot_counter, poll_until, query_counter | YES - read source |
| common.sh | tests/e2e/lib/common.sh | record_pass, record_fail, log_info/warn/error | YES - read source |
| report.sh | tests/e2e/lib/report.sh | generate_report with category ranges | YES - read source |
| e2e_simulator.py | simulators/e2e-sim/ | HTTP per-OID control, stale (NoSuchInstance) | YES - via sim.sh API |

### Existing OIDs Available

| OID Suffix | MetricName | Poll Interval | Notes |
|------------|-----------|---------------|-------|
| 4.1 | e2e_port_utilization | 10s (default poll) or 1s (T2-T4 poll) | Already used by cfg01/cfg05 |
| 4.2 | e2e_channel_state | 10s or 1s | Already used by cfg01/cfg05 |
| 4.3 | e2e_bypass_status | 10s or 1s | Already used by cfg01/cfg05 |
| 4.4 | e2e_command_response | 10s | Command OID -- receives SET responses (Source=Command) |
| 4.5 | e2e_agg_source_a | 10s | Aggregation source for e2e_total_util (Synthetic) |
| 4.6 | e2e_agg_source_b | 10s | Aggregation source for e2e_total_util (Synthetic) |
| 5.x | e2e_eval_T2, e2e_res1_T2, e2e_res2_T2 | 1s | Used by cfg05 4-tenant fixture |
| 6.x | T3 metrics | 1s | Used by cfg05 |
| 7.x | T4 metrics | 1s | Used by cfg05 |

### Device Poll Config for E2E-SIM

The E2E-SIM device has a 1s poll group for T2-T4 metrics (.999.5-7.x). The .999.4.x OIDs are in a 10s poll group. **For PSS with IntervalSeconds=1, the fixture MUST reference metrics from the 1s poll group** or the timing math breaks.

**Critical finding:** The existing .999.4.x OIDs (e2e_port_utilization, e2e_channel_state, e2e_bypass_status) are polled at 10s interval by the device config. The v2.1 SNS suite (cfg05) uses these same OIDs but cfg05 tenants G1-T1 reference them with tenant-level IntervalSeconds not specified (defaults from device). However, examining cfg05 more carefully, the tenant fixture does NOT specify IntervalSeconds -- it uses the holder's interval from the device poll config.

**Wait -- re-examining:** The tenant metrics config does NOT have IntervalSeconds. Looking at MetricSlotHolder constructor, IntervalSeconds is set from the device poll config, not the tenant config. The T2-T4 metrics (.999.5-7.x) are in the 1s poll group, which is why the SNS suite achieves 1s evaluation cycles.

**Conclusion:** PSS fixture must use metrics from the 1s poll group (.999.5.x, .999.6.x, or .999.7.x) to get 1s IntervalSeconds on the holders. Using .999.4.x OIDs would give 10s intervals and 20s grace windows instead of 6s.

## Architecture Patterns

### Scenario Script Pattern (from v2.1 SNS suite)

Every scenario follows this exact structure:

```bash
# Header comment with scenario number, name, fixture, OID map, timing math
FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# --- Setup ---
log_info "PSS-XX: Saving original tenant ConfigMap..."
save_configmap "simetra-tenants" "simetra" "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true

log_info "PSS-XX: Applying fixture..."
kubectl apply -f "$FIXTURES_DIR/tenant-cfg06-pss-single.yaml" > /dev/null 2>&1 || true

log_info "PSS-XX: Waiting for tenant vector reload..."
if poll_until_log 60 5 "Tenant vector reload complete\|TenantVectorWatcher initial load complete" 30; then
    log_info "PSS-XX: Tenant vector reload confirmed"
else
    log_warn "PSS-XX: Tenant vector reload not detected within 60s; proceeding"
fi

# --- Prime (if needed) ---
sim_set_oid "X.Y" "value"
sleep 8  # grace window

# --- Baseline capture ---
BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')

# --- Stimulus ---
sim_set_oid "X.Y" "new_value"  # or sim_set_oid_stale

# --- Assert ---
if poll_until_log 30 1 "tenant-name.*tier=N" 15; then
    record_pass "PSS-XXA: description" "evidence"
else
    record_fail "PSS-XXA: description" "evidence"
fi

# --- Cleanup ---
reset_oid_overrides
if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
    restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || \
        log_warn "PSS-XX: Failed to restore tenant ConfigMap from snapshot"
fi
```

### Tenant Fixture Pattern

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-tenants
  namespace: simetra
data:
  tenants.json: |
    [
      {
        "Name": "e2e-pss-tenant",
        "Priority": 1,
        "SuppressionWindowSeconds": 10,
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
            "CommandName": "e2e_set_bypass",
            "Value": "0",
            "ValueType": "Integer32"
          }
        ]
      }
    ]
```

### OID Suffix Mapping for PSS Fixture

Since PSS reuses T2 OIDs (.999.5.x) which are in the 1s poll group:

| OID Suffix | MetricName | Role | Threshold |
|-----------|-----------|------|-----------|
| 5.1 | e2e_eval_T2 | Evaluate | Min: 10.0 |
| 5.2 | e2e_res1_T2 | Resolved | Min: 1.0 |
| 5.3 | e2e_res2_T2 | Resolved | Min: 1.0 |

Grace = TimeSeriesSize(3) * IntervalSeconds(1) * GraceMultiplier(2.0) = 6s
Staleness threshold = IntervalSeconds(1) * GraceMultiplier(2.0) = 2s

### Log Patterns to Match (from SnapshotJob.cs source)

| Tier | Log Pattern (exact from source) | Template ID |
|------|--------------------------------|-------------|
| Pre-tier | `{TenantId}...not ready (in grace window)` | LogDebug |
| Tier 1 | `{TenantId}...tier=1 stale` | LogDebug |
| Tier 2 (all violated) | `{TenantId}...tier=2 — all resolved violated, no commands` | LogDebug |
| Tier 2 (not all) | `{TenantId}...tier=2 — resolved not all violated, proceeding` | LogDebug |
| Tier 3 (healthy) | `{TenantId}...tier=3 — not all evaluate metrics violated` | LogDebug |
| Tier 4 | `{TenantId}...tier=4 — commands enqueued, count={N}` | LogInformation |
| Suppression | `Command {CommandName} suppressed for tenant {TenantId}` | LogDebug |

**Important:** tier=4 log uses LogInformation, all others use LogDebug. Grep patterns must handle both em-dash and double-dash variants (existing v2.1 scripts already do this with `\|` alternation).

### Prometheus Counter Names

| Counter | Label Filter | Purpose |
|---------|-------------|---------|
| snmp_command_sent_total | device_name="E2E-SIM" | Command dispatch confirmed |
| snmp_command_suppressed_total | device_name="e2e-pss-tenant" | Suppression counter (label is tenant ID) |

**Note:** The suppressed counter uses `device_name` label but the value is actually the tenant ID (from `IncrementCommandSuppressed(tenant.Id)`). The sent counter uses the actual device name "E2E-SIM".

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| OID value control | Custom HTTP calls | sim_set_oid / sim_set_oid_stale | Already handles error codes, logging |
| ConfigMap backup/restore | Manual kubectl get/apply | save_configmap / restore_configmap | Handles annotation stripping, resourceVersion removal |
| Counter polling | Custom polling loops | poll_until / snapshot_counter | Handles timeout, baseline comparison |
| Log polling | Custom kubectl logs loops | poll_until_log | Handles multi-pod search, since window |
| Test result tracking | Custom counters | record_pass / record_fail | Integrates with report.sh |
| Report categories | Manual formatting | report.sh _REPORT_CATEGORIES | Existing category system |

## Common Pitfalls

### Pitfall 1: Wrong Poll Interval (10s vs 1s)
**What goes wrong:** Fixture references .999.4.x OIDs which are in the 10s poll group. Grace becomes 60s instead of 6s. All timing assumptions break.
**Why it happens:** The OID-to-interval mapping comes from the device poll config, not the tenant fixture.
**How to avoid:** Use metrics from the 1s poll group (.999.5.x, .999.6.x, .999.7.x). Verify with device config.
**Warning signs:** Grace window > 6s, scenarios timing out.

### Pitfall 2: ConfigMap Annotation Double-Reload
**What goes wrong:** kubectl apply fires two Modified events when last-applied-configuration annotation is present. Second event causes unexpected tenant reload mid-scenario.
**Why it happens:** K8s annotates ConfigMaps with last-applied-configuration on first apply. Subsequent applies update both the data and the annotation, triggering two watch events.
**How to avoid:** save_configmap already strips the annotation. Always use save_configmap before applying fixtures.
**Warning signs:** "Tenant vector reload" log appearing twice.

### Pitfall 3: Log Contamination from Prior Scenarios
**What goes wrong:** poll_until_log with `--since=10s` captures logs from a prior sub-assertion, causing false positives.
**Why it happens:** Scenario scripts run sequentially in the same shell. Short `--since` windows overlap.
**How to avoid:** Use flush sleeps (3-5s) between sub-assertions that change OID state. Use appropriate `--since` values (15s is the standard in v2.1 SNS).
**Warning signs:** Assertion passes before stimulus could have taken effect.

### Pitfall 4: Stale Age-Out Timing
**What goes wrong:** sim_set_oid_stale is called but staleness not detected because newest sample timestamp is still fresh.
**Why it happens:** After priming, the newest sample has a recent timestamp. Staleness requires the newest sample to be older than IntervalSeconds * GraceMultiplier = 2s.
**How to avoid:** Sleep 5s after switching to stale (with 1s interval, samples age out after ~3s; 5s gives margin).
**Warning signs:** tier=1 stale log not appearing within expected window.

### Pitfall 5: Suppression Counter Label Mismatch
**What goes wrong:** Querying snmp_command_suppressed_total with wrong label value (device name vs tenant ID).
**Why it happens:** IncrementCommandSuppressed takes tenant.Id as parameter but the label name is "device_name" (a telemetry naming artifact).
**How to avoid:** Use `device_name="e2e-pss-tenant"` for suppressed counter. Use `device_name="E2E-SIM"` for sent counter.
**Warning signs:** Counter always returns 0 despite suppression logs appearing.

### Pitfall 6: Shared OID State Between Scenarios
**What goes wrong:** A scenario starts with OID overrides left from the previous scenario, causing incorrect initial state.
**Why it happens:** Each scenario script is sourced sequentially. OID overrides persist across scripts.
**How to avoid:** Every scenario MUST call reset_oid_overrides at start (within setup) AND at cleanup. Belt-and-suspenders.
**Warning signs:** First sub-assertion of a scenario gets unexpected tier result.

## Design Decisions

### Decision 1: PSS Fixture OIDs -- Use T2 OIDs (.999.5.x)

**Recommendation:** Reuse e2e_eval_T2, e2e_res1_T2, e2e_res2_T2 from the 1s poll group.

**Rationale:**
- These OIDs are in the 1s IntervalSeconds poll group in simetra-devices.yaml
- This gives the desired 6s grace window (3 * 1 * 2.0)
- The existing cfg05 4-tenant fixture also uses these OIDs, but cfg05 is restored before PSS runs so no conflict
- No new OIDs or device config changes needed

**Risk:** If a prior scenario (41-52 SNS suite) fails to restore the tenant ConfigMap, T2 OIDs may still have active tenant holders. Mitigation: PSS always save/restore its own ConfigMap snapshot.

### Decision 2: PSS-04/05 Trap and Command Immunity

**Recommendation:** Use e2e_command_response (.999.4.4.0) as a Command-sourced Resolved metric in the fixture. For trap immunity, this requires a separate consideration.

**Analysis of the HasStaleness code:**
```csharp
if (holder.Source == SnmpSource.Trap || holder.Source == SnmpSource.Command || holder.IntervalSeconds == 0)
    continue;  // excluded from staleness check
```

The Source property on MetricSlotHolder is set when data arrives through the pipeline (WriteValue). A holder's Source depends on which pipeline path wrote to it:
- Source=Poll: from MetricPollJob (SNMP GET)
- Source=Trap: from ChannelConsumerService (trap receiver)
- Source=Command: from CommandWorkerService (SET response)
- Source=Synthetic: from MetricPollJob aggregate dispatch

**Problem:** The holder Source is set at write time. A tenant metric mapped to "e2e_command_response" will receive data from BOTH the 10s poll group (Source=Poll) AND from command SET responses (Source=Command). The last write determines the Source value. This is unreliable for testing immunity.

**Alternative approach:** The command response path (CommandWorkerService line 185) writes Source=Command to the holder when the SET response comes back. If the tenant has e2e_set_bypass as a command AND e2e_command_response as a metric, the command response will write to the e2e_command_response holder with Source=Command. But the 10s poll also writes to it with Source=Poll, overwriting the source.

**Recommendation:** Defer PSS-04 (trap immunity) and PSS-05 (command immunity) to unit test coverage (quick-070 already covers this). The E2E simulator does not have reliable infrastructure to produce a pure Trap-sourced or Command-sourced holder. Creating that infrastructure is out of scope for phase 62.

If the user wants E2E coverage: would need to either (a) add a new OID that is ONLY trap-sourced (not polled), or (b) remove e2e_command_response from the 10s poll group so it only gets Command-sourced writes. Both require device config changes.

### Decision 3: PSS-03 Synthetic Staleness

**Recommendation:** Create a separate fixture (tenant-cfg06-pss-synthetic.yaml) that uses e2e_total_util (the synthetic aggregate) as the Evaluate metric. This mirrors the existing STS-07 scenario (39-sts-07-synthetic-stale-to-commands.sh) approach with cfg04-aggregate.

**Caveat:** The synthetic aggregate (e2e_total_util) is computed from .999.4.5 and .999.4.6 which are in the 10s poll group. So the synthetic holder has IntervalSeconds=10 and grace=60s. This makes PSS-03 significantly slower (~55-60s). This is unavoidable without adding new aggregate OIDs to the 1s poll group.

**Alternative:** Accept the 10s timing for PSS-03 only, or skip PSS-03 if synthetic stale is already covered by STS-07 (scenario 39).

### Decision 4: Scenario Numbering

**Recommendation:** Continue from 53. The existing suite has scenarios 01-52. report.sh uses 0-based index ranges for categories. PSS scenarios 53-62 (or however many) need a new category entry.

### Decision 5: Report Category

**Recommendation:** Add a new category to report.sh:
```bash
"Progressive Snapshot Suite|52|62"
```
This covers scenarios 53-63 (0-based indices 52-62).

### Decision 6: Suppression Fixture

**Recommendation:** For PSS-10 (suppression), create a second fixture variant with SuppressionWindowSeconds=30 (matching the existing STS-04 approach with cfg01-suppression). With 1s SnapshotJob interval, the suppression window is easily observable within 30s. Name: tenant-cfg06-pss-suppression.yaml.

**Note:** The existing STS-04 suppression test uses a 30s window because the SnapshotJob runs at 15s intervals. With PSS's 1s interval, a 10s window would also work, but 30s gives more margin and matches the established pattern.

Actually, re-examining: the SnapshotJob interval is configured separately from the poll interval. Let me check.

The SnapshotJob interval is set in SnapshotJobOptions, not per-tenant. In the E2E cluster it is likely 15s. But the v2.1 SNS suite tests with cfg05 achieved tier logs within seconds because the SnapshotJob interval fires independently. The key question: what is the SnapshotJob interval in the E2E cluster?

Looking at the SNS-01 scenario: it expects "not ready" log within 15s of applying the fixture, polling every 1s. The SNS-02 scenario primes for 8s then expects stale detection within 30s. These timings suggest the SnapshotJob fires frequently enough (likely 1s interval based on the comment "With IntervalSeconds=1 the first cycle fires within ~1s of reload").

**Correction:** Looking more carefully at SnapshotJob -- it is a Quartz job. The interval at which it runs is separate from the poll interval. The tenant's holders just provide the data. The SnapshotJob evaluates all tenants each cycle. The cycle interval is likely 1s based on the SNS scenario comments.

For suppression: with SnapshotJob at 1s, a 10s SuppressionWindowSeconds means the second tier=4 fires at ~2s, well within 10s. Use 10s for simplicity.

## Code Examples

### PSS-01: Not Ready (Grace Window)
```bash
# Apply fixture, wait for reload, do NOT prime
# SnapshotJob evaluates within ~1s, holders have no data, logs "not ready"
# Assert within 15s (before 6s grace expires is implicit -- first eval cycle is ~1s)
if poll_until_log 15 1 "e2e-pss-tenant.*not ready" 15; then
    record_pass "PSS-01A: not ready during grace window" "log=not_ready_found"
fi
```

### PSS-02: Stale Poll Data -> tier=1 -> tier=4
```bash
# Prime with in-range values, sleep 8s (grace)
sim_set_oid "5.1" "10"  # eval in-range
sim_set_oid "5.2" "1"   # res1 in-range
sim_set_oid "5.3" "1"   # res2 in-range
sleep 8

BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')

# Switch to stale
sim_set_oid_stale "5.1"
sim_set_oid_stale "5.2"
sim_set_oid_stale "5.3"
sleep 5  # staleness age-out

# Assert tier=1 stale, then tier=4 commands
poll_until_log 30 1 "e2e-pss-tenant.*tier=1 stale" 15
poll_until_log 30 1 "e2e-pss-tenant.*tier=4.*commands enqueued\|e2e-pss-tenant.*tier=4 -- commands enqueued" 15
poll_until 30 2 "snmp_command_sent_total" 'device_name="E2E-SIM"' "$BEFORE_SENT"
```

### PSS-06: All Resolved Violated -> tier=2
```bash
# Prime, sleep 8s
BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')

# Violate both resolved
sim_set_oid "5.2" "0"  # res1 < Min:1
sim_set_oid "5.3" "0"  # res2 < Min:1

# Assert tier=2
poll_until_log 30 1 "e2e-pss-tenant.*tier=2" 15

# Negative: no commands dispatched
sleep 10
AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
DELTA=$((AFTER_SENT - BEFORE_SENT))
# DELTA must be 0
```

### PSS-10: Suppression Window
```bash
# Apply suppression fixture (SuppressionWindowSeconds=30 or 10)
# Prime, sleep 8s
# Set eval violated to trigger tier=4

BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
sim_set_oid "5.1" "0"  # eval violated

# Window 1: first command sent
poll_until 30 2 "snmp_command_sent_total" 'device_name="E2E-SIM"' "$BEFORE_SENT"

# Window 2: suppressed (within window)
BEFORE_SUPP=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-pss-tenant-supp"')
poll_until 30 2 "snmp_command_suppressed_total" 'device_name="e2e-pss-tenant-supp"' "$BEFORE_SUPP"
AFTER_SUPP=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-pss-tenant-supp"')
# DELTA_SUPP must be > 0
```

## State of the Art

| Old Approach (v2.1 SNS) | New Approach (v2.2 PSS) | Impact |
|--------------------------|------------------------|--------|
| 4-tenant fixture (cfg05) with 12 OIDs to prime | 1-tenant fixture with 3 OIDs | Simpler setup, faster priming |
| 10s poll interval for .999.4.x | 1s poll interval via .999.5.x | 6s grace instead of 60s |
| Shared OIDs across tenants cause cross-talk | Single tenant, no cross-talk | More reliable assertions |
| save_configmap without annotation strip | save_configmap WITH annotation strip (quick-080) | No double-reload |

## Open Questions

1. **SnapshotJob cycle interval in E2E cluster**
   - What we know: SNS scenarios expect logs within 1-2s of state change, suggesting ~1s cycle
   - What's unclear: Actual SnapshotJobOptions.IntervalSeconds value deployed
   - Recommendation: Check appsettings or Helm values; if 1s, PSS timing works as designed

2. **PSS-04/05 trap and command immunity feasibility**
   - What we know: Source is set at WriteValue time. Polled OIDs get Source=Poll. There is no OID that is exclusively trap-sourced in the E2E sim.
   - What's unclear: Whether we can avoid polling e2e_command_response to get pure Command source
   - Recommendation: Defer to unit tests (quick-070). If user wants E2E, needs device config changes.

3. **PSS-03 synthetic stale timing**
   - What we know: Synthetic aggregate (e2e_total_util) uses 10s poll group sources
   - What's unclear: Whether adding synthetic sources to the 1s poll group is in scope
   - Recommendation: Accept 10s timing for PSS-03 or skip if STS-07 already covers this case

4. **Fixture OID contention with concurrent scenario execution**
   - What we know: PSS uses T2 OIDs (.999.5.x). SNS suite also uses T2 via cfg05. Both save/restore ConfigMaps.
   - What's unclear: Could parallel test execution cause issues?
   - Recommendation: E2E tests run sequentially (run-all.sh sources scripts in order). No issue.

## Sources

### Primary (HIGH confidence)
- **SnapshotJob.cs** - Read full source. Tier evaluation logic (lines 134-218), staleness exclusion (lines 240-260), resolved gate (270-311), evaluate gate (322-363), suppression (179-209).
- **MetricSlotHolder.cs** - Read full source. Source property, WriteValue, ReadSlot, ReadSeries, IsReady, ReadinessGrace.
- **SnmpSource.cs** - Enum: Poll, Trap, Synthetic, Command.
- **simetra-devices.yaml** - E2E-SIM device poll config. 10s group for .999.4.x, 1s group for .999.5-7.x.
- **simetra-oid-metric-map.yaml** - Full OID mapping including .999.4.x and .999.5-7.x.
- **simetra-oid-command-map.yaml** - e2e_set_bypass mapped to .999.4.4.0.
- **Existing scenario scripts 41-52** - Complete v2.1 SNS suite patterns.
- **Existing scenario script 32** - STS-04 suppression window pattern.
- **Existing fixtures** - tenant-cfg01-single, cfg01-suppression, cfg05-four-tenant-snapshot.
- **lib/*.sh** - All 5 library files read in full.

### Secondary (MEDIUM confidence)
- **PipelineMetricService.cs** - Counter names: snmp.command.suppressed, IncrementCommandSuppressed(deviceName=tenant.Id).
- **CommandWorkerService.cs** - Source=Command on SET response write (line 185).

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - read all source files directly
- Architecture: HIGH - existing v2.1 patterns verified from 12 scenario scripts
- Pitfalls: HIGH - derived from actual v2.1 debugging sessions (documented in phase context)
- Design decisions: MEDIUM - PSS-04/05 immunity analysis depends on runtime Source behavior

**Research date:** 2026-03-20
**Valid until:** 2026-04-20 (stable infrastructure, no planned changes)
