# Phase 60: Readiness Window for Holders - Research

**Researched:** 2026-03-19
**Domain:** C# in-process pipeline — MetricSlotHolder sentinel removal, time-based readiness gating, SnapshotJob staleness tiers
**Confidence:** HIGH (all findings from direct codebase inspection)

## Summary

Phase 60 is a pure internal refactor of two files (`MetricSlotHolder.cs`, `SnapshotJob.cs`) plus their unit-test counterparts, with an e2e scenario for the eliminated MTS-03 startup race. There are no new library dependencies; no NuGet packages are added. The work is well-scoped: remove seven lines from the constructor, add one property (`ConstructedAt`), add a readiness check in `EvaluateTenant`, and update all tests that relied on sentinel behavior.

The sentinel in `MetricSlotHolder` currently serves two purposes: (1) it keeps `ReadSlot()` non-null before any real write, so `HasStaleness` doesn't skip the holder as "null = not yet judged"; and (2) it provides a timestamp anchor for the staleness clock. Both purposes are made obsolete by the readiness grace window: the holder is invisible to evaluation until `ConstructedAt + ReadinessGrace` has elapsed, after which a null `ReadSlot()` is treated as stale (device never responded). Post-grace, the existing three-tier logic in `HasStaleness`/`AreAllResolvedViolated`/`AreAllEvaluateViolated` continues unchanged — those methods already handle `series.Length == 0` with `continue` (skip) semantics.

The MTS-03 e2e scenario (40-mts-03-starvation-proof.sh) currently tests priority starvation under sustained commands. After Phase 60 it also needs to survive the startup grace window: the test must wait for holders to become ready before asserting tier=4 behavior. The STS-05 priming step (sleep 20 to populate timestamps) becomes unnecessary under the new model because the grace window prevents evaluation until real data can arrive — but the priming step does no harm and may be left as-is or simplified.

**Primary recommendation:** Implement readiness as a pre-tier check in `EvaluateTenant` (before Tier 1), returning `TierResult.Unresolved` for not-ready tenants. This keeps `HasStaleness` focused purely on staleness and avoids complicating it with a readiness concept.

## Standard Stack

This phase uses no new libraries. The existing in-process stack is:

### Core (existing, no changes)
| Component | Role in Phase 60 |
|-----------|-----------------|
| `System.Collections.Immutable` | `ImmutableArray<MetricSlot>` — already used, no change |
| `System.Threading` (`Volatile`) | Volatile.Read/Write for thread-safe `_box` swap — no change |
| `DateTimeOffset.UtcNow` | Used for `ConstructedAt` (same pattern as existing `MetricSlot` timestamps) |
| `xUnit` | Unit test framework — no change |
| Bash e2e scripts | E2E scenario harness — no change to harness, one scenario updated |

**Installation:** None required.

## Architecture Patterns

### Recommended Project Structure (unchanged)
```
src/SnmpCollector/
├── Pipeline/
│   └── MetricSlotHolder.cs       # PRIMARY CHANGE: remove sentinel, add ConstructedAt
├── Jobs/
│   └── SnapshotJob.cs            # PRIMARY CHANGE: add readiness pre-check in EvaluateTenant
tests/SnmpCollector.Tests/
├── Pipeline/
│   └── MetricSlotHolderTests.cs  # UPDATE: sentinel-based tests → readiness-based tests
├── Jobs/
│   └── SnapshotJobTests.cs       # UPDATE: large sentinel surface area, add readiness tests
tests/e2e/scenarios/
└── 40-mts-03-starvation-proof.sh # UPDATE: add grace window wait before tier=4 assertion
```

### Pattern 1: ConstructedAt — Immutable Construction Timestamp

**What:** Add a single `public DateTimeOffset ConstructedAt { get; }` property set to `DateTimeOffset.UtcNow` in the constructor. The sentinel creation lines are removed. `_box` starts as `SeriesBox.Empty` (already the field initializer default).

