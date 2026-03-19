# Phase 59: Advance Gate Fix & Priority Starvation Simulation - Research

**Researched:** 2026-03-19
**Domain:** SnapshotJob TierResult enum, advance gate logic, E2E simulation
**Confidence:** HIGH (all findings from direct source code inspection)

---

## Summary

This phase has two co-located changes: (1) a semantic fix to `TierResult` that reclassifies suppressed-all-commands from `Violated` to `Unresolved`, and (2) a new E2E simulation scenario that proves P2 starvation when P1 is in an active command cycle.

The bug is well-understood and in a single line. The rename (`Violated` → `Resolved`, `Commanded` → `Unresolved`) touches one enum definition, three return-site expressions in `SnapshotJob.cs`, two loop references in `Execute`, and approximately 35 assertion references in `SnapshotJobTests.cs`. The fix is the suppression path: `return enqueueCount > 0 ? TierResult.Commanded : TierResult.Violated` must become `return TierResult.Unresolved` regardless of enqueueCount.

The simulation fixture reuses the existing `tenant-cfg03-two-diff-prio.yaml` (P1 priority=1, SuppressionWindowSeconds=10, P2 priority=2). The `command_trigger` simulator scenario already exists. The E2E test pattern (apply fixture → set scenario → poll logs → assert counters → restore) is fully established in scenarios 30-35.

**Primary recommendation:** Rename first, fix the gate second, new unit tests third, new E2E script fourth. Each step is individually verifiable.

---

## Standard Stack

This phase uses no new libraries. All changes are within the existing codebase.

### Existing Infrastructure Used

| Component | Location | Purpose |
|-----------|----------|---------|
| `SnapshotJob` | `src/SnmpCollector/Jobs/SnapshotJob.cs` | Contains `TierResult` enum and `EvaluateTenant` |
| `PipelineMetricService` | `src/SnmpCollector/Telemetry/PipelineMetricService.cs` | `snmp.command.suppressed` counter (PMET-15) |
| `SnapshotJobTests` | `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` | ~35 `TierResult` assertion references |
| `tenant-cfg03-two-diff-prio.yaml` | `tests/e2e/fixtures/tenant-cfg03-two-diff-prio.yaml` | P1/P2 equal suppression window fixture |
| `35-mts-02-advance-gate.sh` | `tests/e2e/scenarios/35-mts-02-advance-gate.sh` | Reference advance-gate E2E pattern |
| E2E lib (`sim.sh`, `prometheus.sh`) | `tests/e2e/lib/` | `poll_until_log`, `snapshot_counter`, `poll_until` |

---

## Architecture Patterns

### TierResult Enum: Current vs Target State

**Current (3 values):**
```csharp
internal enum TierResult { Violated, Healthy, Commanded }
```

**Target (3 values, renamed):**
```csharp
internal enum TierResult { Resolved, Healthy, Unresolved }
```

The semantic shift:
- `Violated` → `Resolved`: tier=2 stop when all Resolved metrics are violated (device condition confirmed). Advances gate.
- `Healthy` → `Healthy`: tier=3 stop, not all Evaluate violated. No change. Advances gate.
- `Commanded` → `Unresolved`: tier=4 intent, whether or not commands were actually enqueued. Blocks gate.

### The Bug: Line 207 of SnapshotJob.cs

```csharp
// CURRENT (BUG): suppressed count=0 → returns Violated → gate advances → P2 evaluated
return enqueueCount > 0 ? TierResult.Commanded : TierResult.Violated;

// FIX: tier=4 always means command intent = Unresolved, regardless of suppression
return TierResult.Unresolved;
```

The fix is a one-liner. Reaching tier=4 at all (stale OR all-evaluate-violated) means the device state is NOT confirmed — suppression is a rate-limit mechanism, not confirmation of resolution.

### Advance Gate: Current vs Target Logic

**Current advance gate (SnapshotJob.cs lines 89-100):**
```csharp
// Advance gate: block if ANY tenant is Commanded
var shouldAdvance = true;
for (var i = 0; i < results.Length; i++)
{
    if (results[i] == TierResult.Commanded)
    {
        shouldAdvance = false;
        break;
    }
}
```

**Target (after rename only):**
```csharp
// Block if ANY tenant is Unresolved
if (results[i] == TierResult.Unresolved)
```

The gate logic is structurally correct: block on the "command intent" enum value. Only the value name changes. No loop restructuring needed.

