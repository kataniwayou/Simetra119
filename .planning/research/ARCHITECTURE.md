# Architecture Patterns: Combined / Synthetic Metrics

**Domain:** SNMP monitoring agent -- combined metrics via synthetic SnmpOidReceived dispatch
**Researched:** 2026-03-15
**Confidence:** HIGH (all findings based on direct codebase read; no training-data speculation)

---

## Context: What "Combined Metric" Means Here

A combined metric aggregates two or more raw SNMP OID values (already fetched in one multi-GET)
into a single synthetic value, then records it as a named gauge through the same OTel pipeline
that records individual OIDs. The canonical example is computing a ratio or sum from values that
arrived in the same varbind list.

The dispatch model is: after iterating the real varbinds in `DispatchResponseAsync`, compute the
aggregate in `MetricPollJob` and dispatch one additional `SnmpOidReceived` whose `MetricName`
is already populated and whose `Oid` is a sentinel (not a real SNMP OID).

---

## Existing Pipeline (Baseline)

Full behavior chain for every `SnmpOidReceived`:

```
ISender.Send(SnmpOidReceived)
  1. LoggingBehavior         -- increments snmp.event.published; logs Oid/Agent/Source at Debug
  2. ExceptionBehavior       -- wraps next() in try/catch; prevents unhandled exceptions leaking
  3. ValidationBehavior      -- rejects bad OID format; rejects null DeviceName; short-circuits on fail
  4. OidResolutionBehavior   -- msg.MetricName = _oidMapService.Resolve(msg.Oid)   (overwrites)
  5. ValueExtractionBehavior -- sets msg.ExtractedValue / msg.ExtractedStringValue from TypeCode
  6. TenantVectorFanOutBehavior -- routes to MetricSlotHolder(s) by (ip, port, metricName)
  7. OtelMetricHandler       -- records snmp_gauge or snmp_info; calls RecordGauge / RecordInfo
```

Key properties of this chain:

- Every behavior checks `if (notification is SnmpOidReceived msg)` and is a no-op for other types.
- `OidResolutionBehavior` **overwrites** `msg.MetricName` unconditionally:
  `msg.MetricName = _oidMapService.Resolve(msg.Oid)` -- there is no guard on whether
  `MetricName` is already set.
- `ValidationBehavior` rejects any `Oid` that does not match `^\d+(\.\d+){1,}$`. A sentinel OID
  like `"synthetic"` or an empty string would be rejected here.
- `OtelMetricHandler` reads `msg.MetricName ?? OidMapService.Unknown` -- it does not require that
  the name was set by OID resolution specifically.

---

## Integration Point 1: Where Aggregation Happens in MetricPollJob

`MetricPollJob.Execute` calls `DispatchResponseAsync(response, device, sw.Elapsed.TotalMilliseconds, ct)`.
`DispatchResponseAsync` iterates the varbind list, skips error sentinels, and dispatches each real
varbind individually.

**Placement: after the `DispatchResponseAsync` call, before the success/failure tracking.**

```
await DispatchResponseAsync(response, device, sw.Elapsed.TotalMilliseconds, ct);
// NEW: compute and dispatch synthetic metrics
await DispatchCombinedMetricsAsync(response, device, pollGroup, ct);   // new private method
// (success path continues)
if (_unreachabilityTracker.RecordSuccess(device.Name)) { ... }
```

This placement is correct for two reasons:

1. The individual OID values are in `response` (already fetched). The combined metric reads from
   the same `IList<Variable>` — no second SNMP call needed.
2. `DispatchResponseAsync` does not need to know about combined metrics. Keeping aggregation in a
   separate method maintains single-responsibility and keeps the combined-metric path unit-testable
   in isolation.

`pollGroup` (a `MetricPollInfo`) must carry the combined metric definitions. This requires
extending `MetricPollInfo` (see Integration Point 4 below).

`DispatchCombinedMetricsAsync` is a new private method on `MetricPollJob`. It:
- Receives the raw varbind list and the `MetricPollInfo` (which now knows which OIDs combine
  into which output metric name)
