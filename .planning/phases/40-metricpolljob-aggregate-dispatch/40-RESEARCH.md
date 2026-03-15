# Phase 40: MetricPollJob Aggregate Dispatch - Research

**Researched:** 2026-03-15
**Domain:** C# / MediatR pipeline / SNMP polling / .NET 9
**Confidence:** HIGH — all findings verified directly from codebase

## Summary

Phase 40 is the behavioral payoff of v1.8: after `MetricPollJob.DispatchResponseAsync` dispatches each varbind individually, it must iterate `pollGroup.AggregatedMetrics`, compute each aggregate from the response values, and dispatch synthetic `SnmpOidReceived` messages through the full MediatR pipeline. All prerequisite infrastructure is complete (Phases 37-39): `CombinedMetricDefinition` and `MetricPollInfo.AggregatedMetrics` exist, `SnmpSource.Synthetic` is defined, and `OidResolutionBehavior` has its bypass guard.

The core challenge is that `DispatchResponseAsync` currently receives `IList<Variable> response` but does not build an OID-keyed dictionary from it. For aggregate dispatch, the job needs fast OID→value lookup from the same response. The solution is to build a `Dictionary<string, ISnmpData>` from the response before or during the varbind loop, then use it for aggregate computation.

The computation rules are locked by STATE.md decisions: `Subtract`/`AbsDiff` → `SnmpType.Integer32`, `Sum`/`Mean` → `SnmpType.Gauge32`. Numeric eligibility check uses the same TypeCode switch as `ValueExtractionBehavior` (Integer32, Gauge32, TimeTicks, Counter32, Counter64). Any failure = Warning log + skip for that cycle. Exceptions in the aggregate block are caught and logged — they do NOT trigger `snmp_poll_unreachable_total`.

**Primary recommendation:** Add a private `ComputeAndDispatchAggregatesAsync` method called from `DispatchResponseAsync` after the foreach loop, wrapped in try/catch. Build the OID→value dictionary inline. Keep the aggregate block completely isolated from the unreachability tracker.

## Standard Stack

### Core (already present — no new dependencies)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| MediatR | existing | `ISender.Send` for synthetic message dispatch | Already wired; behaviors run automatically |
| SharpSnmpLib | existing | `ISnmpData`, `Integer32`, `Gauge32` type wrappers | Required to construct synthetic Value field |
| Microsoft.Extensions.Diagnostics.Metrics | existing | `Counter<long>` for `snmp.combined.computed` | Same pattern as all other pipeline counters |

### No New NuGet Packages

Phase 40 is purely behavioral — no new dependencies.

## Architecture Patterns

### Current `DispatchResponseAsync` Structure

```csharp
private async Task DispatchResponseAsync(
    IList<Variable> response,
    DeviceInfo device,
    double pollDurationMs,
    CancellationToken ct)
{
    foreach (var variable in response)
    {
        // skip NoSuchObject / NoSuchInstance / EndOfMibView
        // construct SnmpOidReceived { Source = SnmpSource.Poll, ... }
        // await _sender.Send(msg, ct)
    }
    // ← aggregate dispatch goes here (after the foreach)
}
```

### Pattern 1: OID-to-Value Dictionary

The response is `IList<Variable>`. Each `Variable` has `.Id.ToString()` (OID string) and `.Data` (`ISnmpData`). Build the dictionary after skipping error sentinels:

```csharp
var oidValues = response
    .Where(v => v.Data.TypeCode is not SnmpType.NoSuchObject
                               and not SnmpType.NoSuchInstance
                               and not SnmpType.EndOfMibView)
    .ToDictionary(v => v.Id.ToString(), v => v.Data);
```

The foreach loop can skip and also populate the dictionary, but building it separately (or before) is cleaner and avoids dual-purpose logic.

**Alternative:** Build the dictionary inside the foreach loop alongside dispatch. Both work — building first is marginally simpler.

### Pattern 2: Numeric Eligibility Check