### Cycle Summary Logging: Updated Tracking

**Current** (lines 85-86):
```csharp
if (results[i] == TierResult.Commanded) totalCommanded++;
```

**Target**: Track `Unresolved` instead of `Commanded`. Log label should update to `Unresolved` (or `unresolved`) to match the new semantics.

The context also mentions: consider adding `Unresolved` count to the cycle summary log. The current log is:
```
"Snapshot cycle complete: {TenantsEvaluated} evaluated, {Commanded} commanded, {DurationMs:F1}ms"
```
This should change to track `totalUnresolved` with label `unresolved`.

### Unit Tests: Scope of `TierResult` Reference Changes

From source inspection, `SnapshotJobTests.cs` contains approximately 35 `TierResult` references:
- `TierResult.Commanded` appears ~6 times (renamed to `Unresolved`)
- `TierResult.Violated` appears ~14 times (renamed to `Resolved`)
- `TierResult.Healthy` appears ~5 times (unchanged)

The rename is mechanical. No test logic changes — only the enum value names change.

Two specific tests need semantic attention:

1. **`Execute_CommandSuppressed_NoTryWrite_IncrementSuppressed`** (line 741):
   - Currently asserts `TierResult.Violated` (the bug: suppressed = safe to cascade)
   - After fix: must assert `TierResult.Unresolved` (suppressed = still unresolved)
   - Comment currently says "zero enqueued → Violated (safe to cascade)" — must be corrected

2. **`Execute_ChannelFull_IncrementFailed_NoException`** (line 763):
   - Currently asserts `TierResult.Violated` (channel full = zero enqueued = cascade)
   - After fix: must assert `TierResult.Unresolved` (channel full means command intent was blocked, not resolved)
   - This is also semantically correct: a full channel means we couldn't send the command, device is still unresolved

**New unit tests to add** (2 cases to prove the fix directly):
1. `EvaluateTenant_AllCommandsSuppressed_ReturnsUnresolved` — all commands suppressed → `Unresolved`
2. `EvaluateTenant_ChannelFull_ReturnsUnresolved` — channel full → `Unresolved`

### E2E Simulation: Fixture and Script Design

**Fixture**: `tenant-cfg03-two-diff-prio.yaml` (confirmed content):
- P1: priority=1, `SuppressionWindowSeconds=10`, commands to `e2e_set_bypass`
- P2: priority=2, `SuppressionWindowSeconds=10`, same metrics/commands

**Simulator scenario**: `command_trigger` — sets `e2e_port_utilization=90` (>Max:80, violated), `e2e_channel_state=2` (>=Min:1.0, in-range), `e2e_bypass_status=2` (in-range). This means: Resolved metrics NOT all violated (tier=2 passes), Evaluate violated → tier=4.

**SnapshotJob interval**: The new scenario needs `IntervalSeconds=1` for observability. This requires a separate fixture variant that overrides the SnapshotJob config, OR the test relies on the production 15s interval and runs longer. However, the context says "SnapshotJob IntervalSeconds=1 for observable frequency." This should be a ConfigMap override applied in the scenario, similar to how tenant fixtures are applied/restored. Looking at existing scenarios, there is no pattern for overriding SnapshotJob config in E2E tests — the SnapshotJob `IntervalSeconds` is likely in `appsettings.json` or an environment variable.

**Key distinction from scenario 35 (MTS-02)**: Scenario 35 uses `tenant-cfg03-two-diff-prio-mts.yaml` with P1 having a 30s suppression window specifically so P2 can eventually pass the gate. The new Phase 59 scenario uses a 10s suppression window AND asserts that P2 is NEVER evaluated — the starvation proof.

**Script structure** (following established pattern from scenarios 30-35):
1. Save current ConfigMap snapshot
2. Apply `tenant-cfg03-two-diff-prio.yaml`
3. Wait for tenant vector reload
4. Set `command_trigger` scenario
5. Snapshot baseline counters (`snmp_command_sent_total`, `snmp_command_suppressed_total`)
6. **Assert Phase A (initial cycle)**: P1 reaches tier=4 (log poll), P1 sent counter increments
7. **Wait suppression window** (10s + margin): P1 commands should be suppressed in next cycle
8. **Assert Phase B (suppressed cycle)**: P1 reaches tier=4 again with suppressed log, `snmp_command_suppressed_total` increments for P1, sent counter does NOT increment further
9. **Assert starvation**: P2 tier log NEVER appears throughout the entire observation window
10. Restore ConfigMap, reset scenario