- Extracts numeric values for the participating OIDs
- Computes the aggregate
- Dispatches one `SnmpOidReceived` per combined metric

---

## Integration Point 2: Bypassing OidResolutionBehavior

`OidResolutionBehavior` unconditionally overwrites `MetricName`:

```csharp
msg.MetricName = _oidMapService.Resolve(msg.Oid);
```

If the synthetic message carries a sentinel `Oid` (e.g. `"synthetic"` or a generated string like
`"0.0"`), `_oidMapService.Resolve` will return `OidMapService.Unknown`, which overwrites the
pre-set `MetricName` and causes the metric to be recorded under the unknown name. This is the
core integration problem.

**Two viable mechanisms:**

### Option A: Guard in OidResolutionBehavior (recommended)

Add a one-line guard: if `msg.MetricName` is already set (non-null, non-Unknown), skip resolution
and call `next()` directly.

```csharp
if (notification is SnmpOidReceived msg)
{
    // Synthetic messages pre-populate MetricName; skip OID lookup to avoid overwrite.
    if (msg.MetricName is null || msg.MetricName == OidMapService.Unknown)
    {
        msg.MetricName = _oidMapService.Resolve(msg.Oid);
        if (msg.MetricName == OidMapService.Unknown)
            _logger.LogDebug("OID {Oid} not found in OidMap", msg.Oid);
        else
            _logger.LogDebug("OID {Oid} resolved to {MetricName}", msg.Oid, msg.MetricName);
    }
    // else: MetricName already set by caller (synthetic path); no log needed at this stage
}
return await next();
```

This change is backward-compatible: all existing real varbind dispatches arrive with
`MetricName = null` (it is not set in `DispatchResponseAsync`), so the guard does not fire for
the normal path.

### Option B: New SnmpSource value

Add `SnmpSource.Synthetic` to the `SnmpSource` enum and make `OidResolutionBehavior` skip
resolution when `msg.Source == SnmpSource.Synthetic`.

```csharp
if (notification is SnmpOidReceived msg && msg.Source != SnmpSource.Synthetic)
{
    msg.MetricName = _oidMapService.Resolve(msg.Oid);
    ...
}
```

This is less intrusive to the resolution behavior itself, but ties the bypass to the `Source`
discriminator rather than the actual semantic state of `MetricName`. If a future code path sets
`Source = Synthetic` for a message that still needs OID resolution, the guard would silently
skip it. Option A is more defensively correct.

**Recommendation: Option A.** The guard is on the actual state (`MetricName` already set), not
on a discriminator that could be misused.

**Sentinel OID requirement:** The synthetic `SnmpOidReceived` still needs a non-empty, syntactically
valid `Oid` value because `ValidationBehavior` requires `^\d+(\.\d+){1,}$`. Use a stable synthetic
OID like `"0.0"` (two-arc numeric, never assigned by IANA, clearly synthetic). Do not use a string
like `"synthetic"` -- it will be rejected by `ValidationBehavior`.

---

## Integration Point 3: Do Existing Behaviors Handle Synthetic Messages?

Walk through each behavior for a synthetic `SnmpOidReceived` with:
- `Oid = "0.0"` (sentinel)
- `MetricName = "npb_combined_throughput"` (pre-set)
- `TypeCode = SnmpType.Gauge32` (or Integer32, depending on aggregation result)
- `Source = SnmpSource.Poll`
- `ExtractedValue` left at default (0.0) -- ValueExtractionBehavior will re-extract from `Value`
- `Value` = a `Gauge32` wrapping the computed aggregate value