**Current constructor (lines to change):**
```csharp
// REMOVE these two lines from MetricSlotHolder constructor:
var sentinel = new MetricSlot(0, null, DateTimeOffset.UtcNow);
Volatile.Write(ref _box, new SeriesBox(ImmutableArray.Create(sentinel)));
```

**After change — constructor simply assigns:**
```csharp
// Source: MetricSlotHolder.cs (direct inspection)
public DateTimeOffset ConstructedAt { get; } = DateTimeOffset.UtcNow;
// _box = SeriesBox.Empty is already the field initializer
```

### Pattern 2: ReadinessGrace — Computed Per-Holder Window

**What:** `ReadinessGrace` is `TimeSpan.FromSeconds(TimeSeriesSize * IntervalSeconds * GraceMultiplier)`. A holder is ready when `DateTimeOffset.UtcNow - ConstructedAt > ReadinessGrace`.

**Note:** This is a property, not a stored field. `DateTimeOffset.UtcNow` is evaluated at check time, so readiness is naturally time-advancing.

```csharp
// Source: CONTEXT.md decisions (design confirmed by code inspection)
public TimeSpan ReadinessGrace =>
    TimeSpan.FromSeconds(TimeSeriesSize * IntervalSeconds * GraceMultiplier);

public bool IsReady =>
    DateTimeOffset.UtcNow - ConstructedAt > ReadinessGrace;
```

**Alternatively** (keep the property on `MetricSlotHolder` and check inline in `SnapshotJob` — either approach is fine; exposing `IsReady` on the holder is cleaner for tests).

### Pattern 3: Pre-Tier Readiness Check in EvaluateTenant

**What:** Before Tier 1 (HasStaleness), check if the tenant is "not ready". If any holder is not ready, return `TierResult.Unresolved` immediately. This matches the CONTEXT.md decision: "Not-ready tenant blocks the advance gate (same effect as Unresolved)."

**Where to put it:** In `SnapshotJob.EvaluateTenant`, before the `HasStaleness` call. The check can be a new private static method `IsReady(IReadOnlyList<MetricSlotHolder>)` (mirrors `HasStaleness` placement pattern).

```csharp
// Source: SnapshotJob.cs (direct inspection), CONTEXT.md decisions
internal TierResult EvaluateTenant(Tenant tenant)
{
    // Pre-tier: Readiness check — tenant is not judged until all holders are ready
    if (!IsReady(tenant.Holders))
    {
        _logger.LogDebug(
            "Tenant {TenantId} priority={Priority} not ready (in grace window) — skipping",
            tenant.Id, tenant.Priority);
        return TierResult.Unresolved;
    }

    // Tier 1: Staleness check ...
    if (HasStaleness(tenant.Holders))
    { ... }
    ...
}

private static bool IsReady(IReadOnlyList<MetricSlotHolder> holders)
{
    var now = DateTimeOffset.UtcNow;
    foreach (var holder in holders)
    {
        if (now - holder.ConstructedAt <= holder.ReadinessGrace)
            return false; // At least one holder still in grace window
    }
    return true;
}
```

**Why pre-tier vs. inside HasStaleness:** Readiness is conceptually different from staleness. Putting it in `HasStaleness` would require that method to return a tri-state (stale / not-ready / fresh), breaking the clean boolean contract. A pre-tier check in `EvaluateTenant` is consistent with how Tier 1 itself is already structured.

### Pattern 4: Staleness Post-Readiness — Three States

**What:** After readiness is confirmed, `HasStaleness` already handles `ReadSlot() == null` with `continue` (skip = not stale). Per CONTEXT.md, the new behavior is: once grace ends and the holder has no data, it is stale. This means `HasStaleness` must be updated to treat null `ReadSlot()` as stale when grace has ended.

**Current HasStaleness logic for null slots (line 224–225):**
```csharp
var slot = holder.ReadSlot();
if (slot is null)
    continue; // No data yet — cannot judge, not stale
```

**After Phase 60:** This `continue` is still correct — because the pre-tier readiness check already ensured all holders are ready. If we reach `HasStaleness` and a holder has `ReadSlot() == null`, it means grace ended and the device never responded. That is stale.