The key new assertion pattern (P2 never evaluated) can be implemented as a negative log assertion over the full observation window, similar to the `--since=15s` check in scenario 35b but extended to the full run duration.

### Prometheus Counter Labels: Key Findings

The `snmp_command_suppressed_total` counter uses `device_name` as the label key but is called with `tenant.Id` as the value in `SnapshotJob` (line 181: `_pipelineMetrics.IncrementCommandSuppressed(tenant.Id)`).

Prometheus query pattern for tenant-specific suppression:
```
sum(snmp_command_suppressed_total{device_name="e2e-tenant-P1"})
```

The `snmp_command_sent_total` counter uses `device_name` = actual device name from `CommandWorkerService` (line 194), NOT tenant ID. So `snmp_command_sent_total{device_name="E2E-SIM"}` is the correct query for sent commands.

This distinction matters for the E2E assertions:
- **Sent**: query by device name (`E2E-SIM`)
- **Suppressed**: query by tenant ID (`e2e-tenant-P1`)

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| Log-based assertions | Custom kubectl log parser | Existing `poll_until_log` in `sim.sh` |
| Counter assertions | Raw curl + jq | Existing `snapshot_counter` + `poll_until` in `prometheus.sh` |
| ConfigMap lifecycle | Manual kubectl apply/delete | Existing `save_configmap` / `restore_configmap` in `kubectl.sh` |
| Starvation proof | Time-based counter absence | Negative log assertion using `poll_until_log` failure path |

---

## Common Pitfalls

### Pitfall 1: Mechanical rename breaks semantic test intent for suppressed path
**What goes wrong:** Renaming `Violated` → `Resolved` in tests that assert `TierResult.Violated` for the suppressed-commands case will make those assertions pass after the rename, but the test will now assert the WRONG thing — it should assert `Unresolved` after the fix.
**Why it happens:** Two tests (`Execute_CommandSuppressed_*` and `Execute_ChannelFull_*`) currently assert `TierResult.Violated` because that's the CURRENT (buggy) behavior. After fix, they must assert `TierResult.Unresolved`.
**How to avoid:** Rename `Violated` → `Resolved` everywhere mechanically FIRST. Then separately fix the two "all suppressed" tests to assert `Unresolved`. Do not merge these two steps.
**Warning signs:** If both `Execute_CommandSuppressed_*` and `Execute_ChannelFull_*` still assert `Resolved` after the fix, the fix wasn't applied.

### Pitfall 2: Scenario 35 (MTS-02) breaks after advance gate fix
**What goes wrong:** Scenario 35 specifically tests that P1 returns `ConfirmedBad` (now `Resolved`) when all commands are suppressed, allowing P2 to pass the gate. If the fix makes suppressed-all return `Unresolved`, P2 will never pass in scenario 35.
**Why it happens:** The fix changes the gate semantics in a way that contradicts MTS-02B's design.
**How to avoid:** Review scenario 35 carefully. Scenario 35 currently uses the buggy behavior intentionally — it RELIES on suppressed-all returning `Violated` (which allows gate pass). After the fix, scenario 35B will fail because P2 will never be evaluated. The scenario 35 test must be updated to reflect correct semantics: P2 should only pass when P1 is truly `Resolved` (Resolved = all Resolved metrics violated, tier=2 stop). This is a fundamental behavioral change.
**Resolution**: Scenario 35 MTS-02B may need to be redesigned or its assertions adjusted to match the new semantics. MTS-02B was valid under old semantics but incorrect under corrected semantics. The new Phase 59 E2E scenario proves the correct P2-never-evaluated behavior.

### Pitfall 3: SnapshotJob IntervalSeconds=1 requires a config override mechanism
**What goes wrong:** The simulation requires 1s interval for observable starvation, but the default is 15s. There is no established E2E pattern for overriding SnapshotJob config dynamically.
**Why it happens:** SnapshotJob options come from appsettings or environment, not from a ConfigMap.
**How to avoid:** Investigate whether `SnapshotJobOptions` can be overridden via ConfigMap or environment. If not, the E2E scenario may need to rely on the 15s production interval (extending observation windows) OR the simulation design should be rethought to work at 15s cadence. Alternatively, a separate deployment config (e.g., a Quartz-schedule override) may be needed.
**Note**: The context specifies `IntervalSeconds=1` — this needs to be reachable from the E2E environment. The planner should clarify how this override is applied.