| Behavior | Synthetic path behavior | Change needed? |
|----------|------------------------|----------------|
| `LoggingBehavior` | Logs `Oid=0.0 Agent=... Source=Poll`; increments published counter | None. Works correctly. |
| `ExceptionBehavior` | Wraps in try/catch; no opinion on content | None. |
| `ValidationBehavior` | `"0.0"` passes regex `^\d+(\.\d+){1,}$`; `DeviceName` is set | None -- **but OID sentinel must be `"0.0"` or similar numeric form.** |
| `OidResolutionBehavior` | With Option A guard: sees `MetricName` already set, skips lookup, calls `next()` | Yes -- add the guard (one-line change). |
| `ValueExtractionBehavior` | Reads `msg.TypeCode` and extracts from `msg.Value`; works on any numeric TypeCode | None. Caller must set `TypeCode` and `Value` to carry the computed double. |
| `TenantVectorFanOutBehavior` | Routes using `msg.MetricName`, `msg.AgentIp`, device port; same as real metric | None. Synthetic metric with a registered tenant slot will route correctly. |
| `OtelMetricHandler` | `switch (notification.TypeCode)` dispatches to `RecordGauge`; uses `msg.MetricName` | None. Works as-is, records under the pre-set MetricName. |

**Summary: only `OidResolutionBehavior` needs a one-line change. No new behavior is required.**

The existing pipeline handles synthetic messages completely if the sentinel OID is numeric and the
bypass guard is added to `OidResolutionBehavior`.

**Packaging the computed double into `ISnmpData`:**

`ValueExtractionBehavior` reads from `msg.Value` (an `ISnmpData`), not from `msg.ExtractedValue`
directly. For synthetic messages, the caller in `MetricPollJob` must wrap the computed value in a
concrete `ISnmpData` implementation so `ValueExtractionBehavior` can extract it correctly.

For a Gauge32 result, use `new Gauge32(checked((uint)computedValue))`.
For an Integer32 result, use `new Integer32(checked((int)computedValue))`.

The caller sets `TypeCode` to match. `ValueExtractionBehavior` will then set `ExtractedValue`
from the wrapped value, and `OtelMetricHandler` will call `RecordGauge` with the correct value.

Alternatively, the caller can set `ExtractedValue` directly AND set a matching `TypeCode` on a
trivially-typed `Value`. Because `OtelMetricHandler` reads `notification.ExtractedValue` (set by
`ValueExtractionBehavior`), the two approaches are equivalent as long as both `Value` and
`TypeCode` are consistent. Using a real `Gauge32` wrapper is cleaner and avoids type/value
divergence.

---

## Integration Point 4: PollOptions and MetricPollInfo Changes

Combined metrics must be defined in config so `MetricPollJob` knows which OIDs to aggregate and
what to call the result. The config → runtime path is:

```
PollOptions (config model)
    -> BuildPollGroups() in DeviceWatcherService
        -> MetricPollInfo (runtime record passed to Quartz job via JobDataMap or DeviceInfo)
```

### PollOptions change

Add a new list to `PollOptions`:

```csharp
/// <summary>
/// Zero or more combined metric definitions for this poll group.
/// Each definition names the output metric and the source OID metric names whose values combine.
/// </summary>
public List<CombinedMetricOptions> CombinedMetrics { get; set; } = [];
```

`CombinedMetricOptions` is a new config model:

```csharp
public sealed class CombinedMetricOptions
{
    /// <summary>Output metric name (e.g. "npb_combined_throughput"). Must be unique within poll group.</summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>Aggregation function: Sum, Ratio, Difference, Max, Min.</summary>
    public string Aggregation { get; set; } = string.Empty;

    /// <summary>Ordered list of source metric names (resolved to OIDs at load time).</summary>
    public List<string> SourceMetricNames { get; set; } = [];
}
```

This is additive and backward-compatible: existing `PollOptions` without `CombinedMetrics` will
deserialize with an empty list.

### MetricPollInfo change

`MetricPollInfo` is a positional record. Add a new property:

```csharp
public sealed record MetricPollInfo(
    int PollIndex,
    IReadOnlyList<string> Oids,
    int IntervalSeconds,
    double TimeoutMultiplier = 0.8,
    IReadOnlyList<CombinedMetricDefinition> CombinedMetrics = default!)   // NEW
```

`CombinedMetricDefinition` is a new runtime record (parallel to `CombinedMetricOptions`):

