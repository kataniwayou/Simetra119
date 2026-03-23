# Phase 77: Gather-Then-Decide Evaluation Flow - Research

**Researched:** 2026-03-23
**Domain:** SnapshotJob EvaluateTenant refactor — gather tier results before deciding state, percentage API migration
**Confidence:** HIGH

---

## Summary

Phase 76 replaced the 6 `Increment*` counter methods on `ITenantMetricService` with 6 `RecordXxxPercent` gauge methods. `SnapshotJob.cs` still calls the removed `Increment*` methods, breaking compilation. Phase 77 fixes those callers by refactoring `EvaluateTenant` to (1) gather all tier counts up front before deciding state, (2) compute percentages from gathered counts, and (3) call all 6 `RecordXxxPercent` gauges at a single exit point.

The current `EvaluateTenant` has three early-return paths that each call subsets of the old counter API mid-flow (Resolved path, Healthy path, Tier 4 path). The new flow collapses these to a single recording point, with one exception: the `NotReady` early return is preserved and records only state + duration (no percentages).

The existing unit tests in `SnapshotJobTests.cs` reference 9 methods that no longer exist on `ITenantMetricService` (`IncrementTier1Stale`, `IncrementTier2Resolved`, `IncrementTier3Evaluate`, `IncrementCommandDispatched`, `IncrementCommandSuppressed`, `IncrementCommandFailed` — called via NSubstitute `Received()`/`DidNotReceive()`). These tests must be rewritten to assert the new `RecordXxxPercent` signatures with computed percent values.

**Primary recommendation:** Replace every `Increment*` call site with percentage computation from gathered counts. The gather-then-decide structure directly parallels the logic already proven in the old per-path counter calls — it is a structural refactor, not a logic change.

---

## Current EvaluateTenant Structure (Confirmed, HIGH)

### Flow with early return points

```
EvaluateTenant(Tenant tenant)
│
├─ Pre-tier: AreAllReady → false → RecordAndReturn(NotReady) [EARLY RETURN — keeps in Phase 77]
│
├─ Tier 1: HasStaleness → true → fall-through to Tier 4 (skip Tier 2 & 3)
│
├─ else branch (not stale):
│   ├─ Tier 2: AreAllResolvedViolated → true → [counter calls] → RecordAndReturn(Resolved) [EARLY RETURN — REMOVED]
│   ├─ Tier 3: !AreAllEvaluateViolated → true → [counter calls] → RecordAndReturn(Healthy) [EARLY RETURN — REMOVED]
│   └─ else (all evaluate violated) → fall-through to Tier 4
│
└─ Tier 4: command dispatch loop → [counter calls] → RecordAndReturn(Unresolved)
```

### Existing Increment* call sites in EvaluateTenant (all must be removed)

| Location | Calls | Notes |
|----------|-------|-------|
| Tier 2 early return (line 162-169) | `IncrementTier1Stale` × staleCount, `IncrementTier2Resolved` × resolvedCount | Removed — was partial recording |
| Tier 3 early return (line 184-194) | `IncrementTier1Stale` × staleCount, `IncrementTier2Resolved` × resolvedCount, `IncrementTier3Evaluate` × evaluateCount | Removed — was partial recording |
| Tier 4 exit (line 244-258) | All 6 Increment* | Removed — becomes the new single exit |

### Existing count helpers (all static, keep as-is)

| Method | What it counts | Used as numerator for |
|--------|---------------|----------------------|
| `CountStaleHolders` | Non-excluded holders with null slot or expired grace | stale percent |
| `CountResolvedNonViolated` | Resolved holders where at least one sample is in-range | CAUTION: see below |
| `CountEvaluateViolated` | Evaluate holders where ALL checked samples violated | evaluate percent |

**CAUTION on resolved direction:** Phase 76 CONTEXT.md states "Numerator = violated resolved holders (higher % = worse)." The existing `CountResolvedNonViolated` counts non-violated holders. The new numerator is violated resolved holders, meaning the numerator is `(totalResolved - CountResolvedNonViolated)` — or a new `CountResolvedViolated` helper is needed.