```csharp
// After Phase 60: reached only when IsReady() already passed
var slot = holder.ReadSlot();
if (slot is null)
    return true; // Grace ended, no data — device never responded — stale
```

**Important:** The `continue` in `AreAllResolvedViolated` and `AreAllEvaluateViolated` for `series.Length == 0` remains correct — empty holders skip threshold participation (CONTEXT.md: "Empty holders are skipped — do not participate in threshold checks"). This is already implemented and requires no change.

### Pattern 5: CopyFrom and ConstructedAt Interaction

**What:** `TenantVectorRegistry.Reload` calls `newHolder.CopyFrom(oldHolder)` to carry over series data when config reloads. After Phase 60, `newHolder` has a fresh `ConstructedAt` set at construction time.

**Critical observation:** If config reloads and an old holder has real series data that gets copied via `CopyFrom`, the new holder will have `ConstructedAt = now` but will already have data. This means the new holder will be in the readiness grace window even though it has current data — it will block evaluation for `ReadinessGrace` duration after each config reload.

**Resolution options:**
1. When `CopyFrom` is called and copies real data, also reset `ConstructedAt` to far in the past (e.g., `DateTimeOffset.MinValue`) so the holder is immediately ready.
2. Or: If `ReadSeries().Length > 0` after `CopyFrom`, the holder has real data and should be considered ready immediately.

**CONTEXT.md does not address this directly.** This is a Claude's Discretion area. The recommended approach: add a `MarkReady()` method or simply use a mutable `ConstructedAt` field that `CopyFrom` sets to `DateTimeOffset.MinValue` when copying real data. Simpler alternative: check `ReadSeries().Length > 0` in `IsReady` as an early-out (holder is ready if it has any real data).

**Simplest correct implementation:**
```csharp
private static bool IsReady(IReadOnlyList<MetricSlotHolder> holders)
{
    var now = DateTimeOffset.UtcNow;
    foreach (var holder in holders)
    {
        // Holder with real data is always ready (e.g., after CopyFrom on config reload)
        if (holder.ReadSeries().Length > 0)
            continue;
        if (now - holder.ConstructedAt <= holder.ReadinessGrace)
            return false;
    }
    return true;
}
```

This correctly handles both: fresh holders in grace window (no data, grace not elapsed = not ready) and carried-over holders (has data = ready regardless of ConstructedAt).

### Anti-Patterns to Avoid

- **Putting readiness inside HasStaleness:** Makes HasStaleness return a tri-state, breaks boolean contract, complicates all callers and tests.
- **Making ConstructedAt mutable without discipline:** Multiple callers writing to it creates confusion. Use `ReadSeries().Length > 0` as the ready-with-data signal instead.
- **Forgetting the CopyFrom interaction:** New holders created during config reload will have `ConstructedAt = now`. If data was copied from the old holder, the holder should be considered ready. Ignoring this causes all tenants to block for the full grace window after every config reload.
- **Changing the `continue` in AreAllResolvedViolated/AreAllEvaluateViolated:** These methods skip empty-series holders from threshold participation. This is correct behavior (CONTEXT.md: "Empty holders do not participate in threshold checks"). Do not change them.

## Don't Hand-Roll

This phase is entirely in-process C# logic. There are no external problems requiring libraries.

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Time-based readiness | Custom timer/state machine | `DateTimeOffset.UtcNow` arithmetic | Already used throughout the codebase for staleness; stateless, testable |
| Thread safety | Mutex/lock | Existing `Volatile.Read/Write` pattern | `_box` swap pattern already established; `ConstructedAt` is init-only, no write after construction |
| Test time control | Real clock in tests | `Task.Delay` (already used in stale test) or tiny grace window via `graceMultiplier: 0.001` | Existing `EvaluateTenant_StaleHolder_SkipsToCommands` test uses `await Task.Delay(1.5s)` — same pattern works for readiness |