```csharp
/// <summary>
/// Resolved combined metric definition carried by MetricPollInfo at runtime.
/// SourceOids are the resolved OID strings whose values feed the aggregation.
/// </summary>
public sealed record CombinedMetricDefinition(
    string MetricName,
    AggregationKind Aggregation,
    IReadOnlyList<string> SourceOids);

public enum AggregationKind { Sum, Ratio, Difference, Max, Min }
```

### DeviceWatcherService.ValidateAndBuildDevicesAsync change

`BuildPollGroups` (a `private static` method in `DeviceWatcherService`) currently:

1. Iterates `poll.MetricNames`, resolves each to OID via `oidMapService.ResolveToOid(name)`.
2. Builds `MetricPollInfo(PollIndex, resolvedOids, IntervalSeconds, TimeoutMultiplier)`.

After the change, it must also:

3. Iterate `poll.CombinedMetrics`, resolve each `SourceMetricName` to OID, and build
   `CombinedMetricDefinition` instances.
4. Validate: each `SourceMetricName` must resolve; if any source OID is missing, log warning and
   skip that combined metric definition (do not skip the whole poll group).
5. Pass the built `IReadOnlyList<CombinedMetricDefinition>` as the new `MetricPollInfo` parameter.

The `ValidateAndBuildDevicesAsync` method signature does not change. The change is entirely
within `BuildPollGroups`.

**Validation rules for `CombinedMetricOptions`:**

- `MetricName` must be non-empty.
- `Aggregation` must parse to a known `AggregationKind`.
- `SourceMetricNames` must have at least 2 entries (combining one value with nothing is not a
  combination).
- For `Ratio`, exactly 2 source names are required (numerator / denominator).
- Each source name must resolve to an OID via `oidMapService.ResolveToOid`. Unresolvable sources
  cause a warning + skip of that combined metric definition (not the whole poll group).

These validation rules belong in `BuildPollGroups`, not in `DevicesOptionsValidator`, because they
require `IOidMapService` which is not available in the validator.

---

## Integration Point 5: Full Data Flow

```
simetra-devices ConfigMap
    key: devices.json
    |
    DeviceWatcherService.HandleConfigMapChangedAsync
    |  JsonDeserialize -> List<DeviceOptions>
    |      DeviceOptions.Polls[i].CombinedMetrics  <-- NEW LIST
    v
BuildPollGroups(polls, deviceName, oidMapService, logger)
    |  for each PollOptions poll:
    |      for each MetricName in poll.MetricNames:
    |          resolve to OID -> resolvedOids[]
    |      for each CombinedMetricOptions cm in poll.CombinedMetrics:   <-- NEW LOOP
    |          validate MetricName non-empty
    |          validate Aggregation parseable
    |          validate SourceMetricNames.Count >= 2
    |          for each SourceMetricName:
    |              oid = oidMapService.ResolveToOid(sourceName)
    |              if null: warn, skip this definition entirely
    |          if all sources resolved: emit CombinedMetricDefinition
    |      MetricPollInfo(PollIndex, resolvedOids, IntervalSeconds, TimeoutMultiplier,
    |                     combinedMetricDefinitions)       <-- NEW PARAMETER
    v
DeviceInfo.PollGroups[i] (MetricPollInfo with combined definitions)
    |
    DynamicPollScheduler.ReconcileAsync
    |  schedules Quartz metric-poll-{addr}_{port}-{pollIndex} job
    |  JobDataMap carries configAddress, port, pollIndex, intervalSeconds
    v
MetricPollJob.Execute (Quartz fires on interval)
    |  device = _deviceRegistry.TryGetByIpPort(configAddress, port)
    |  pollGroup = device.PollGroups[pollIndex]     -- MetricPollInfo with CombinedMetrics
    |
    |  SNMP GET: pollGroup.Oids -> IList<Variable> response
    |
    |  DispatchResponseAsync(response, device, ...)
    |      foreach varbind in response:
    |          if error sentinel: skip
    |          new SnmpOidReceived { Oid, AgentIp, DeviceName, Value, Source=Poll,
    |                                TypeCode, PollDurationMs }
    |          ISender.Send(msg)  -> full pipeline (OidResolutionBehavior sets MetricName)
    |
    |  DispatchCombinedMetricsAsync(response, device, pollGroup, ct)   <-- NEW
    |      Build value index: { oid -> double } from response varbinds
    |      foreach CombinedMetricDefinition cmd in pollGroup.CombinedMetrics:
    |          if any source OID missing from value index: log warning, skip this metric
    |          compute = Aggregate(cmd.Aggregation, sourceValues[])
    |          new SnmpOidReceived
    |          {
    |              Oid         = "0.0"               // sentinel; passes ValidationBehavior
    |              AgentIp     = IPAddress.Parse(device.ResolvedIp)
    |              DeviceName  = device.Name
    |              Value       = new Gauge32(checked((uint)compute))  // or Integer32
    |              Source      = SnmpSource.Poll
    |              TypeCode    = SnmpType.Gauge32
    |              MetricName  = cmd.MetricName      // PRE-SET; bypasses OidResolutionBehavior
    |              PollDurationMs = null             // aggregation has no separate round-trip
    |          }
    |          ISender.Send(syntheticMsg)
    |          |
    |          v  pipeline:
    |          LoggingBehavior          -- logs Oid=0.0; increments published
    |          ExceptionBehavior        -- wraps
    |          ValidationBehavior       -- "0.0" passes regex; DeviceName set -> passes
    |          OidResolutionBehavior    -- MetricName already set; GUARD fires, skips lookup
    |          ValueExtractionBehavior  -- extracts from Gauge32 Value -> ExtractedValue
    |          TenantVectorFanOutBehavior -- routes by (ip, port, cmd.MetricName)
    |          OtelMetricHandler        -- RecordGauge(cmd.MetricName, "0.0", deviceName, ...)
    v
Prometheus scrape reads snmp_gauge{metric_name="npb_combined_throughput", ...}
```