Use the same TypeCode set as `ValueExtractionBehavior` to determine if an input is numeric (snmp_gauge eligible):

```csharp
private static bool IsNumeric(SnmpType typeCode) => typeCode is
    SnmpType.Integer32 or
    SnmpType.Gauge32 or
    SnmpType.TimeTicks or
    SnmpType.Counter32 or
    SnmpType.Counter64;
```

Per CM-09: if any `SourceOid` is absent from `oidValues` OR its TypeCode is not numeric → skip this combined metric with Warning log.

### Pattern 3: Value Extraction for Aggregation

The aggregate computation needs raw `double` values. Apply the same extraction logic as `ValueExtractionBehavior`:

```csharp
private static double ExtractNumericValue(ISnmpData data) => data.TypeCode switch
{
    SnmpType.Integer32  => ((Integer32)data).ToInt32(),
    SnmpType.Gauge32    => ((Gauge32)data).ToUInt32(),
    SnmpType.TimeTicks  => ((TimeTicks)data).ToUInt32(),
    SnmpType.Counter32  => ((Counter32)data).ToUInt32(),
    SnmpType.Counter64  => (double)((Counter64)data).ToUInt64(),
    _                   => throw new InvalidOperationException($"Non-numeric TypeCode {data.TypeCode}")
};
```

This is a private static helper — it should only be called after `IsNumeric` passes, so the `_` branch should be unreachable.

### Pattern 4: Aggregation Computation

All four operations per STATE.md decisions:

```csharp
private static double Compute(AggregationKind kind, IReadOnlyList<double> values) => kind switch
{
    AggregationKind.Sum      => values.Sum(),
    AggregationKind.Subtract => values.Skip(1).Aggregate(values[0], (acc, v) => acc - v),
    AggregationKind.AbsDiff  => Math.Abs(values.Skip(1).Aggregate(values[0], (acc, v) => acc - v)),
    AggregationKind.Mean     => values.Sum() / values.Count,
    _                        => throw new InvalidOperationException($"Unknown AggregationKind {kind}")
};
```