### RecordAndReturn helper (unchanged)

```csharp
// src/SnmpCollector/Jobs/SnapshotJob.cs line 265-270
private TenantState RecordAndReturn(Tenant tenant, TenantState state, Stopwatch sw)
{
    _tenantMetrics.RecordTenantState(tenant.Id, tenant.Priority, state);
    _tenantMetrics.RecordEvaluationDuration(tenant.Id, tenant.Priority, sw.Elapsed.TotalMilliseconds);
    return state;
}
```

This helper stays unchanged — it records state + duration, which is correct for all exit points.

---

## New ITenantMetricService API (Phase 76, HIGH)

```csharp
// src/SnmpCollector/Telemetry/ITenantMetricService.cs
void RecordMetricStalePercent(string tenantId, int priority, double percent);
void RecordMetricResolvedPercent(string tenantId, int priority, double percent);   // higher = more violated
void RecordMetricEvaluatePercent(string tenantId, int priority, double percent);
void RecordCommandDispatchedPercent(string tenantId, int priority, double percent);
void RecordCommandFailedPercent(string tenantId, int priority, double percent);
void RecordCommandSuppressedPercent(string tenantId, int priority, double percent);
void RecordTenantState(string tenantId, int priority, TenantState state);
void RecordEvaluationDuration(string tenantId, int priority, double durationMs);
```

No `Increment*` methods exist. Any call to them causes a compile error — the current break.

---

## Percentage Denominators (HIGH)

The percentages are `(numerator / denominator) * 100.0`. Denominators come from counting holders in `tenant.Holders`.

### Metric gauges

| Gauge | Numerator | Denominator | Count helper needed |
|-------|-----------|-------------|---------------------|
| `stale%` | `CountStaleHolders` | Total staleness-eligible holders (non-Trap, non-Command, IntervalSeconds > 0) | New: `CountStalenessEligibleHolders` |
| `resolved%` | Resolved holders that ARE violated | Total Resolved holders with participating data (non-empty series / non-null slot) | New: `CountResolvedViolated` (or reuse existing) |
| `evaluate%` | `CountEvaluateViolated` | Total Evaluate holders with participating data | New: `CountEvaluateEligibleHolders` |

### Command gauges

| Gauge | Numerator | Denominator |
|-------|-----------|-------------|
| `dispatched%` | `dispatchedCount` | `tenant.Commands.Count` |
| `failed%` | `failedCount` | `tenant.Commands.Count` |
| `suppressed%` | `suppressedCount` | `tenant.Commands.Count` |

Note: `tenant.Commands` is `IReadOnlyList<CommandSlotOptions>`. Count is directly available.

### Zero denominator policy (HIGH, from Phase 76 CONTEXT.md)

> Record 0.0 — tenant validation prevents this case in practice.

Callers must guard: `denominator == 0 ? 0.0 : (numerator / denominator) * 100.0`

---

## New EvaluateTenant Structure (Locked Decisions)

```
EvaluateTenant(Tenant tenant)
│
├─ Pre-tier: !AreAllReady → RecordAndReturn(NotReady, state+duration only, NO gauges) [ONLY early return]
│
├─ GATHER PHASE:
│   ├─ Tier 1: Compute staleCount, staleTotal
│   ├─ Tier 2: if !stale → Compute resolvedViolatedCount, resolvedTotal
│   │          if stale → resolvedViolatedCount = 0, resolvedTotal = (anything > 0 or skip)
│   ├─ Tier 3: if !stale → Compute evaluateViolatedCount, evaluateTotal
│   │          if stale → evaluateViolatedCount = 0, evaluateTotal = (anything > 0 or skip)
│   └─ Tier 4: Command dispatch loop → accumulate dispatchedCount, suppressedCount, failedCount
│
├─ DECIDE PHASE (same priority order as v2.4):
│   ├─ if stale → state = Unresolved
│   ├─ else if AreAllResolvedViolated → state = Resolved
│   ├─ else if AreAllEvaluateViolated → state = Unresolved
│   └─ else → state = Healthy
│
├─ COMPUTE PERCENTAGES:
│   ├─ stalePercent, resolvedPercent, evaluatePercent
│   └─ dispatchedPercent, failedPercent, suppressedPercent
│
└─ SINGLE EXIT: RecordMetricStalePercent, RecordMetricResolvedPercent, RecordMetricEvaluatePercent,
                RecordCommandDispatchedPercent, RecordCommandFailedPercent, RecordCommandSuppressedPercent,
                RecordAndReturn(state, state+duration)
```