---

## Component Change Map

### New Files

| File | Kind | Purpose |
|------|------|---------|
| `Configuration/CombinedMetricOptions.cs` | Config model | `MetricName`, `Aggregation` (string), `SourceMetricNames` list |
| `Pipeline/CombinedMetricDefinition.cs` | Runtime record | Resolved combined metric: `MetricName`, `AggregationKind`, `SourceOids` |
| `Pipeline/AggregationKind.cs` | Enum | `Sum`, `Ratio`, `Difference`, `Max`, `Min` |

### Modified Files

| File | Change | Scope |
|------|--------|-------|
| `Configuration/PollOptions.cs` | Add `List<CombinedMetricOptions> CombinedMetrics { get; set; } = []` | 3 lines |
| `Pipeline/MetricPollInfo.cs` | Add `IReadOnlyList<CombinedMetricDefinition> CombinedMetrics` parameter to record | 2 lines + default |
| `Services/DeviceWatcherService.cs` | `BuildPollGroups`: add loop to resolve and validate combined metric definitions; pass to `MetricPollInfo` | ~30 lines in existing private static method |
| `Jobs/MetricPollJob.cs` | Add `DispatchCombinedMetricsAsync` private method; call it after `DispatchResponseAsync` | ~40 lines new method + 1 call site |
| `Pipeline/Behaviors/OidResolutionBehavior.cs` | Add guard: if `msg.MetricName` already set and not Unknown, skip lookup | 3 lines |

### Unchanged Files