- `Subtract`: sequential subtraction, left-to-right (m1 - m2 - m3). `Aggregate` starting with `values[0]` subtracted by each remaining value.
- `AbsDiff`: same as Subtract but `Math.Abs()` wraps the result.
- `Mean`: double division — `values.Sum() / values.Count` (both are `double` / `int`, C# promotes to double automatically). No integer truncation.

### Pattern 5: TypeCode Selection for Synthetic Message

Per STATE.md decisions:
- `Subtract` / `AbsDiff` → `SnmpType.Integer32` (signed, result can be negative)
- `Sum` / `Mean` → `SnmpType.Gauge32` (unsigned)

```csharp
private static SnmpType SelectTypeCode(AggregationKind kind) => kind switch
{
    AggregationKind.Subtract or AggregationKind.AbsDiff => SnmpType.Integer32,
    AggregationKind.Sum      or AggregationKind.Mean    => SnmpType.Gauge32,
    _ => SnmpType.Gauge32 // safe default
};
```

### Pattern 6: Constructing the Synthetic `SnmpOidReceived`

```csharp
var syntheticMsg = new SnmpOidReceived
{
    Oid         = "0.0",                    // sentinel OID (passes ValidationBehavior regex)
    AgentIp     = IPAddress.Parse(device.ResolvedIp),
    DeviceName  = device.Name,              // REQUIRED: ValidationBehavior runs before OidResolution
    Value       = /* Integer32 or Gauge32 wrapping the computed double */,
    Source      = SnmpSource.Synthetic,     // causes OidResolutionBehavior to bypass
    TypeCode    = selectTypeCode,           // Integer32 or Gauge32
    MetricName  = combined.MetricName,      // pre-set: preserved through OidResolutionBehavior bypass
    PollDurationMs = null                   // no round-trip for synthetic metrics
};
```

**Critical:** `DeviceName` must be set at construction because `ValidationBehavior` runs before `OidResolutionBehavior` in the pipeline order (Logging → Exception → Validation → OidResolution). A null `DeviceName` causes `ValidationBehavior` to reject with `"MissingDeviceName"`.

**Critical:** `MetricName` must be set at construction and equals `combined.MetricName` — the `OidResolutionBehavior` bypass guard skips `_oidMapService.Resolve` for Synthetic source, so `MetricName` is preserved exactly as set.

**Value construction** — the `Value` field holds an `ISnmpData` wrapper. For `Integer32`:
```csharp
Value = new Integer32((int)Math.Clamp(result, int.MinValue, int.MaxValue))
```
For `Gauge32`:
```csharp
Value = new Gauge32((uint)Math.Clamp(result, 0, uint.MaxValue))
```
Both clamps handle edge cases where `double` computation overflows the integer range. Whether to clamp or let it overflow naturally is a discretionary choice — clamping is safer.

**Note:** `ValueExtractionBehavior` will re-extract `ExtractedValue` from the `Value` field as the message flows through the pipeline. Setting `ExtractedValue` directly on the synthetic message is NOT needed (and would be overwritten anyway). The pipeline handles extraction automatically.

### Pattern 7: Exception Isolation

The aggregate block is wrapped in `try/catch(Exception)`. Per the phase_context: exceptions do NOT increment `snmp_poll_unreachable_total`. Only log at Warning/Error and continue:

```csharp
try
{
    await ComputeAndDispatchAggregatesAsync(oidValues, device, ct);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Aggregate dispatch failed for {DeviceName} poll group {PollIndex}",
        device.Name, pollGroup.PollIndex);
    // does NOT call RecordFailure(device.Name, device)
}
```

### Pattern 8: Adding `snmp.combined.computed` Counter

**In `TelemetryConstants.cs`:** No new constant needed — the counter name is embedded in `PipelineMetricService`.

**In `PipelineMetricService.cs`:**

1. Add private field: `private readonly Counter<long> _combinedComputed;`
2. Initialize in constructor: `_combinedComputed = _meter.CreateCounter<long>("snmp.combined.computed");`
3. Add public method:
```csharp
public void IncrementCombinedComputed(string deviceName)
    => _combinedComputed.Add(1, new TagList { { "device_name", deviceName } });
```

Pattern is identical to all other counters (`IncrementPollExecuted`, `IncrementTenantVectorRouted`, etc.).

**Counter increments:** Once per successfully computed and dispatched combined metric (CM-13). Only call after `_sender.Send` succeeds. Does NOT increment on skip (CM-14 covers warning log for skips only).

### Pattern 9: `pollGroup` Access in `DispatchResponseAsync`

Currently `DispatchResponseAsync` does not receive `pollGroup` — it only receives `response`, `device`, `pollDurationMs`, `ct`. To access `pollGroup.AggregatedMetrics`, either:

**Option A:** Pass `pollGroup` as a parameter to `DispatchResponseAsync`
**Option B:** Inline the aggregate block in `Execute` after calling `DispatchResponseAsync`, passing the response dictionary

Option A is cleaner — add `MetricPollInfo pollGroup` parameter to `DispatchResponseAsync`. The call site in `Execute` already has `pollGroup` in scope.

### Pattern 10: Where Exactly the Aggregate Block Lives

`Execute` calls `DispatchResponseAsync(response, device, sw.Elapsed.TotalMilliseconds, ct)`. The aggregate block runs AFTER that call, STILL INSIDE the `try` block in `Execute`. But it should be isolated from the `RecordFailure`/unreachability path.

Best structure: call `DispatchResponseAsync` (dispatches individual varbinds), then call a separate `DispatchAggregatesAsync` with different exception handling. The `DispatchAggregatesAsync` exception handler logs but does NOT call `RecordFailure`.

Alternatively, extend `DispatchResponseAsync` to accept `pollGroup` and handle aggregates at the end of that method — this keeps all dispatch logic together.

**Recommendation:** Extend `DispatchResponseAsync` with a `pollGroup` parameter. Aggregate block at the end, with its own internal `try/catch`. This avoids splitting dispatch logic across two call sites in `Execute`.

### Anti-Patterns to Avoid

- **Setting `ExtractedValue` directly on synthetic message:** `ValueExtractionBehavior` runs in pipeline and re-extracts from `Value` anyway. Pre-setting would be overwritten and causes confusion.
- **Using `SnmpSource.Poll` for synthetic messages:** `OidResolutionBehavior` would then call `_oidMapService.Resolve("0.0")` and overwrite the pre-set `MetricName`.
- **Not setting `DeviceName` on synthetic message:** `ValidationBehavior` rejects with `"MissingDeviceName"`.
- **Integer division for Mean:** `values.Sum() / values.Count` — both operands must be `double`. If `values` is `IReadOnlyList<double>`, `Sum()` returns `double` and `Count` is `int`, so C# promotes to `double`. Safe.
- **Calling `RecordFailure` on aggregate computation exception:** Per phase_context, aggregate exceptions do NOT increment `snmp_poll_unreachable_total`. Isolated try/catch with logging only.
- **Skipping AggregatedMetrics when list is empty:** `IReadOnlyList<CombinedMetricDefinition>` defaults to `[]` on `MetricPollInfo`. An empty list means no aggregate work — the `foreach` naturally handles this.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| OID→value lookup | Custom data structure | `Dictionary<string, ISnmpData>` built from response | Simple, O(1) lookup, one LINQ statement |
| Numeric type check | String comparison on type name | `TypeCode is SnmpType.X or ...` switch | Same pattern as ValueExtractionBehavior; compile-time exhaustive |
| Sequential subtraction | Custom loop | `values.Skip(1).Aggregate(values[0], (acc, v) => acc - v)` | Correct left-fold semantics, concise |
| Counter registration | Manual Meter access | `PipelineMetricService` pattern (field + constructor + method) | Consistent with all 11 existing counters |

## Common Pitfalls

### Pitfall 1: Missing DeviceName on Synthetic Message
**What goes wrong:** `ValidationBehavior` rejects the message with `"MissingDeviceName"`, increments `snmp.event.rejected`, and returns `default!` — the synthetic metric is never recorded.
**Why it happens:** `DeviceName` is nullable on `SnmpOidReceived`. Poll messages set it from `device.Name`. Synthetic messages must also set it explicitly.
**How to avoid:** Always set `DeviceName = device.Name` when constructing synthetic `SnmpOidReceived`.

### Pitfall 2: MetricName Overwritten by OidResolutionBehavior
**What goes wrong:** `OidResolutionBehavior` calls `_oidMapService.Resolve("0.0")` and overwrites `MetricName` with `"Unknown"`.
**Why it happens:** Using `Source = SnmpSource.Poll` instead of `SnmpSource.Synthetic` on the synthetic message.
**How to avoid:** Always use `Source = SnmpSource.Synthetic`. The Phase 39 bypass guard only fires on this source value.

### Pitfall 3: Aggregate Exception Triggers Unreachability
**What goes wrong:** An exception during aggregate computation calls `RecordFailure`, eventually transitioning the device to unreachable and logging a spurious warning.
**Why it happens:** Reusing the outer catch block structure from `Execute`.
**How to avoid:** Use a separate try/catch for the aggregate block that only logs — no `RecordFailure` call.

### Pitfall 4: Non-Numeric Input Not Caught
**What goes wrong:** An OID returns `OctetString` (snmp_info type) and is passed to `ExtractNumericValue`, causing a cast exception.
**Why it happens:** Forgetting to call `IsNumeric(data.TypeCode)` before extraction.
**How to avoid:** CM-09: check `IsNumeric` for each source OID's data. Any non-numeric → skip combined metric with Warning log.

### Pitfall 5: Missing OID in Response Not Caught
**What goes wrong:** A `SourceOid` isn't in `oidValues` (device didn't respond for that OID), `Dictionary` throws `KeyNotFoundException`.
**Why it happens:** Using `oidValues[oid]` without `TryGetValue`.
**How to avoid:** Use `oidValues.TryGetValue(oid, out var data)`. If any OID is absent → skip combined metric with Warning.