**Stale path specifics (from CONTEXT.md):**
- Gather stale% normally
- Skip resolved/evaluate gathering — record those as 0% (stale data unreliable)
- Command dispatch still runs (stale → Unresolved → commands)
- Command percentages recorded normally

**NotReady path specifics (from CONTEXT.md):**
- Return early immediately after pre-tier check
- Call `RecordAndReturn` (state + duration only)
- Do NOT call any of the 6 `RecordXxxPercent` methods

---

## Architecture Patterns

### Recommended EvaluateTenant skeleton

```csharp
internal TenantState EvaluateTenant(Tenant tenant)
{
    var sw = Stopwatch.StartNew();

    // Pre-tier: NotReady — only exception to gather-then-decide
    if (!AreAllReady(tenant.Holders))
    {
        _logger.LogDebug("...");
        return RecordAndReturn(tenant, TenantState.NotReady, sw);
    }

    // --- GATHER ---
    var staleCount = CountStaleHolders(tenant.Holders);
    var staleTotal = CountStalenessEligibleHolders(tenant.Holders);
    var isStale = staleCount > 0;  // reuse existing HasStaleness logic via count

    int resolvedViolatedCount, resolvedTotal, evaluateViolatedCount, evaluateTotal;

    if (isStale)
    {
        // Stale: skip resolved/evaluate — unreliable on stale data
        resolvedViolatedCount = 0;
        resolvedTotal = 1; // avoid div/0; percent will be 0.0
        evaluateViolatedCount = 0;
        evaluateTotal = 1;
    }
    else
    {
        resolvedViolatedCount = CountResolvedViolated(tenant.Holders);
        resolvedTotal = CountResolvedParticipating(tenant.Holders);
        evaluateViolatedCount = CountEvaluateViolated(tenant.Holders);
        evaluateTotal = CountEvaluateParticipating(tenant.Holders);
    }

    // Tier 4: command dispatch (must run before state determination for command counts)
    var dispatchedCount = 0;
    var suppressedCount = 0;
    var failedCount = 0;
    // ... dispatch loop (unchanged) ...

    // --- DECIDE ---
    TenantState state;
    if (isStale)
        state = TenantState.Unresolved;
    else if (AreAllResolvedViolated(tenant.Holders))  // OR derive from counts
        state = TenantState.Resolved;
    else if (AreAllEvaluateViolated(tenant.Holders))  // OR derive from counts
        state = TenantState.Unresolved;
    else
        state = TenantState.Healthy;

    // --- COMPUTE PERCENTAGES ---
    var stalePercent    = staleTotal == 0 ? 0.0 : staleCount * 100.0 / staleTotal;
    var resolvedPercent = resolvedTotal == 0 ? 0.0 : resolvedViolatedCount * 100.0 / resolvedTotal;
    var evaluatePercent = evaluateTotal == 0 ? 0.0 : evaluateViolatedCount * 100.0 / evaluateTotal;
    var cmdTotal        = tenant.Commands.Count;
    var dispatchedPct   = cmdTotal == 0 ? 0.0 : dispatchedCount * 100.0 / cmdTotal;
    var failedPct       = cmdTotal == 0 ? 0.0 : failedCount * 100.0 / cmdTotal;
    var suppressedPct   = cmdTotal == 0 ? 0.0 : suppressedCount * 100.0 / cmdTotal;

    // --- SINGLE EXIT: record all 6 gauges + state + duration ---
    _tenantMetrics.RecordMetricStalePercent(tenant.Id, tenant.Priority, stalePercent);
    _tenantMetrics.RecordMetricResolvedPercent(tenant.Id, tenant.Priority, resolvedPercent);
    _tenantMetrics.RecordMetricEvaluatePercent(tenant.Id, tenant.Priority, evaluatePercent);
    _tenantMetrics.RecordCommandDispatchedPercent(tenant.Id, tenant.Priority, dispatchedPct);
    _tenantMetrics.RecordCommandFailedPercent(tenant.Id, tenant.Priority, failedPct);
    _tenantMetrics.RecordCommandSuppressedPercent(tenant.Id, tenant.Priority, suppressedPct);
    return RecordAndReturn(tenant, state, sw);
}
```