**Key insight:** The codebase already has a working pattern for "tiny window" testing: `graceMultiplier: 0.001` gives a 1ms window. Tests for "not ready" use a just-constructed holder with no delay; tests for "becomes ready" use `graceMultiplier: 0.001` plus a small delay.

## Common Pitfalls

### Pitfall 1: Sentinel Tests in MetricSlotHolderTests

**What goes wrong:** `MetricSlotHolderTests` has tests that assert sentinel behavior: `ReadSlot_BeforeAnyWrite_ReturnsSentinel`, `ReadSeries_BeforeAnyWrite_ReturnsSentinel`. These will fail after Phase 60 because `ReadSlot()` returns null before any write, and `ReadSeries()` returns an empty array.

**Specific tests to change:**
- `ReadSlot_BeforeAnyWrite_ReturnsSentinel` → rename and flip: `ReadSlot_BeforeAnyWrite_ReturnsNull`
- `ReadSeries_BeforeAnyWrite_ReturnsSentinel` → `ReadSeries_BeforeAnyWrite_ReturnsEmpty`
- `CopyFrom_CopiesSeriesAndMetadata` — asserts `series.Length == 3` based on "sentinel + 2 writes". After Phase 60, it will be 2 writes (length 2, not 3).

**How to avoid:** Do a targeted search for "sentinel" in `MetricSlotHolderTests.cs` before starting — there are ~3-4 tests that directly assert sentinel properties.

**Warning signs:** Any test asserting `Assert.Equal(0, series[0].Value)` or `Assert.Single(series)` on a freshly constructed holder.

### Pitfall 2: Sentinel Tests in SnapshotJobTests

**What goes wrong:** `SnapshotJobTests` has tests that comment "sentinel" and reason about Value=0 participating in threshold checks. After Phase 60, a freshly constructed holder has no series, so AreAllResolvedViolated/AreAllEvaluateViolated skip it. The test outcomes change.

**Specific cases identified (lines from grep):**
- Line 98–111: `EvaluateTenant_SentinelReadSlot_NotStaleWithinGrace` — tests that a sentinel (no real write) with large interval is not stale. After Phase 60: holder has no data, is in grace window, IsReady returns false → `TierResult.Unresolved`. Test must be renamed and expectation flipped.
- Line 224–236: Tests reasoning about `h2 sentinel Value=0 < Min=10 → violated`. After Phase 60: h2 has no data, series.Length == 0, is skipped by AreAllResolvedViolated. Outcome changes — only h1 (with real data) participates.
- Line 578–589: Similar pattern for Evaluate tier.
- Line 339, 694: Depth-3 tests that include sentinel as one of the 3 samples.

**How to avoid:** Grep `SnapshotJobTests.cs` for the word "sentinel" before coding — all 14 occurrences identify tests requiring attention.

**Warning signs:** Test comments containing "sentinel" or assertions that a holder with no real write participates in threshold evaluation.

### Pitfall 3: EvaluateTenant_StaleHolder Test Scope Ambiguity

**What goes wrong:** The test `EvaluateTenant_StaleHolder_SkipsToCommands` (line 158) writes a value, waits 1.5s for grace to expire (interval=1, multiplier=1.0, grace=1s), then expects `Unresolved`. After Phase 60, there is now a *readiness* grace window: `TimeSeriesSize(1) × IntervalSeconds(1) × GraceMultiplier(1.0) = 1s`. Both windows are 1 second. The staleness test may now be ambiguous: is `Unresolved` because stale or because not-ready?

**How to avoid:** Use a holder with very small readiness grace (e.g., `graceMultiplier: 0.001`) for the staleness test and a larger staleness window — or add a small delay before writing the value to ensure readiness grace elapsed, then check staleness. Alternatively use `intervalSeconds=1, graceMultiplier=0.001` so readiness expires in 1ms, and staleness expires in 1s.

### Pitfall 4: CopyFrom Does Not Reset ConstructedAt

**What goes wrong:** After a config reload, all new holders get `ConstructedAt = now`. If `CopyFrom` copies real series data, the holder is "ready" (has data) but `ConstructedAt` is still recent, so a naive `IsReady` check returns "not ready" — tenants block evaluation for the full grace window after every reload.