### Pitfall 6: Gauge32 Wrapping for Negative AbsDiff Result
**What goes wrong:** `AggregationKind.AbsDiff` with `SnmpType.Integer32` TypeCode — but `Math.Abs` guarantees non-negative, so the result is always ≥ 0. However `Subtract` can produce a negative value, and casting a negative `double` to `uint` for `Gauge32` wraps around.
**Why it happens:** Using `Gauge32` for `Subtract`/`AbsDiff` instead of `Integer32`.
**How to avoid:** Per STATE.md: `Subtract`/`AbsDiff` → `Integer32`. This is a locked decision. Use `SelectTypeCode` helper.

### Pitfall 7: `pollGroup` Not Available in `DispatchResponseAsync`
**What goes wrong:** `DispatchResponseAsync` currently takes `response`, `device`, `pollDurationMs`, `ct` — no `pollGroup`. `AggregatedMetrics` is on `pollGroup`.
**Why it happens:** Forgetting to update the method signature.
**How to avoid:** Add `MetricPollInfo pollGroup` parameter to `DispatchResponseAsync` at the call site in `Execute` (where `pollGroup` is already in scope).

## Code Examples

### Inserting Aggregate Block into DispatchResponseAsync

```csharp
private async Task DispatchResponseAsync(
    IList<Variable> response,
    DeviceInfo device,
    MetricPollInfo pollGroup,        // ← ADD THIS PARAMETER
    double pollDurationMs,
    CancellationToken ct)
{
    // Build OID→value dictionary (excluding error sentinels)
    var oidValues = response
        .Where(v => v.Data.TypeCode is not SnmpType.NoSuchObject
                                   and not SnmpType.NoSuchInstance
                                   and not SnmpType.EndOfMibView)
        .ToDictionary(v => v.Id.ToString(), v => v.Data);

    // Existing varbind dispatch loop (unchanged)
    foreach (var (oid, data) in oidValues)
    {
        var msg = new SnmpOidReceived { /* ... Source = SnmpSource.Poll ... */ };
        await _sender.Send(msg, ct);
    }

    // Aggregate dispatch (new)
    foreach (var combined in pollGroup.AggregatedMetrics)
    {
        try
        {
            await DispatchCombinedMetricAsync(combined, oidValues, device, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Combined metric {MetricName} dispatch failed for {DeviceName} poll group {PollIndex}",
                combined.MetricName, device.Name, pollGroup.PollIndex);
        }
    }
}
```