| File | Reason unchanged |
|------|-----------------|
| `Pipeline/SnmpOidReceived.cs` | All required fields already exist; `MetricName` is already mutable (`set`) |
| `Pipeline/Behaviors/ValidationBehavior.cs` | Sentinel `"0.0"` satisfies existing OID regex |
| `Pipeline/Behaviors/ValueExtractionBehavior.cs` | Works on any TypeCode; no knowledge of synthetic path |
| `Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` | Routes by `MetricName`; works identically for synthetic and real metrics |
| `Pipeline/Handlers/OtelMetricHandler.cs` | Records by `MetricName` regardless of origin |
| `Pipeline/Behaviors/LoggingBehavior.cs` | No change |
| `Pipeline/Behaviors/ExceptionBehavior.cs` | No change |
| `Configuration/DeviceOptions.cs` | Gains `CombinedMetrics` only through `PollOptions.CombinedMetrics` (indirectly) |
| `Pipeline/DeviceInfo.cs` | `PollGroups` is `IReadOnlyList<MetricPollInfo>`; no change to shape |
| `Services/DynamicPollScheduler.cs` | Schedules jobs from `MetricPollInfo.JobKey`; does not need to know about combined metrics |
| `Pipeline/Telemetry/TelemetryConstants.cs` | Synthetic metrics use existing `snmp_gauge` instrument |

---

## Special Cases and Edge Conditions

### Ratio Aggregation and Division by Zero

`Ratio(a, b) = a / b`. If `b == 0`, the result is undefined. `DispatchCombinedMetricsAsync` must
check for zero denominator and skip dispatch (with a Debug log) rather than dividing. Emitting a
gauge of `+Inf` or `NaN` would be recorded by OpenTelemetry but would confuse Prometheus consumers.
The safest behavior: skip emission when denominator is zero.

### Missing Source OID in Response

If the SNMP device returns fewer varbinds than requested (e.g. NoSuchObject for one source OID),
the value index built from `response` will be incomplete. The combined metric that requires the
missing OID must be skipped for this poll cycle. This is a per-poll-cycle skip, not a permanent
configuration error, so it should log at Debug level to avoid noise on flapping devices.

### Negative Values from Difference Aggregation

`Difference(a, b) = a - b`. This can produce a negative double. `Gauge32` is unsigned and will
overflow. Use `Integer32` (signed) for Difference aggregation. `DispatchCombinedMetricsAsync`
must select the TypeCode based on `AggregationKind`:

- Sum, Max, Min: `Gauge32` (unsigned; safe as long as source values are non-negative counters)
- Ratio: `Gauge32` for non-negative ratios; consider scaling (e.g. multiply by 100 for percentages)
- Difference: `Integer32` (signed)

### Synthetic OID in OtelMetricHandler

`OtelMetricHandler.RecordGauge` receives `oid = "0.0"` for synthetic metrics. The `oid` parameter
is used as a label value (`snmp_oid` label in Prometheus). This means all synthetic metrics from
all poll groups will share `snmp_oid="0.0"`. This is acceptable because the `metric_name` label
differentiates them. If the `snmp_oid` label is important for synthetic metrics, callers could
use the combined metric's output `MetricName` as the synthetic OID (e.g. `"npb_combined_throughput"`)
-- but this is a non-numeric string and would fail `ValidationBehavior`. The sentinel `"0.0"` is
the correct choice; document it explicitly in code comments.

### PollDurationMs on Synthetic Messages

Set `PollDurationMs = null` on synthetic `SnmpOidReceived`. The SNMP round-trip duration belongs
to the poll, not to the aggregation computation (which is in-process). `OtelMetricHandler` checks
`notification.PollDurationMs.HasValue` before calling `RecordGaugeDuration` -- null safely skips
the duration instrument. No change needed in the handler.

---

## Suggested Build Order

Dependencies flow from config models → runtime models → watcher resolution → job execution → pipeline bypass.

### Phase 1: Config and Runtime Models

New files only. No existing code changes. Fully isolated.

- `Configuration/CombinedMetricOptions.cs`
- `Pipeline/AggregationKind.cs`
- `Pipeline/CombinedMetricDefinition.cs`
- `Configuration/PollOptions.cs`: add `CombinedMetrics` property
- `Pipeline/MetricPollInfo.cs`: add `CombinedMetrics` parameter

**Why first:** Everything downstream depends on these shapes. No behavior changes yet; existing
tests are unaffected. `MetricPollInfo` is a positional record -- adding a new optional parameter
with a default value (`default!` or `[]`) is source-compatible with existing construction sites.

### Phase 2: DeviceWatcherService Resolution