**How to avoid:** Use the "has data" short-circuit in `IsReady` (see Pattern 5 above). Alternatively, give `MetricSlotHolder` a `MarkReady()` method called by `CopyFrom` to backdate `ConstructedAt`, but the data-length check is simpler and has no mutability concerns.

### Pitfall 5: STS-05 E2E Priming Becomes Unnecessary But Must Not Break

**What goes wrong:** `STS-05-staleness.sh` currently primes with `sim_set_scenario healthy; sleep 20` so that slots have real timestamps before switching to stale. After Phase 60, freshly-reloaded tenants will be in the grace window (`IntervalSeconds=10, GraceMultiplier=2.0, grace=20s` for the E2E cluster config), so even without priming the scenario would not trigger tier=1 during the grace window. The priming step is still *correct* (it waits for the grace window to pass naturally), but the comment explaining it will be outdated.

**How to avoid:** Update the comment in STS-05 to note that priming serves double duty: (1) populating timestamps for staleness, (2) waiting for readiness grace window to elapse. The sleep duration (20s) matches the readiness grace window exactly, which is convenient.

### Pitfall 6: MTS-03 E2E Scenario Grace Window

**What goes wrong:** The MTS-03 scenario (40-mts-03-starvation-proof.sh) applies `tenant-cfg03-two-diff-prio.yaml`, waits for reload, then immediately polls for `tier=4` log. After Phase 60, newly loaded tenants are in the grace window. The `poll_until_log 90s` timeout should be sufficient (90s >> 20s grace), but the test comment should acknowledge the grace window.

**How to avoid:** The existing 90s timeout absorbs the grace window automatically. No functional change needed to the scenario; just a comment update to note that tier=4 is expected only after the grace window elapses.

### Pitfall 7: IntervalSeconds=0 Holders in IsReady

**What goes wrong:** Holders with `IntervalSeconds=0` are excluded from staleness checks (CONTEXT.md, existing behavior). Should they also be excluded from readiness? With `IntervalSeconds=0`, `ReadinessGrace = 0 × anything = 0 seconds` — the holder is immediately ready upon construction. This is the correct behavior: if the interval is zero, there's no fill window to wait for. No special case needed.

**How to avoid:** Nothing to do — the formula naturally handles IntervalSeconds=0 correctly (ReadinessGrace = 0 → IsReady immediately = true).

## Code Examples

### Constructor After Phase 60

```csharp
// Source: MetricSlotHolder.cs (modified)
public sealed class MetricSlotHolder
{
    private SeriesBox _box = SeriesBox.Empty; // no sentinel write needed

    public DateTimeOffset ConstructedAt { get; } = DateTimeOffset.UtcNow;

    public TimeSpan ReadinessGrace =>
        TimeSpan.FromSeconds(TimeSeriesSize * IntervalSeconds * GraceMultiplier);

    public MetricSlotHolder(string ip, int port, string metricName, int intervalSeconds,
        string role, int timeSeriesSize = 1, double graceMultiplier = 2.0,
        ThresholdOptions? threshold = null)
    {
        Ip = ip;
        Port = port;
        MetricName = metricName;
        IntervalSeconds = intervalSeconds;
        Role = role;
        TimeSeriesSize = timeSeriesSize;
        GraceMultiplier = graceMultiplier;
        Threshold = threshold;
        // No sentinel write — series starts empty
    }
    // ... ReadSlot, ReadSeries, WriteValue, CopyFrom unchanged
}
```

### ReadSlot After Phase 60

```csharp
// Source: MetricSlotHolder.cs (existing, already correct)
// ReadSlot() already returns null when series is empty (s.Length == 0)
public MetricSlot? ReadSlot()
{
    var s = Volatile.Read(ref _box).Series;
    return s.Length > 0 ? s[^1] : null;
}
// No change needed — the null return before any write was suppressed by the sentinel.
// After sentinel removal, null = no real data received.
```

### IsReady Check in SnapshotJob