### DispatchCombinedMetricAsync

```csharp
private async Task DispatchCombinedMetricAsync(
    CombinedMetricDefinition combined,
    Dictionary<string, ISnmpData> oidValues,
    DeviceInfo device,
    CancellationToken ct)
{
    // CM-09: all source OIDs must be present and numeric
    var values = new List<double>(combined.SourceOids.Count);
    foreach (var oid in combined.SourceOids)
    {
        if (!oidValues.TryGetValue(oid, out var data))
        {
            _logger.LogWarning(
                "Combined metric {MetricName} skipped: OID {Oid} absent from response for {DeviceName}",
                combined.MetricName, oid, device.Name);
            return;
        }
        if (!IsNumeric(data.TypeCode))
        {
            _logger.LogWarning(
                "Combined metric {MetricName} skipped: OID {Oid} is non-numeric ({TypeCode}) for {DeviceName}",
                combined.MetricName, oid, data.TypeCode, device.Name);
            return;
        }
        values.Add(ExtractNumericValue(data));
    }

    // CM-08: compute aggregate
    var result = Compute(combined.Kind, values);
    var typeCode = SelectTypeCode(combined.Kind);

    // Construct ISnmpData Value wrapper
    ISnmpData value = typeCode == SnmpType.Integer32
        ? new Integer32((int)Math.Clamp(result, int.MinValue, int.MaxValue))
        : new Gauge32((uint)Math.Clamp(result, uint.MinValue, uint.MaxValue));

    // CM-07, CM-10: dispatch through full MediatR pipeline
    var syntheticMsg = new SnmpOidReceived
    {
        Oid        = "0.0",
        AgentIp    = IPAddress.Parse(device.ResolvedIp),
        DeviceName = device.Name,
        Value      = value,
        Source     = SnmpSource.Synthetic,
        TypeCode   = typeCode,
        MetricName = combined.MetricName,
        PollDurationMs = null
    };

    await _sender.Send(syntheticMsg, ct);

    // CM-13: increment counter on successful dispatch
    _pipelineMetrics.IncrementCombinedComputed(device.Name);
}
```

