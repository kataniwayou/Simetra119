# Phase 73: SnapshotJob Instrumentation - Research

**Researched:** 2026-03-23
**Domain:** C# / OTel Metrics / SnapshotJob evaluation loop / CommandWorkerService SET failure
**Confidence:** HIGH

## Summary

Phase 73 wires the 8 `ITenantMetricService` methods (created in Phase 72) into
`SnapshotJob.EvaluateTenant` and `CommandWorkerService` so that every evaluation cycle
emits per-tenant counters, gauge state, and histogram duration to Prometheus.

The interface, its concrete implementation, and DI registration are already complete from
Phase 72. `ITenantMetricService` is already injected into `SnapshotJob` (constructor +
field `_tenantMetrics` present, unused). The work is exclusively call-site wiring plus
two structural changes: (1) add `TenantId` + `Priority` to `CommandRequest` so
`CommandWorkerService` can tag failed-SET metrics, and (2) extend `CommandWorkerService`
to accept and call `ITenantMetricService` for those failures.

There are no external library choices to make. All patterns follow what is already
established in the codebase.

**Primary recommendation:** Wire calls directly in `EvaluateTenant` using a `try/finally`
Stopwatch pattern. Use a private helper method for the counter block that is called at
multiple return points, keeping each return path readable.

---

## Standard Stack

No new libraries required. All instrumentation uses existing infrastructure.

### Already Available
| Component | Location | What It Provides |
|-----------|----------|-----------------|
| `ITenantMetricService` | `src/SnmpCollector/Telemetry/ITenantMetricService.cs` | 8 metric methods with `(tenantId, priority)` signature |
| `TenantMetricService` | `src/SnmpCollector/Telemetry/TenantMetricService.cs` | Singleton implementation; all 8 instruments created on `SnmpCollector.Tenant` meter |
| DI registration | `ServiceCollectionExtensions.cs:409` | `services.AddSingleton<ITenantMetricService, TenantMetricService>()` — already registered |
| `_tenantMetrics` field | `SnapshotJob.cs:29` | Field exists, constructor parameter exists — both unused, ready to wire |
| `System.Diagnostics.Stopwatch` | Already imported in SnapshotJob (`using System.Diagnostics;`) | Per-tenant duration measurement |
| `TenantState` enum | `src/SnmpCollector/Pipeline/TenantState.cs` | Values `NotReady=0, Healthy=1, Resolved=2, Unresolved=3` match gauge encoding |

### Not Yet Done
| Component | Location | Required Change |
|-----------|----------|-----------------|
| `CommandRequest` | `src/SnmpCollector/Pipeline/CommandRequest.cs` | Add `TenantId` + `Priority` properties |
| `CommandWorkerService` | `src/SnmpCollector/Services/CommandWorkerService.cs` | Add `ITenantMetricService` constructor parameter + call `IncrementCommandFailed` on SET failures |

---

## Architecture Patterns

### EvaluateTenant Structure (current — 4 return points)

```
EvaluateTenant(Tenant tenant):
  1. Pre-tier: !AreAllReady  → return TenantState.NotReady       [line 141]
  2. Tier 2:   AreAllResolvedViolated → return TenantState.Resolved  [line 159]
  3. Tier 3:   !AreAllEvaluateViolated → return TenantState.Healthy  [line 173]
  4. Tier 4:   (command dispatch loop) → return TenantState.Unresolved [line 216]
```

Each return point needs:
- State gauge recorded (`RecordTenantState`)
- Duration recorded (`RecordEvaluationDuration`)
- Some return points also need tier/command counters (see counter rules below)

### Counter Rules per Return Point (from CONTEXT.md)

| Return Point | Tier Counters | Command Counters | State Gauge | Duration |
|---|---|---|---|---|
| NotReady (pre-tier) | NONE | NONE | YES | YES |
| Resolved (tier 2) | tier1_stale count, tier2_resolved count | NONE | YES | YES |
| Healthy (tier 3) | tier1_stale count, tier2_resolved count, tier3_evaluate count | NONE | YES | YES |
| Unresolved (tier 4) | tier1_stale count, tier2_resolved count, tier3_evaluate count | dispatched/suppressed/failed per cmd | YES | YES |