```csharp
// Source: SnapshotJob.cs (new method, modeled on HasStaleness pattern)
private static bool IsReady(IReadOnlyList<MetricSlotHolder> holders)
{
    var now = DateTimeOffset.UtcNow;
    foreach (var holder in holders)
    {
        // Holder with real data is always ready (handles CopyFrom on config reload)
        if (holder.ReadSeries().Length > 0)
            continue;

        // No data yet — check grace window
        if (now - holder.ConstructedAt <= holder.ReadinessGrace)
            return false;
    }
    return true;
}
```

### HasStaleness Post-Readiness (updated null handling)

```csharp
// Source: SnapshotJob.cs (existing HasStaleness, one line change)
// The null-slot continue becomes: return true (stale — grace ended, no data)
var slot = holder.ReadSlot();
if (slot is null)
    return true; // Grace ended + no data = device never responded = stale
```

### Unit Test Pattern for "Not Ready"

```csharp
// Source: SnapshotJobTests.cs (new test, uses existing MakeHolder pattern)
[Fact]
public void EvaluateTenant_HolderInGraceWindow_BlocksGate()
{
    // Default graceMultiplier=2.0, intervalSeconds=3600 → grace=7200s — holder never ready in test
    var holder = MakeHolder(intervalSeconds: 3600, graceMultiplier: 2.0, role: "Resolved",
        threshold: new ThresholdOptions { Min = 0, Max = 100 });
    // No write — series empty, ConstructedAt = now, grace = 7200s → not ready

    var tenant = MakeTenant(holder);
    var result = _job.EvaluateTenant(tenant);

    Assert.Equal(TierResult.Unresolved, result);
}

[Fact]
public async Task EvaluateTenant_HolderPastGraceNoData_IsStale()
{
    // Tiny grace window (1ms) — holder exits grace window almost immediately
    var holder = MakeHolder(intervalSeconds: 1, graceMultiplier: 0.001, role: "Resolved",
        threshold: new ThresholdOptions { Min = 0, Max = 100 });
    // No write — series empty, grace = 1ms

    await Task.Delay(TimeSpan.FromMilliseconds(10)); // Grace expired

    var cmd = new CommandSlotOptions
        { Ip = "10.0.0.1", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
    var tenant = MakeTenant([holder], [cmd]);
    var result = _job.EvaluateTenant(tenant);

    // Past grace + no data = stale → tier=4 → Unresolved
    Assert.Equal(TierResult.Unresolved, result);
}
```

### MetricSlotHolderTests Updates