### PipelineMetricService Addition

```csharp
// In field declarations:
private readonly Counter<long> _combinedComputed;

// In constructor (after _tenantVectorRouted):
_combinedComputed = _meter.CreateCounter<long>("snmp.combined.computed");

// New public method:
/// <summary>CM-13: Increment count of successfully computed combined metrics by 1.</summary>
public void IncrementCombinedComputed(string deviceName)
    => _combinedComputed.Add(1, new TagList { { "device_name", deviceName } });
```

### Test Pattern for Aggregate Dispatch

Following `MetricPollJobTests` patterns:

```csharp
[Fact]
public async Task Execute_WithAggregatedMetrics_Sum_DispatchesSyntheticMessage()
{
    // Arrange
    var combined = new CombinedMetricDefinition("obp_combined_power", AggregationKind.Sum,
        new[] { IfInOctetsOid, IfOutOctetsOid });
    var pollGroup = new MetricPollInfo(0, [IfInOctetsOid, IfOutOctetsOid], 30)
    {
        AggregatedMetrics = new[] { combined }
    };
    var device = new DeviceInfo(DeviceName, DeviceIp, DeviceIp, DevicePort, [pollGroup], $"Simetra.{DeviceName}");

    var response = new List<Variable>
    {
        new(new ObjectIdentifier(IfInOctetsOid),  new Gauge32(1000)),
        new(new ObjectIdentifier(IfOutOctetsOid), new Gauge32(2000)),
    };

    var snmpClient = new StubSnmpClient { Response = response };
    var sender = new CapturingSender();
    var job = CreateJob(registry: new StubDeviceRegistry([device]), snmpClient: snmpClient, sender: sender);

    // Act
    await job.Execute(MakeContext());

    // Assert: 2 individual varbinds + 1 synthetic
    Assert.Equal(3, sender.Sent.Count);
    var synthetic = sender.Sent.Single(m => m.Source == SnmpSource.Synthetic);
    Assert.Equal("obp_combined_power", synthetic.MetricName);
    Assert.Equal("0.0", synthetic.Oid);
    Assert.Equal(SnmpType.Gauge32, synthetic.TypeCode);
    Assert.Equal(DeviceName, synthetic.DeviceName);
}
```

## Test Coverage Required

Per the test patterns in `MetricPollJobTests`, these scenarios need unit tests:

1. **Sum aggregation** — 2 Gauge32 inputs, expects Gauge32 synthetic with sum value
2. **Subtract aggregation** — 2 inputs, expects Integer32 synthetic with m1-m2
3. **AbsDiff aggregation** — 2 inputs where m2 > m1, expects Integer32 synthetic with |m1-m2| (positive)
4. **Mean aggregation** — 3 inputs, expects Gauge32 synthetic with double mean (not truncated)
5. **Skip on missing OID** — response omits one source OID, no synthetic dispatched, Warning logged
6. **Skip on non-numeric input** — one source OID returns OctetString, no synthetic dispatched, Warning logged
7. **Multiple CombinedMetricDefinitions** — poll group has 2 aggregates, both synthetic messages dispatched
8. **Empty AggregatedMetrics** — no aggregates configured, no synthetic dispatched (baseline regression)
9. **snmp.combined.computed counter** — verify counter increments on successful dispatch
10. **Exception in aggregate block does not trigger unreachability** — aggregate throws, device NOT recorded as failed