**Counter value semantics:**
- `tier1_stale`: count of stale holders (NOT +1; count actual stale holders). Called always except NotReady.
- `tier2_resolved`: count of resolved-role metrics that are NOT violated (those that "resolved" successfully). Called always except NotReady — even when 0.
- `tier3_evaluate`: count of violated evaluate metrics. Called always except NotReady — even when 0.
- Command counters: +1 per command (dispatched, suppressed, or failed) within the Tier 4 loop.

**Important: These are counts of holders/commands, not just +1 per call.**
The interface methods increment by 1 each call — so call them in a loop or count first and call Add(count).

Looking at `TenantMetricService.IncrementTier1Stale`, it calls `_tier1Stale.Add(1, ...)`.
This means: to record count=N stale holders, either loop N times (once per holder) or
change the increment amount. The interface signature is `void IncrementTier1Stale(string, int)` — no
count parameter. Therefore the correct approach is to **count first, then call once with the
existing +1 method N times** or, more cleanly, record by calling in a loop over stale holders.

Actually, re-reading CONTEXT.md: "always record the count of stale holders" — this means
count the stale holders and report that count. Since the service only increments by 1,
the pattern is either:
- Call `IncrementTier1Stale` in a loop (once per stale holder), OR
- Add a new `AddTier1Stale(count)` overload

The interface has no bulk Add. The existing implementation uses `.Add(1, ...)`. To add N,
call N times or (preferred) call `.Add(N, ...)` — but the interface only exposes
`IncrementTier1Stale(string, int)` which always adds 1.

**Resolution:** Count stale holders into a local variable, then loop that many times calling
`_tenantMetrics.IncrementTier1Stale(tenant.Id, tenant.Priority)`. This keeps the interface
unchanged. Alternatively, refactor the tier counter recording to accept a count and call
`_tier1Stale.Add(count, ...)` — but that requires interface change.

Given CONTEXT.md says "always record the count", and the interface only increments by 1,
the simplest approach without interface changes is to call the method once-per-unit in a loop.
However this is inefficient. The recommended approach: count locally, call a single `Add(count)`
— which requires slightly extending the interface or calling the underlying counter directly.

Since `TenantMetricService` is a concrete singleton that the planner can modify, the clean
solution is to change the interface method to accept an optional count parameter or to add
`AddTier1Stale(string tenantId, int priority, long count)` methods alongside the existing
`IncrementTier1Stale`. The planner should decide — this is a Claude's Discretion area.

### Stopwatch Pattern

**Current:** `SnapshotJob` already has `using System.Diagnostics;`. There is already a
job-level stopwatch in `Execute()` (lines 57-102). The per-tenant stopwatch goes inside
`EvaluateTenant` — separate from the job-level one.

**Recommended pattern (try/finally):**
```csharp
internal TenantState EvaluateTenant(Tenant tenant)
{
    var sw = Stopwatch.StartNew();
    try
    {
        // ... all evaluation logic with returns ...
        // Each return point calls:
        // _tenantMetrics.RecordTenantState(tenant.Id, tenant.Priority, state);
        // _tenantMetrics.RecordEvaluationDuration(tenant.Id, tenant.Priority, sw.Elapsed.TotalMilliseconds);
        // return state;
    }
    finally
    {
        // try/finally ensures duration is always recorded even on unexpected exceptions
        // But: RecordTenantState needs to be called before return, not in finally
        // (state value is not accessible in finally)
    }
}
```

**Alternative (single start, record before each return):**
```csharp
internal TenantState EvaluateTenant(Tenant tenant)
{
    var sw = Stopwatch.StartNew();

    if (!AreAllReady(tenant.Holders))
    {
        _tenantMetrics.RecordTenantState(tenant.Id, tenant.Priority, TenantState.NotReady);
        _tenantMetrics.RecordEvaluationDuration(tenant.Id, tenant.Priority, sw.Elapsed.TotalMilliseconds);
        return TenantState.NotReady;
    }
    // ...
}
```

**Recommendation:** The "record before each return" approach (alternative) because:
- `try/finally` can't record the gauge in `finally` (no access to state value at that point)
- The current method has 4 clean return points — each is explicit
- CONTEXT.md explicitly permits this and marks it as Claude's Discretion

### Helper Method Pattern

A private `RecordAndReturn` helper reduces repetition at each return:

```csharp
// Source: internal design pattern consistent with codebase style
private TenantState RecordAndReturn(Tenant tenant, TenantState state, Stopwatch sw)
{
    _tenantMetrics.RecordTenantState(tenant.Id, tenant.Priority, state);
    _tenantMetrics.RecordEvaluationDuration(tenant.Id, tenant.Priority, sw.Elapsed.TotalMilliseconds);
    return state;
}
```

Each return point becomes:
```csharp
return RecordAndReturn(tenant, TenantState.NotReady, sw);
```

This is clean and reduces the chance of missing a call at one of the 4 exit points.
CONTEXT.md marks this as Claude's Discretion.

### CommandRequest Extension

```csharp
// Before (src/SnmpCollector/Pipeline/CommandRequest.cs)
public sealed record CommandRequest(
    string Ip,
    int Port,
    string CommandName,
    string Value,
    string ValueType);

// After — add TenantId and Priority
public sealed record CommandRequest(
    string Ip,
    int Port,
    string CommandName,
    string Value,
    string ValueType,
    string TenantId,
    int Priority);
```

**Impact:** Every `new CommandRequest(...)` call site must be updated. Current call sites:
- `SnapshotJob.cs:193-194` — 1 construction site (has tenant.Id and tenant.Priority available)
- `SnapshotJobTests.cs` — multiple test stub constructions using `new CommandRequest(...)` directly (lines 828-829, 1217)
- `CommandWorkerServiceTests.cs:81` — `MakeRequest()` helper
- Any other test helpers

### CommandWorkerService Injection

The service currently has `PipelineMetricService _pipelineMetrics` injected (constructor
parameter 8). `ITenantMetricService` needs to be added as parameter 9.

**Current constructor signature (10 params):**
```csharp
public CommandWorkerService(
    ICommandChannel commandChannel,
    ISnmpClient snmpClient,
    ISender sender,
    IDeviceRegistry deviceRegistry,
    ICommandMapService commandMapService,
    ICorrelationService correlation,
    ILeaderElection leaderElection,
    PipelineMetricService pipelineMetrics,
    IOptions<SnapshotJobOptions> snapshotJobOptions,
    ILogger<CommandWorkerService> logger)
```

Add `ITenantMetricService tenantMetrics` after `pipelineMetrics` (parameter 9).

**Call site in `CommandWorkerService`:** Only in SET failure paths (inside `ExecuteCommandAsync`):
- OID not found (line 107) — `_pipelineMetrics.IncrementCommandFailed($"{req.Ip}:{req.Port}")`
- Device not found (line 117) — `_pipelineMetrics.IncrementCommandFailed($"{req.Ip}:{req.Port}")`
- Timeout (line 159) — `_pipelineMetrics.IncrementCommandFailed(device.Name)`
- General exception catch (line 87) — `_pipelineMetrics.IncrementCommandFailed($"{req.Ip}:{req.Port}")`

After adding `TenantId` + `Priority` to `CommandRequest`, these become:
```csharp
_tenantMetrics.IncrementCommandFailed(req.TenantId, req.Priority);
```

Note: The `_pipelineMetrics.IncrementCommandFailed(...)` calls in `CommandWorkerService`
are the existing pipeline-level metrics (device_name tag) — they remain. The new calls
add per-tenant metrics alongside them. Both fire on SET failures in CommandWorkerService.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Stopwatch pattern | Custom timing class | `System.Diagnostics.Stopwatch` (already imported) | Already used in SnapshotJob.Execute() and CommandWorkerService |
| Tag construction | Manual dictionary | `new TagList { { "tenant_id", ... } }` | Already used in TenantMetricService |
| Counting stale holders | New iterating method | Reuse existing `HasStaleness` logic or count inline | Logic already exists in private methods |

---

## Common Pitfalls

### Pitfall 1: Forgetting a return point
**What goes wrong:** One of the 4 return paths in `EvaluateTenant` doesn't call state
gauge or duration — Prometheus shows missing samples for some states.
**How to avoid:** Use a `RecordAndReturn` private helper so every return path is a
single call. Verify in tests that each `TenantState` case triggers both metrics.
**Warning signs:** Test assertions on `RecordTenantState` calls count not matching 1.