**Note on derive-from-counts vs re-calling the bool helpers:** The state determination after the gather phase can either re-call the existing `AreAllResolvedViolated` / `AreAllEvaluateViolated` bool methods (same logic, just called twice) or derive state directly from the gathered counts. Both are correct. Re-calling the existing bool methods avoids duplicating the counting logic and keeps `AreAllXxx` as the single truth for state decisions.

### New count helpers required

The following static helper methods do not currently exist and must be added:

```csharp
/// Counts holders eligible for staleness check (excludes Trap, Command, IntervalSeconds=0).
private static int CountStalenessEligibleHolders(IReadOnlyList<MetricSlotHolder> holders)

/// Counts Resolved holders that ARE violated (numerator for resolved%).
/// For Trap/Command: newest sample violated. For Poll/Synthetic: all samples violated.
/// Empty series = skip.
private static int CountResolvedViolated(IReadOnlyList<MetricSlotHolder> holders)

/// Counts Resolved holders with at least one participating sample.
/// (the denominator for resolved%)
private static int CountResolvedParticipating(IReadOnlyList<MetricSlotHolder> holders)

/// Counts Evaluate holders with at least one participating sample.
/// (the denominator for evaluate%)
private static int CountEvaluateParticipating(IReadOnlyList<MetricSlotHolder> holders)
```

**Alternative:** If denominator helpers are too many, a single `CountHoldersByRole` helper returning (violatedCount, totalParticipating) per role could work. The planner may choose either approach.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| State determination | Custom state machine | Re-use the existing `AreAllResolvedViolated`/`AreAllEvaluateViolated`/`HasStaleness` helpers — proven, tested |
| Division by zero | Custom safe-divide utility | Inline ternary `denominator == 0 ? 0.0 : ...` — Per Phase 76 decision, record 0.0 |
| Percentage scaling | Custom math | Simple `count * 100.0 / total` — no rounding, no clamping needed |

---

## Common Pitfalls

### Pitfall 1: Resolved direction inverted

**What goes wrong:** Using `CountResolvedNonViolated` as the numerator for `RecordMetricResolvedPercent`.
**Why it happens:** The old counter was named `IncrementTier2Resolved` and counted non-violated holders. Phase 76 changed direction to "higher = worse" (violated), matching evaluate direction.
**How to avoid:** Numerator for resolved% = violated resolved holders = `totalResolved - CountResolvedNonViolated` or a new `CountResolvedViolated` helper.
**Warning sign:** 100% resolved% when all resolved metrics are healthy.

### Pitfall 2: Still calling Increment* somewhere

**What goes wrong:** Compiler error remains because one call site was missed.
**Why it happens:** Three separate early returns each had their own counter calls in the old code. Easy to miss one.
**How to avoid:** After refactor, grep for `Increment` in SnapshotJob.cs — result must be zero.

### Pitfall 3: Command dispatch skipped on non-stale paths

**What goes wrong:** Moving command dispatch inside the stale branch only, so non-stale Unresolved tenants never enqueue commands.
**Why it happens:** Misreading the flow — Tier 4 runs whenever state ends up as Unresolved (both stale and all-evaluate-violated paths).
**How to avoid:** Command dispatch runs unconditionally after the gather phase (before state determination) OR conditionally on the final state being Unresolved. The simplest: run dispatch when `isStale || AreAllEvaluateViolated`.