```csharp
// Source: MetricSlotHolderTests.cs (tests to replace)

// REPLACE: ReadSlot_BeforeAnyWrite_ReturnsSentinel
[Fact]
public void ReadSlot_BeforeAnyWrite_ReturnsNull()
{
    var holder = CreateHolder();
    Assert.Null(holder.ReadSlot()); // No sentinel — series is empty
}

// REPLACE: ReadSeries_BeforeAnyWrite_ReturnsSentinel
[Fact]
public void ReadSeries_BeforeAnyWrite_ReturnsEmpty()
{
    var holder = CreateHolder();
    Assert.Empty(holder.ReadSeries());
}

// UPDATE: CopyFrom_CopiesSeriesAndMetadata
// Old assertion: Assert.Equal(3, fresh.ReadSeries().Length) // "sentinel + 2 writes"
// New assertion: Assert.Equal(2, fresh.ReadSeries().Length) // 2 real writes only
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Sentinel sample (Value=0, Timestamp=now) in constructor | Empty series + `ConstructedAt` grace window | Phase 60 | Eliminates false Value=0 in threshold checks; adds detection of "never responded" devices |
| Null ReadSlot = skip (not stale) | Null ReadSlot after grace = stale (stale = never responded) | Phase 60 | New capability: detect devices that never polled successfully |
| STS-05 priming: populates timestamps to avoid null-slot skip | Grace window: blocks evaluation during fill window | Phase 60 | MTS-03 startup race eliminated; priming step still works but rationale changes |

**Deprecated/outdated after Phase 60:**
- The XML doc comment "ReadSlot returns a sentinel (Value=0, Timestamp=construction time) before any real write" — must be removed/replaced
- `MTS-03 priming fix TODO` mentioned in STATE.md / MEMORY — becomes unnecessary

## Open Questions

1. **Should `ReadinessGrace` be a property on MetricSlotHolder or computed inline in SnapshotJob?**
   - What we know: `TimeSeriesSize`, `IntervalSeconds`, `GraceMultiplier` are all `MetricSlotHolder` properties. The formula is `TimeSeriesSize * IntervalSeconds * GraceMultiplier`.
   - What's unclear: Whether the property should be `public` (for unit testing readiness directly on the holder) or package-internal.
   - Recommendation: Make it a `public` computed property on `MetricSlotHolder` for testability. Cost is zero (no new state).

2. **CopyFrom: should ConstructedAt be mutable to allow backdating after carry-over?**
   - What we know: The "has data" short-circuit in `IsReady` (see Pattern 5) makes this unnecessary for correctness.
   - What's unclear: Whether the planner prefers the mutable-backdate approach for explicitness.
   - Recommendation: Use the data-length check (`ReadSeries().Length > 0 → continue` in `IsReady`). Simpler, no new mutable state.

3. **Should the "not ready" log message be Debug or Information?**
   - What we know: All other tier-level logs are `LogDebug`. `Unresolved` causes the advance gate to block. During startup, this fires on every cycle for every tenant until the grace window expires.
   - What's unclear: Whether a high-volume Debug log is acceptable or whether it should be silent (no log at all).
   - Recommendation: Log at `LogDebug` level — consistent with tier logs, visible with debug logging enabled, silent in production. Not logging at all is also valid given the high-frequency startup case.

4. **Does the MTS-03 e2e scenario need a new sub-scenario for "not ready during grace window"?**
   - What we know: The MTS-03 scenario tests starvation (P2 never evaluated while P1 is Unresolved). After Phase 60, a new property is that P1 is "not ready" during startup, which also blocks P2. This is MTS-03's original motivation (the priming-fix TODO).
   - What's unclear: Whether a dedicated e2e sub-scenario (MTS-03D?) should assert that during the grace window, tier=4 logs are absent — or whether this is out of scope for Phase 60.
   - Recommendation: This is Claude's discretion. A new sub-scenario would validate the startup race fix but adds test duration (must wait for grace window). The CONTEXT.md says "MTS-03 startup race is eliminated" as a success criterion, so at minimum the existing 40-mts-03 scenario must survive Phase 60, and optionally a new sub-scenario is added.

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — Complete source inspection; sentinel location at lines 55-56, all properties identified
- `src/SnmpCollector/Jobs/SnapshotJob.cs` — Complete source inspection; `HasStaleness` null-slot handling at line 224, tier structure confirmed
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — `CopyFrom` interaction at lines 100-107; `ReadSlot() is not null` carry-over guard identified
- `.planning/phases/60-readiness-window-for-holders/60-CONTEXT.md` — Locked decisions verified
- `tests/SnmpCollector.Tests/Pipeline/MetricSlotHolderTests.cs` — All sentinel-dependent tests identified
- `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` — All 14 sentinel-dependent test locations identified via grep
- `tests/e2e/scenarios/40-mts-03-starvation-proof.sh` — MTS-03 scenario structure and timing confirmed
- `tests/e2e/scenarios/33-sts-05-staleness.sh` — STS-05 priming pattern confirmed; grace window = 20s at E2E intervals

### Secondary (MEDIUM confidence)
- None — all findings are from direct codebase inspection

### Tertiary (LOW confidence)
- None

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new dependencies; all from codebase inspection
- Architecture: HIGH — patterns derived directly from existing code; design decisions follow established conventions in the codebase
- Pitfalls: HIGH — all pitfalls identified from actual code that will break (sentinel tests enumerated by line number)

**Research date:** 2026-03-19
**Valid until:** 2026-04-18 (30 days; stable internal refactor, no external dependencies)