### Pitfall 2: Recording state gauge in finally block
**What goes wrong:** Attempting `try/finally` with gauge record in `finally` — `TenantState`
value is not accessible in the finally block (multiple return points, no shared variable).
**How to avoid:** Either record state before each return (explicit), or capture state in
a local variable and record both gauge and duration in finally.
**Actual pattern if using finally:**
```csharp
var state = TenantState.NotReady;
var sw = Stopwatch.StartNew();
try
{
    // ... set state before each logical return ...
    state = TenantState.Healthy;
    return state;
}
finally
{
    _tenantMetrics.RecordTenantState(tenant.Id, tenant.Priority, state);
    _tenantMetrics.RecordEvaluationDuration(tenant.Id, tenant.Priority, sw.Elapsed.TotalMilliseconds);
}
```
This requires removing the early returns and restructuring — more invasive. The helper method
approach is cleaner.

### Pitfall 3: CommandRequest construction breakage
**What goes wrong:** Adding `TenantId` and `Priority` to `CommandRequest` as required
positional record parameters breaks all existing `new CommandRequest(...)` call sites at
compile time.
**How to avoid:** Update ALL construction sites in a single task:
- `SnapshotJob.cs` (production — has `tenant.Id` and `tenant.Priority` available)
- `SnapshotJobTests.cs` — multiple stub constructions (`new CommandRequest("x", 1, "fill", "0", "Integer32")`)
- `CommandWorkerServiceTests.cs` — `MakeRequest()` helper at line 81
**Warning signs:** Build failures immediately after changing the record.