### Pitfall 4: Test assertions use wrong percent values

**What goes wrong:** Tests assert `RecordMetricResolvedPercent(_, _, Arg.Any<double>())` and pass even with wrong values.
**Why it happens:** Using `Arg.Any<double>()` avoids precise assertions.
**How to avoid:** For path-specific tests, assert the exact computed value (e.g., 100.0 for "all violated", 0.0 for stale path). For zero-denominator cases, assert 0.0 explicitly.

### Pitfall 5: Calling 6 gauges on NotReady path

**What goes wrong:** Recording stale/resolved/evaluate as 0.0 even for NotReady tenants.
**Why it happens:** Trying to "complete" the single-exit pattern for NotReady too.
**How to avoid:** NotReady remains an explicit early return with `RecordAndReturn` only — no gauge calls. This is the locked decision in CONTEXT.md.

---

## Code Examples

### Pattern: verified call signature for the new API

```csharp
// Source: src/SnmpCollector/Telemetry/ITenantMetricService.cs (Phase 76 output)
_tenantMetrics.RecordMetricStalePercent(tenant.Id, tenant.Priority, stalePercent);
_tenantMetrics.RecordMetricResolvedPercent(tenant.Id, tenant.Priority, resolvedPercent);
_tenantMetrics.RecordMetricEvaluatePercent(tenant.Id, tenant.Priority, evaluatePercent);
_tenantMetrics.RecordCommandDispatchedPercent(tenant.Id, tenant.Priority, dispatchedPct);
_tenantMetrics.RecordCommandFailedPercent(tenant.Id, tenant.Priority, failedPct);
_tenantMetrics.RecordCommandSuppressedPercent(tenant.Id, tenant.Priority, suppressedPct);
```

### Pattern: NSubstitute assertions for new percent API in tests

```csharp
// Exact value assertion (preferred for path-specific tests)
_tenantMetrics.Received(1).RecordMetricStalePercent(tenant.Id, tenant.Priority, 0.0);
_tenantMetrics.Received(1).RecordMetricResolvedPercent(tenant.Id, tenant.Priority, 100.0);

// Any value (acceptable when exact percent is not the focus)
_tenantMetrics.Received(1).RecordCommandDispatchedPercent(
    tenant.Id, tenant.Priority, Arg.Any<double>());

// DidNotReceive (NotReady path — no percent gauges)
_tenantMetrics.DidNotReceive().RecordMetricStalePercent(
    Arg.Any<string>(), Arg.Any<int>(), Arg.Any<double>());
```

### Pattern: MakeHolder helper (unchanged, for reference)

```csharp
// tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs line 1307-1324
private static MetricSlotHolder MakeHolder(
    int intervalSeconds = 3600,
    double graceMultiplier = 2.0,
    string role = "Resolved",
    ThresholdOptions? threshold = null,
    string metricName = "test-metric",
    int timeSeriesSize = 1)
// → new MetricSlotHolder(ip, port, metricName, intervalSeconds, role, timeSeriesSize, graceMultiplier, threshold)
```

---

## Existing Test Structure (HIGH)

### Test class setup
- `ITenantMetricService _tenantMetrics = Substitute.For<ITenantMetricService>()` — NSubstitute mock
- `EvaluateTenant` is called directly (not via `Execute`) for unit tests
- `_tenantMetrics.ClearReceivedCalls()` called at start of metric-assertion tests

### Tests that must change (reference removed Increment* methods)