Tests use `CapturingSender` and `StubSnmpClient` — same infrastructure as existing `MetricPollJobTests`. The `NonParallelCollection` attribute is required for meter listener tests.

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| MetricPollJob dispatches individual varbinds only | Phase 40 adds post-dispatch aggregate computation | Closes v1.8 Combined Metrics feature |
| `DispatchResponseAsync` takes 4 params | Will take 5 params (add `pollGroup`) | Update call site in `Execute` |
| `PipelineMetricService` has 11 counters | Phase 40 adds `snmp.combined.computed` = 12 counters | Update constructor + add method |

## Open Questions

1. **Value clamping strategy**
   - What we know: `double` result may exceed `int` or `uint` range for large counter values
   - What's unclear: Should we clamp silently or log a Warning when clamping occurs?
   - Recommendation: Silent clamp (no warning) — overflow in aggregated counter values is a data issue, not a pipeline error; logging would be noisy

2. **`DispatchResponseAsync` refactor scope**
   - What we know: Adding `pollGroup` parameter changes the method signature
   - What's unclear: Whether to also refactor the foreach body to use `oidValues` dictionary (vs. iterating `response` directly as now)
   - Recommendation: Refactor the foreach to iterate `oidValues.Keys` rather than `response` directly — this consolidates the sentinel-skipping logic in one place (the `.Where()` filter building the dictionary)

3. **Poll group index in warning logs**
   - What we know: CM-14 requires Warning log with "poll group index"
   - What's unclear: `DispatchCombinedMetricAsync` currently doesn't receive `pollGroup` directly — only `combined` and `oidValues`
   - Recommendation: Pass `pollGroup.PollIndex` as a separate parameter (int) to `DispatchCombinedMetricAsync`, or pass the whole `pollGroup`

## Sources

### Primary (HIGH confidence)
- Direct codebase read: `MetricPollJob.cs` — current structure, parameter list, exception handling
- Direct codebase read: `SnmpOidReceived.cs` — all fields, required vs optional, `ExtractedValue` behavior
- Direct codebase read: `CombinedMetricDefinition.cs`, `MetricPollInfo.cs` — AggregatedMetrics structure
- Direct codebase read: `ValueExtractionBehavior.cs` — numeric TypeCode set, extraction patterns
- Direct codebase read: `OidResolutionBehavior.cs` — confirmed bypass guard already in place (Phase 39 complete)
- Direct codebase read: `ValidationBehavior.cs` — confirmed DeviceName check, OID regex
- Direct codebase read: `PipelineMetricService.cs` — counter registration pattern (12 counters → 13)
- Direct codebase read: `OtelMetricHandler.cs` — confirms TypeCode routing to snmp_gauge vs snmp_info
- Direct codebase read: `SnmpSource.cs` — confirmed `Synthetic` member exists
- Direct codebase read: `.planning/REQUIREMENTS.md` — CM-07 through CM-15 full text
- Direct codebase read: `.planning/STATE.md` — locked decisions (TypeCode selection, sentinel OID, computation semantics)
- Direct codebase read: `MetricPollJobTests.cs` — test infrastructure (CapturingSender, StubSnmpClient, StubDeviceRegistry patterns)

### Secondary (MEDIUM confidence)
- N/A — all findings from codebase, no external sources needed

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new libraries; existing stack fully understood
- Architecture: HIGH — all integration points verified in source code
- Computation semantics: HIGH — locked in STATE.md, derived from requirements
- Pitfalls: HIGH — derived from reading validation, pipeline, and handler code
- Test coverage: HIGH — exact test infrastructure available in MetricPollJobTests.cs

**Research date:** 2026-03-15
**Valid until:** Indefinite (pure codebase research, no external library versions)