### Pitfall 4: Stale-holder count semantics vs. interface increment-by-1
**What goes wrong:** `ITenantMetricService.IncrementTier1Stale` always adds 1.
CONTEXT.md says "record the count of stale holders". If there are 3 stale holders and
you call `IncrementTier1Stale` once, the counter only goes up by 1 instead of 3.
**How to avoid:** Either (a) loop over stale holders calling `IncrementTier1Stale` once
per stale holder, or (b) extend the interface to accept a count parameter.
Option (b) is cleaner — add `void AddTier1Stale(string, int, long count)` — but requires
interface + implementation change.
Option (a) requires counting stale holders separately from `HasStaleness` (which short-circuits
after finding the first stale holder and doesn't count all of them).

### Pitfall 5: NSubstitute mock in SnapshotJobTests missing new method calls
**What goes wrong:** Tests use `Substitute.For<ITenantMetricService>()` (line 69). After
wiring calls in `EvaluateTenant`, existing tests that don't assert on `_tenantMetrics` still
pass silently. But new tests that DO assert may use `Received()` — NSubstitute requires no
special setup for void methods.
**How to avoid:** Existing tests will continue to pass (NSubstitute accepts any calls on mocks
without explicit setup for void methods). New tests should use `.Received(N).Method(...)` to
verify call counts.

### Pitfall 6: CommandWorkerService test fixture not injecting ITenantMetricService
**What goes wrong:** `CommandWorkerServiceTests.CreateService()` helper (line 83-102) doesn't
pass `ITenantMetricService` — after adding it to the constructor, all test factory calls break.
**How to avoid:** Update `CreateService()` to accept optional `ITenantMetricService?` parameter
with `Substitute.For<ITenantMetricService>()` as default.

---

## Code Examples

### Verified: ITenantMetricService method signatures
```csharp
// Source: src/SnmpCollector/Telemetry/ITenantMetricService.cs
void IncrementTier1Stale(string tenantId, int priority);
void IncrementTier2Resolved(string tenantId, int priority);
void IncrementTier3Evaluate(string tenantId, int priority);
void IncrementCommandDispatched(string tenantId, int priority);
void IncrementCommandFailed(string tenantId, int priority);
void IncrementCommandSuppressed(string tenantId, int priority);
void RecordTenantState(string tenantId, int priority, TenantState state);
void RecordEvaluationDuration(string tenantId, int priority, double durationMs);
```

### Verified: TenantState enum integer values
```csharp
// Source: src/SnmpCollector/Pipeline/TenantState.cs
public enum TenantState
{
    NotReady   = 0,
    Healthy    = 1,
    Resolved   = 2,
    Unresolved = 3
}
```

### Verified: RecordTenantState gauge encoding
```csharp
// Source: src/SnmpCollector/Telemetry/TenantMetricService.cs:84-85
public void RecordTenantState(string tenantId, int priority, TenantState state)
    => _tenantState.Record((double)(int)state, new TagList { ... });
// Casts enum to int then to double — gauge values 0/1/2/3
```

### Verified: Existing Stopwatch usage pattern in SnapshotJob.Execute
```csharp
// Source: src/SnmpCollector/Jobs/SnapshotJob.cs:57-102
var sw = Stopwatch.StartNew();
// ... evaluation loop ...
sw.Stop();
_pipelineMetrics.RecordSnapshotCycleDuration(sw.Elapsed.TotalMilliseconds);
// Pattern: StartNew() → stop when done → pass TotalMilliseconds
```

### Verified: Existing command dispatch loop (Tier 4)
```csharp
// Source: src/SnmpCollector/Jobs/SnapshotJob.cs:178-216
foreach (var cmd in tenant.Commands)
{
    var suppressionKey = $"{tenant.Id}:{cmd.Ip}:{cmd.Port}:{cmd.CommandName}";
    if (_suppressionCache.TrySuppress(...))
    {
        _pipelineMetrics.IncrementCommandSuppressed(tenant.Id);  // → replace with _tenantMetrics
        continue;
    }
    var request = new CommandRequest(cmd.Ip, cmd.Port, cmd.CommandName, cmd.Value, cmd.ValueType);
    if (_commandChannel.Writer.TryWrite(request))
    {
        enqueueCount++;
        _pipelineMetrics.IncrementCommandDispatched(tenant.Id);  // → replace with _tenantMetrics
    }
    else
    {
        _pipelineMetrics.IncrementCommandFailed(tenant.Id);      // → replace with _tenantMetrics
    }
}
```
Note: `_pipelineMetrics.IncrementCommand*` calls in `EvaluateTenant` currently use pipeline
metrics with `tenant.Id` (a device_name-scoped tag). These become `_tenantMetrics.IncrementCommand*(tenant.Id, tenant.Priority)` — both tenant_id and priority tags.

### Verified: NSubstitute usage in SnapshotJobTests
```csharp
// Source: tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs:69
Substitute.For<ITenantMetricService>(),
// NSubstitute mock — all void methods are no-ops; Received() verifies call counts
```

### Verified: CommandWorkerService constructor test factory
```csharp
// Source: tests/SnmpCollector.Tests/Services/CommandWorkerServiceTests.cs:83-103
private CommandWorkerService CreateService(...)
{
    return new CommandWorkerService(
        commandChannel,
        snmpClient ?? new StubSnmpClient(),
        sender,
        deviceRegistry ?? new StubDeviceRegistry(),
        commandMapService ?? new StubCommandMapService(),
        new RotatingCorrelationService(),
        leaderElection ?? new AlwaysLeaderElection(),
        _metrics,                                    // PipelineMetricService
        Options.Create(new SnapshotJobOptions()),
        logger ?? NullLogger<CommandWorkerService>.Instance);
    // ITenantMetricService must be inserted before IOptions<SnapshotJobOptions>
}
```

---

## Key Implementation Details

### Which _pipelineMetrics calls to REPLACE vs. SUPPLEMENT

**In SnapshotJob.EvaluateTenant (lines 189, 199, 206):**
These 3 `_pipelineMetrics.IncrementCommand*` calls should be replaced by `_tenantMetrics.IncrementCommand*` calls. The pipeline-level metrics are device-scoped (using `tenant.Id` as device_name tag) — an anomaly from before per-tenant metrics existed. The new per-tenant calls are the canonical source.

However: existing tests assert on `snmp.command.dispatched` / `snmp.command.suppressed` pipeline metrics — those assertions would break if the `_pipelineMetrics` calls are removed. Verify whether any `SnapshotJobTests` tests assert on pipeline metric counts. Looking at the test file: the MeterListener in `SnapshotJobTests` only subscribes to `TelemetryConstants.MeterName` (the pipeline meter, not the tenant meter). There are no assertions on those command pipeline counters in the existing tests. The planner should decide whether to remove or keep both.

CONTEXT.md doesn't address this explicitly. The safest approach: keep the existing `_pipelineMetrics` calls (they provide pipeline-level aggregate counts) and ADD `_tenantMetrics` calls alongside them.

**In CommandWorkerService (lines 87, 107, 117, 159):**
These existing `_pipelineMetrics.IncrementCommandFailed(...)` calls use IP:Port or device.Name as the label. ADD `_tenantMetrics.IncrementCommandFailed(req.TenantId, req.Priority)` alongside — do not remove the pipeline-level calls.

### Stale Holder Counting Strategy

`HasStaleness` short-circuits (returns `true` on first stale holder). To count stale holders for the metric, a separate counting loop is needed. Options:
1. Inline counting loop in `EvaluateTenant` before/after calling `HasStaleness`
2. New private method `CountStaleHolders(IReadOnlyList<MetricSlotHolder>)` returning int
3. Extend `HasStaleness` to also return count (but that changes a private static method signature)

Option 2 is cleanest and follows existing patterns (all tier checks are private static methods).

### Resolved/Evaluate Holder Counting

Similarly, `AreAllResolvedViolated` doesn't return count of non-violated resolved holders.
To record "count of resolved-role metrics NOT violated" for tier2 counter:
- A separate counting method or inline loop is needed
- Same pattern for tier3 evaluate violated count

These are new private static methods parallel to the existing tier check methods.

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|---|---|---|---|
| Pipeline-level command counters (`snmp.command.*`) with device_name tag | Per-tenant counters (`tenant.command.*`) with tenant_id + priority tags | Phase 73 | Prometheus queries can now filter by tenant |
| No per-tenant duration measurement | Per-tenant histogram `tenant.evaluation.duration.milliseconds` | Phase 73 | Enables p50/p99 per tenant |
| No tenant state gauge | `tenant.state` gauge (0-3) | Phase 73 | State transitions visible in Grafana |

---

## Open Questions

1. **Stale/Resolved/Evaluate count semantics vs. increment-by-1 interface**
   - What we know: `ITenantMetricService` methods all increment by 1 (confirmed by implementation). CONTEXT.md says "always record the count".
   - What's unclear: Does the planner want to call `IncrementTier1Stale` N times (once per stale holder), or extend the interface to accept a delta?
   - Recommendation: Add private counting helpers and call the increment method in a loop, OR add `long count` parameters to the interface. The loop approach requires no interface change and is straightforward.

2. **Replace or supplement `_pipelineMetrics.IncrementCommand*` in EvaluateTenant**
   - What we know: Existing calls use `tenant.Id` as `device_name` tag on the pipeline meter. No existing tests assert on those pipeline counters in SnapshotJobTests.
   - Recommendation: Keep pipeline calls (backward compatibility) and ADD tenant metric calls. Remove only if explicitly desired.

3. **CountStaleHolders / CountResolvedNonViolated / CountEvaluateViolated private methods**
   - These are implied by the counter semantics but not explicitly specified.
   - Recommendation: Add three private static counting methods mirroring the three existing tier check methods.

---

## Sources

### Primary (HIGH confidence)
All findings are from direct codebase inspection — no external sources required for this phase.

- `src/SnmpCollector/Jobs/SnapshotJob.cs` — full EvaluateTenant logic, all 4 return points, existing `_tenantMetrics` field
- `src/SnmpCollector/Telemetry/ITenantMetricService.cs` — all 8 method signatures
- `src/SnmpCollector/Telemetry/TenantMetricService.cs` — implementation, gauge cast pattern
- `src/SnmpCollector/Pipeline/CommandRequest.cs` — current record shape (5 positional params)
- `src/SnmpCollector/Pipeline/TenantState.cs` — enum values 0-3
- `src/SnmpCollector/Pipeline/Tenant.cs` — `Id` and `Priority` available in EvaluateTenant
- `src/SnmpCollector/Services/CommandWorkerService.cs` — all `_pipelineMetrics.IncrementCommandFailed` call sites (4 places)
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs:409,427-428` — `ITenantMetricService` already registered; `CommandWorkerService` registered as singleton + hosted service
- `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` — `Substitute.For<ITenantMetricService>()` usage, all test helper patterns
- `tests/SnmpCollector.Tests/Services/CommandWorkerServiceTests.cs` — `CreateService()` factory helper shape

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all components verified in source
- Architecture: HIGH — EvaluateTenant structure fully read, all return points identified
- Pitfalls: HIGH — based on direct code inspection of construction sites and test patterns

**Research date:** 2026-03-23
**Valid until:** Stable (no fast-moving external deps); valid until codebase changes