- `Services/DeviceWatcherService.cs`: extend `BuildPollGroups` to resolve `CombinedMetricOptions`
  into `CombinedMetricDefinition` instances; validate at load time.

**Why second:** Depends on models from Phase 1. Isolated to one private static method. Unit-testable
without MediatR or Quartz. Existing `ValidateAndBuildDevicesAsync` tests should be extended here.

### Phase 3: OidResolutionBehavior Bypass

- `Pipeline/Behaviors/OidResolutionBehavior.cs`: add pre-set MetricName guard.

**Why third:** This is a 3-line change with high risk of being overlooked. Doing it before
`MetricPollJob` changes ensures the pipeline is ready before synthetic dispatches are added.
Write a unit test: construct `SnmpOidReceived` with pre-set `MetricName`, send through the behavior,
assert `MetricName` is unchanged and `_oidMapService.Resolve` was not called.

### Phase 4: MetricPollJob Dispatch

- `Jobs/MetricPollJob.cs`: add `DispatchCombinedMetricsAsync`; call it after `DispatchResponseAsync`.

**Why fourth (last):** Depends on models (Phase 1), resolved definitions arriving via `MetricPollInfo`
(Phase 2), and the bypass being in place (Phase 3). Integration test: mock a two-OID response,
configure one combined metric (Sum), verify a synthetic `SnmpOidReceived` is sent with the correct
`MetricName` and computed `ExtractedValue` after the pipeline runs.

---

## Confidence Assessment

| Area | Confidence | Basis |
|------|------------|-------|
| Aggregation placement in MetricPollJob (after DispatchResponseAsync) | HIGH | Direct code read; Execute() method structure is clear; call site is unambiguous |
| OidResolutionBehavior overwrites MetricName unconditionally | HIGH | Line 35: `msg.MetricName = _oidMapService.Resolve(msg.Oid)` -- no guard in current code |
| ValidationBehavior passes "0.0" | HIGH | Regex `^\d+(\.\d+){1,}$` -- "0.0" matches; verified by inspection |
| No new pipeline behavior needed | HIGH | All existing behaviors are compatible with synthetic messages (verified per-behavior above) |
| MetricPollInfo positional record -- adding optional parameter is source-compatible | HIGH | C# positional records with default parameter values are backward-compatible for call sites that use named parameters or trailing defaults |
| PollOptions additive change is backward-compatible | HIGH | `List<CombinedMetricOptions> CombinedMetrics = []` -- empty default; no existing config breaks |
| Negative Difference requiring Integer32 vs Gauge32 | HIGH | Gauge32 is uint (0..2^32-1); Integer32 is int (-2^31..2^31-1); overflow on negative is a hard bug |
| Ratio denominator zero handling | HIGH | Standard IEEE 754 / Prometheus constraint; not specific to this codebase |

---

## Open Questions for Roadmap

1. **Scaling for Ratio results:** A ratio of two counters (e.g. error_packets / total_packets) is
   typically 0.0–1.0, which rounds to 0 in a Gauge32 uint. Should ratios be multiplied by a
   configurable scale factor (e.g. 10000 for 4-decimal precision)? Or should the output TypeCode
   for Ratio be a different representation? This needs a decision before Phase 4 implementation.

2. **Combined metrics in TenantVector config:** Tenant vector slots route by `MetricName`. If a
   combined metric's output name (e.g. `"npb_combined_throughput"`) is added to a tenant's
   `MetricSlotOptions`, it will route correctly through `TenantVectorFanOutBehavior` with no
   additional changes. No research gap -- confirming the intended behavior is sufficient.

3. **Counter-type combined metrics:** If source OIDs are `Counter32`/`Counter64`, the raw values
   are monotonically increasing. Aggregating two raw counter values (Sum) produces a combined raw
   counter, which is semantically correct for Prometheus `rate()`/`increase()` to process. But
   `Ratio(counter_a, counter_b)` on raw values is not meaningful -- you would need the delta, not
   the raw cumulative. This semantic constraint is a documentation note, not an implementation
   blocker, but the roadmap should flag it.