| Test | Old assertions | New assertions |
|------|---------------|----------------|
| `EvaluateTenant_NotReadyPath_RecordsOnlyStateAndDuration` | 6× `DidNotReceive().IncrementXxx` | 6× `DidNotReceive().RecordXxxPercent` |
| `EvaluateTenant_ResolvedPath_RecordsStateAndDurationAndTier1AndTier2` | `DidNotReceive().IncrementTier1Stale`, etc. | `Received(1).RecordMetricResolvedPercent(_, _, 100.0)` etc. |
| `EvaluateTenant_HealthyPath_RecordsAllTierCountersAndStateAndDuration` | `Received(1).IncrementTier2Resolved` | `Received(1).RecordMetricResolvedPercent(_, _, 0.0)` etc. |
| `EvaluateTenant_UnresolvedPath_RecordsAllTierCountersPlusCommandCounters` | `Received(1).IncrementTier2Resolved`, etc. | `RecordMetricResolvedPercent(_, _, 0.0)`, `RecordMetricEvaluatePercent(_, _, 100.0)`, `RecordCommandDispatchedPercent(_, _, 100.0)` |
| `EvaluateTenant_StaleHolderCount_IncrementsByActualCount` | `Received(2).IncrementTier1Stale` | `Received(1).RecordMetricStalePercent(_, _, [computed])` |

### Tests that remain valid (no Increment* references)
All state-result and command-channel tests (roughly 30 tests) do not reference `ITenantMetricService` methods and require no changes to their assertions. The only change is that the stale path, resolved path, and healthy path no longer have intermediate returns — so the test setup remains identical and the `Assert.Equal(state, result)` assertions are unchanged.

### New tests to add

| New test | Purpose |
|----------|---------|
| `EvaluateTenant_StalePath_RecordsNonZeroStalePercentAndZeroResolvedAndEvaluate` | Verifies stale path records stale% > 0, resolved% = 0, evaluate% = 0 |
| `EvaluateTenant_AllMetricsHealthy_RecordsZeroPercentForAllMetricGauges` | Healthy path records 0% for all 3 metric gauges |
| `EvaluateTenant_CommandsDispatched_RecordsCorrectDispatchedPercent` | 1 of 2 commands dispatched → 50% |

---

## Open Questions

1. **Derive state from counts vs re-call bool helpers**
   - What we know: both produce the same result; gathered counts contain all needed information
   - What's unclear: whether the planner prefers to eliminate the bool helper calls (DRY) or keep them (clarity)
   - Recommendation: re-call the existing bool helpers for the decide phase — avoids duplicating the participation/vacuous-true logic that's already correct

2. **Denominator helpers: new methods vs inline counting**
   - What we know: 4 new count helpers are needed (staleness-eligible, resolved-violated, resolved-participating, evaluate-participating)
   - What's unclear: whether the planner wants all 4 as separate static methods or combined
   - Recommendation: separate static methods, consistent with the existing CountXxx pattern

3. **Stale denominator when no staleness-eligible holders**
   - What we know: zero denominator → record 0.0 (Phase 76 policy)
   - What's unclear: whether 0.0 stale% on a "all excluded" tenant is misleading vs correct
   - Recommendation: follow Phase 76 policy (0.0), tenant validation prevents zero-eligible tenants in practice

---

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Jobs/SnapshotJob.cs` — read directly; all call sites, all count helpers, full flow
- `src/SnmpCollector/Telemetry/ITenantMetricService.cs` — read directly; complete new API
- `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` — read directly; full test structure and all helper methods
- `src/SnmpCollector/Pipeline/Tenant.cs` — read directly; `Commands`, `Holders`, `Id`, `Priority`, `SuppressionWindowSeconds`
- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — read directly; `Role`, `Source`, `IntervalSeconds`, `IsReady`, `ReadSlot`, `ReadSeries`
- `.planning/phases/76-percentage-gauge-instruments/76-CONTEXT.md` — resolved direction, zero denominator policy
- `.planning/phases/76-percentage-gauge-instruments/76-01-SUMMARY.md` — confirms Phase 77 is the intended caller fixer; compile break is expected

---

## Metadata

**Confidence breakdown:**
- Current EvaluateTenant structure: HIGH — read source directly
- New ITenantMetricService API: HIGH — read source directly
- Denominator logic: HIGH — derived directly from count helpers + Phase 76 decisions
- Test structure: HIGH — read full test file
- New count helper names/signatures: MEDIUM — derived from pattern, exact names are planner's discretion

**Research date:** 2026-03-23
**Valid until:** 2026-04-23 (stable internal codebase, no external dependencies)