### Pitfall 4: P2 starvation assertion is time-dependent without a hard boundary
**What goes wrong:** Asserting "P2 was never evaluated" during a bounded time window is inherently fragile if the window is too short (P1 might not have fired yet) or too long (test suite runtime grows).
**Why it happens:** The suppression window is 10s; with 1s interval, each cycle evaluates P1 and immediately blocks P2.
**How to avoid:** The observation window should span at least: (3 poll intervals to fill TimeSeriesSize=3) + (1 suppression window=10s) + margin. With 1s interval this is ~15s. With 15s interval this is ~60-75s. The script should log the window duration explicitly.

### Pitfall 5: `snmp_command_suppressed_total` uses tenant ID as `device_name` label
**What goes wrong:** Querying `snmp_command_suppressed_total{device_name="E2E-SIM"}` will return 0 because suppressed commands are tagged with `tenant.Id`, not device name.
**Why it happens:** `_pipelineMetrics.IncrementCommandSuppressed(tenant.Id)` passes tenant ID as the `deviceName` parameter.
**How to avoid:** Use `device_name="e2e-tenant-P1"` for suppressed counter queries, and `device_name="E2E-SIM"` for sent counter queries. This asymmetry exists in the current codebase and should be preserved as-is.

---

## Code Examples

### Correct enum rename in SnapshotJob.cs

```csharp
// Source: src/SnmpCollector/Jobs/SnapshotJob.cs
internal enum TierResult { Resolved, Healthy, Unresolved }

// Tier 2 stop (all Resolved metrics violated = device confirmed)
return TierResult.Resolved;

// Tier 3 stop (not all Evaluate violated = healthy)
return TierResult.Healthy;

// Tier 4 (command intent = device not confirmed, regardless of suppression count)
return TierResult.Unresolved;   // was: enqueueCount > 0 ? Commanded : Violated
```

### Advance gate update

```csharp
// Source: src/SnmpCollector/Jobs/SnapshotJob.cs
// Only rename needed — logic is unchanged
if (results[i] == TierResult.Unresolved)  // was: Commanded
{
    shouldAdvance = false;
    break;
}
```

### Cycle summary tracking update

```csharp
// Source: src/SnmpCollector/Jobs/SnapshotJob.cs
var totalUnresolved = 0;   // was: totalCommanded

// in loop:
if (results[i] == TierResult.Unresolved) totalUnresolved++;   // was: Commanded

// log:
_logger.LogDebug(
    "Snapshot cycle complete: {TenantsEvaluated} evaluated, {Unresolved} unresolved, {DurationMs:F1}ms",
    totalEvaluated, totalUnresolved, sw.Elapsed.TotalMilliseconds);
```

### E2E starvation proof structure (scenario 40 candidate)

```bash
# Pattern from 35-mts-02-advance-gate.sh, adapted for starvation
BEFORE_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
BEFORE_SUPP=$(snapshot_counter "snmp_command_suppressed_total" 'device_name="e2e-tenant-P1"')

# Phase A: P1 reaches tier=4 and sends first command
if poll_until_log 90 1 "e2e-tenant-P1.*tier=4 — commands" 30; then
    record_pass "MTS-03A: P1 tier=4 detected" "log=tier4_P1_found"
fi

# Phase B: P1 suppressed in subsequent cycle (10s suppression window)
if poll_until_log 60 1 "Command.*suppressed.*e2e-tenant-P1\|tier=4.*e2e-tenant-P1" 20; then
    # ... check suppressed counter increment
fi

# Starvation assertion: P2 NEVER appears in tier logs
P2_TIER_FOUND=0
for POD in $PODS; do
    P2_LOGS=$(kubectl logs "$POD" -n simetra --since=120s 2>/dev/null \
        | grep "e2e-tenant-P2.*tier=" || echo "") || true
    if [ -n "$P2_LOGS" ]; then P2_TIER_FOUND=1; break; fi
done
if [ "$P2_TIER_FOUND" -eq 0 ]; then
    record_pass "MTS-03: P2 never evaluated (starvation proven)" "P2 tier log absent in 120s window"
fi
```

---

## State of the Art

| Old Behavior | New Behavior | When Changed | Impact |
|--------------|-------------|--------------|--------|
| `TierResult.ConfirmedBad` | `TierResult.Violated` (then → `Resolved`) | Quick-076 | Rename 1 of 2 |
| `TierResult.Stale` removed | Staleness falls through to tier=4 | Quick-076 | Staleness always → commands |
| Single-tenant: `Task.Run` | Direct `EvaluateTenant` on Quartz thread | Quick-077 | No latency outliers |
| Suppressed-all → `Violated` (gate passes) | Suppressed-all → `Unresolved` (gate blocks) | **Phase 59** | THE BUG FIX |

---

## Open Questions

1. **How is `SnapshotJobOptions.IntervalSeconds=1` applied in the E2E cluster?**
   - What we know: `SnapshotJobOptions` is bound from `"SnapshotJob"` config section; default is 15s; range is `[1, 300]`
   - What's unclear: Is there a ConfigMap for SnapshotJob options? Can it be overridden per-test? Is it an environment variable in the Deployment YAML?
   - Recommendation: Planner should check the Deployment YAML and appsettings files for how `SnapshotJob:IntervalSeconds` is configured, and whether a ConfigMap override or env var override exists for E2E. If not, the simulation may need to work at the default 15s interval with extended observation windows.

2. **Should scenario 35 (MTS-02) be updated or left as-is?**
   - What we know: MTS-02B currently asserts P2 is evaluated when P1's commands are suppressed (returning old `Violated`). After the fix, this assertion will fail because suppressed → `Unresolved` blocks the gate.
   - What's unclear: Is MTS-02B intentionally testing the (now-wrong) behavior, or should it be redesigned?
   - Recommendation: MTS-02B should be rewritten to test the correct behavior: P2 should only pass when P1 is truly `Resolved` (tier=2 stop, all Resolved metrics violated). This requires changing the scenario from `command_trigger` to a scenario where P1's Resolved metrics are all violated.

3. **Should `snmp_command_suppressed_total` add a `tenant_id` label (separate from `device_name`)?**
   - What we know: Currently the label is `device_name` but the value is the tenant ID — a semantic mismatch that exists as a quirk.
   - What's unclear: Phase 59 success criterion 4 says "Command sent and suppressed counters are observable per tenant in Prometheus" — this implies tenant-level observability.
   - Recommendation: The current tag (`device_name=tenant.Id`) is functional but semantically odd. If a `tenant_id` tag is added, it's a breaking change for any existing Prometheus alerts. The planner should decide: rename the tag key (breaking) vs accept the current quirk vs add a second tag. This is under "Claude's Discretion."

---

## Sources

### Primary (HIGH confidence)

All findings from direct source code inspection:

- `src/SnmpCollector/Jobs/SnapshotJob.cs` — TierResult enum, EvaluateTenant, advance gate logic, cycle summary log
- `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` — all TierResult assertion patterns (~35 references)
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — PMET-13/14/15 counter definitions and label patterns
- `tests/e2e/scenarios/35-mts-02-advance-gate.sh` — reference advance-gate E2E script pattern
- `tests/e2e/scenarios/30-sts-02-evaluate-violated.sh` — reference single-tenant command E2E pattern
- `tests/e2e/fixtures/tenant-cfg03-two-diff-prio.yaml` — confirmed P1/P2 fixture content (equal 10s suppression)
- `tests/e2e/fixtures/tenant-cfg03-two-diff-prio-mts.yaml` — P1 30s / P2 10s suppression MTS fixture
- `tests/e2e/lib/sim.sh` — `poll_until_log`, `sim_set_scenario`, `reset_scenario`
- `simulators/e2e-sim/e2e_simulator.py` — `command_trigger` scenario definition confirmed
- `.planning/quick/076-snapshot-tier-fixes/076-SUMMARY.md` — prior rename history
- `.planning/quick/077-snapshot-direct-eval-single-tenant/077-SUMMARY.md` — single-tenant direct eval background

---

## Metadata

**Confidence breakdown:**
- Bug identification and fix: HIGH — single line, confirmed by reading current code
- TierResult rename scope: HIGH — grep confirmed all locations in both source and tests
- Unit test changes: HIGH — two tests need semantic correction beyond mechanical rename
- E2E script structure: HIGH — established pattern in scenarios 30-35
- IntervalSeconds=1 mechanism: LOW — not found in any E2E config; needs investigation by planner

**Research date:** 2026-03-19
**Valid until:** 2026-04-19 (stable codebase, no fast-moving dependencies)
