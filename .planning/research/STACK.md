# Technology Stack — Combined / Synthetic Metrics Milestone

**Project:** Simetra119 SNMP Collector
**Researched:** 2026-03-15
**Milestone scope:** Adding combined (synthetic) numeric metrics computed from poll responses
**Out of scope:** Any new NuGet packages; changes to existing behaviors unrelated to synthesis

---

## Executive Decision

**Zero new NuGet packages.** Every type needed for synthetic metric dispatch is already present in the
existing codebase. The feature requires targeted additions to three existing files and one new enum
member — no new classes, no new services, no new infrastructure.

---

## Existing Stack (Unchanged)

| Technology | Version | Role |
|------------|---------|------|
| .NET / C# | 9.0 | Runtime |
| Lextm.SharpSnmpLib | 12.5.7 | SNMP types — `SnmpType` enum, `ISnmpData` hierarchy |
| MediatR | 12.5.0 | Pipeline dispatch via `ISender.Send` |
| OpenTelemetry SDK | 1.15.0 | `Gauge<double>.Record` / `TagList` |
| Quartz.Extensions.Hosting | 3.15.1 | `[DisallowConcurrentExecution]` on `MetricPollJob` |
| System.Diagnostics.Metrics | BCL (.NET 9) | `TagList`, `Gauge<double>` |

---

## Question 1: Does the MediatR pipeline need changes for synthetic dispatch?

**Answer: No behavioral changes. One guard needed in `ValidationBehavior`.**

The existing `ISender.Send` path works without modification. The synthetic message is constructed by
`MetricPollJob.DispatchResponseAsync` after the varbind loop completes, then sent through the same
`_sender.Send(msg, ct)` call. MediatR routes all `SnmpOidReceived` messages through the same behavior
chain: Logging → Exception → Validation → OidResolution → ValueExtraction → TenantVectorFanOut →
OtelMetricHandler.

However, `ValidationBehavior` currently rejects any message whose `Oid` does not match the pattern
`^\d+(\.\d+){1,}$` (verified in `ValidationBehavior.cs` line 23). A synthetic message with `Oid = ""`
will be rejected at this gate and dropped with a Warning log and `IncrementRejected`. This is a
hard blocker.

**Required change to `ValidationBehavior`:** Allow the empty OID when `Source == SnmpSource.Synthetic`.
The guard should be:

```csharp
// Allow synthetic messages to bypass OID format check
if (msg.Source != SnmpSource.Synthetic && !OidPattern.IsMatch(msg.Oid))
{
    // existing rejection path
}
```

**Required change to `OidResolutionBehavior`:** The behavior calls
`_oidMapService.Resolve(msg.Oid)` for every `SnmpOidReceived`. For a synthetic message, `Oid`
is `""` and `MetricName` is already pre-set by the caller. The OID map will return `Unknown` for
an empty string, overwriting the pre-set `MetricName` with `"unknown"`. This is a second hard blocker.

The guard should be:

```csharp
// Skip OID resolution when MetricName is already set (synthetic messages)
if (msg.Source != SnmpSource.Synthetic)
{
    msg.MetricName = _oidMapService.Resolve(msg.Oid);
    ...
}
```

`ValueExtractionBehavior` does not need changes: synthetic messages arrive with `ExtractedValue`
already set (the computed aggregate). The behavior's `switch (msg.TypeCode)` will match `Integer32`
(the recommended type, see Question 4) and set `ExtractedValue` from `msg.Value`. This means the
caller must also populate `msg.Value` with an `Integer32` wrapping the computed double. Alternatively,
the synthetic message can set `ExtractedValue` directly and the `ValueExtractionBehavior` will
overwrite it with the same value from the `Integer32` wrapper — redundant but harmless.

`TenantVectorFanOutBehavior` does not need changes. It routes by `(ip, port, metricName)`. A
synthetic message with a pre-set `MetricName` will route correctly to any tenant slot configured
with that metric name, provided the `AgentIp` and port match a device in the registry. If no slot
is configured for the synthetic metric, fan-out simply finds nothing and moves on.

`OtelMetricHandler` does not need changes. It reads `MetricName`, `Oid`, `DeviceName`,
`AgentIp`, `Source`, `TypeCode`, and `ExtractedValue` — all of which the synthetic message provides.

---

## Question 2: How should numeric aggregation (sum/diff/mean) be implemented?

**Answer: Inline LINQ inside `DispatchResponseAsync`. No new service or class needed.**

The aggregation computes a single `double` from a local `IList<Variable>` that is fully resolved
before `DispatchResponseAsync` is called. The variables are held in a local variable on the call
stack. There is no shared mutable state — the list is created fresh per poll execution. Aggregation
is therefore a pure local computation.

Recommended implementation pattern:

```csharp
// Inside DispatchResponseAsync, after the varbind foreach loop:
// 1. Collect the numeric values that were dispatched for the named components.
// 2. Compute the aggregate.
// 3. Dispatch a synthetic SnmpOidReceived.

var componentValues = response
    .Where(v => v.Data.TypeCode is SnmpType.Integer32 or SnmpType.Gauge32
                               or SnmpType.Counter32 or SnmpType.Counter64
                               or SnmpType.TimeTicks)
    .Select(v => ExtractDouble(v.Data))   // local static helper, same logic as ValueExtractionBehavior
    .ToList();

if (componentValues.Count > 0)
{
    var aggregate = componentValues.Sum();  // or .Average(), or pair-wise diff

    await _sender.Send(new SnmpOidReceived
    {
        Oid            = string.Empty,
        AgentIp        = IPAddress.Parse(device.ResolvedIp),
        DeviceName     = device.Name,
        Value          = new Integer32((int)aggregate),
        Source         = SnmpSource.Synthetic,
        TypeCode       = SnmpType.Integer32,
        MetricName     = "<configured synthetic metric name>",
        ExtractedValue = aggregate,
        PollDurationMs = null   // no round-trip concept for synthetic
    }, ct);
}
```

**Why LINQ, not a new service:**
- The computation is local to one poll group response. It accesses no shared state.
- A `SyntheticMetricEngine` service would require injecting a new dependency into `MetricPollJob`,
  complicate DI registration, and provide no benefit for what is a two-line numeric operation.
- The `[DisallowConcurrentExecution]` attribute on `MetricPollJob` means only one execution per
  job instance runs at a time. The aggregate computation is therefore single-threaded within a
  given poll group (see Question 5).

**Helper method:** A `private static double ExtractDouble(ISnmpData data)` method in
`MetricPollJob` (or `DispatchResponseAsync`) mirrors the numeric cases of `ValueExtractionBehavior`
without the MediatR message overhead. It should not call back into MediatR or any service.

**Difference (A - B) pattern:** For rate-like synthetics (e.g., `ifInOctets - ifOutOctets`),
the poll group config must declare which OIDs are "A" and which are "B". This requires a new config
field (see Question on `PollOptions` below). If the OID order is guaranteed stable in the SNMP
response (it is, for a well-behaved agent), ordered index lookup works. The safer approach is OID
keying: build a `Dictionary<string, double>` from the response, then perform named subtraction.

---

## Question 3: Does OTel handle empty OID labels correctly?

**Answer: Yes, empty string is safe. The risk is null, not empty string.**

Verified from multiple sources:

1. The OTel specification explicitly states: "Empty string should be a valid attribute value" and
   empty-string values are "considered meaningful and MUST be stored and passed on to processors /
   exporters." (opentelemetry-specification issue #5501 for Java; same spec applies to .NET.)

2. The problematic case for OTel .NET was **null** tag values, not empty strings. Issues #3451 and
   #3846 in `opentelemetry-dotnet` document that null values caused silent metric drops or
   NullReferenceExceptions in `Tags.GetHashCode()`. These were fixed in SDK 1.4.0. The project
   uses OTel SDK 1.15.0 (confirmed in `SnmpCollector.csproj`), so the null bug is irrelevant.

3. `SnmpMetricFactory.RecordGauge` passes `oid` directly as a `TagList` value (line 44 of
   `SnmpMetricFactory.cs`). An empty string there is a valid `string` value — `TagList` accepts
   `object?` per the BCL API, and `string.Empty` boxes to a non-null object.

**Conclusion:** Set `Oid = string.Empty` on synthetic messages. The `oid` label in Prometheus will
show as an empty string. This is the intended behavior — it signals "no source OID" to dashboards.
Do not use `null`; that would break the current `TagList` composition.

**Cardinality note:** The `oid` label is currently the highest-cardinality label (one unique value
per polled OID per device). For a synthetic metric, `oid = ""` creates exactly one label
combination per `(metric_name, device_name, ip)` tuple — lower cardinality than real OIDs.

---

## Question 4: Does the existing `SnmpType` enum cover synthetic needs?

**Answer: A new `SnmpSource.Synthetic` member is needed. `SnmpType` does not need a new member.**

**`SnmpSource` — add `Synthetic` member:**

`SnmpSource.cs` currently defines:
```csharp
public enum SnmpSource { Poll, Trap }
```

A `Synthetic` member is necessary for the two pipeline bypasses described in Question 1
(ValidationBehavior OID regex guard, OidResolutionBehavior MetricName preservation). Without it,
there is no clean way for behaviors to distinguish "this OID is intentionally empty" from
"this message is malformed."

Change to:
```csharp
public enum SnmpSource { Poll, Trap, Synthetic }
```

This is a one-line change. All existing `switch` statements and `if`/`is` checks on `SnmpSource`
do not enumerate it exhaustively — they check for specific values — so no existing code breaks.
`TenantVectorFanOutBehavior` has no `SnmpSource` check at all. `OtelMetricHandler` formats source
as `notification.Source.ToString().ToLowerInvariant()` — the synthetic label will appear as
`"synthetic"` in the `source` Prometheus label, which is correct and useful.

**`SnmpType` — no new member needed:**

The existing `SnmpType.Integer32` from SharpSnmpLib covers synthetic numeric values. The
`ValueExtractionBehavior` switch already handles `Integer32` correctly. `OtelMetricHandler`
dispatches `Integer32` to `RecordGauge`. The computed aggregate (sum/diff/mean as `double`)
must be wrapped in a `new Integer32((int)aggregate)` to satisfy the `required ISnmpData Value`
field on `SnmpOidReceived`. Precision loss from the `double → int` cast is acceptable for
integer network metrics. For sub-integer precision (e.g., percentage averages), `Gauge32` is
equivalent — it also maps to `RecordGauge` via the same case in `OtelMetricHandler`.

**The `ExtractedValue` shortcut:** Because `ValueExtractionBehavior` runs in the pipeline, the
caller can set `ExtractedValue = aggregate` directly and also set `Value = new Integer32((int)aggregate)`.
The behavior will overwrite `ExtractedValue` with the same integer, creating harmless redundancy.
This is preferable to adding a skip-extraction path because it keeps the behavior's contract intact
(it always sets `ExtractedValue` from `Value` for numeric types).

---

## Question 5: Thread-safety of aggregate computation in `MetricPollJob`

**Answer: No threading concern. `[DisallowConcurrentExecution]` already provides the guarantee.**

`MetricPollJob` is annotated `[DisallowConcurrentExecution]` (verified in `MetricPollJob.cs` line 22).
Quartz's contract for this attribute: if the trigger fires while the previous execution is still
running for that job key, Quartz skips the fire. There is at most one active `Execute(...)` call
per job key at any moment.

The poll response `IList<Variable>` is created fresh per `Execute` call by `_snmpClient.GetAsync`.
It is local to the call stack and never shared across calls or stored in any field. The aggregate
computation over this list happens entirely within `DispatchResponseAsync`, which is called once
per `Execute`. There are no shared mutable fields involved in the aggregation.

`_sender.Send` is the only external call during aggregation. `ISender` (MediatR) is thread-safe
by design (it is a factory for pipeline invocations). Each `Send` call creates its own pipeline
instance.

**Conclusion:** No lock, `Interlocked`, or `ConcurrentDictionary` is needed for the aggregation.
LINQ `Sum()` / `Average()` on a local `List<double>` is sufficient.

---

## Required Type Changes Summary

### 1. `SnmpSource.cs` — Add enum member

```csharp
public enum SnmpSource { Poll, Trap, Synthetic }
```

**Why:** Required by ValidationBehavior and OidResolutionBehavior to distinguish intentional
synthetic messages from malformed ones. Without this, synthetic messages are either rejected by
the OID regex check or have their MetricName overwritten by OID resolution.

### 2. `ValidationBehavior.cs` — Guard the OID regex check

```csharp
if (msg.Source != SnmpSource.Synthetic && !OidPattern.IsMatch(msg.Oid))
{ /* existing rejection */ }
```

**Why:** Synthetic messages have `Oid = ""` by design. The existing regex `^\d+(\.\d+){1,}$`
rejects empty strings. Without this guard, all synthetic messages are dropped.

### 3. `OidResolutionBehavior.cs` — Guard MetricName overwrite

```csharp
if (msg.Source != SnmpSource.Synthetic)
{
    msg.MetricName = _oidMapService.Resolve(msg.Oid);
    ...
}
```

**Why:** Synthetic messages have MetricName pre-set by the caller. `_oidMapService.Resolve("")`
returns `OidMapService.Unknown`, which would overwrite the correct pre-set name.

### 4. `MetricPollJob.cs` — Inline aggregate + synthetic dispatch

No new field or dependency injection. `DispatchResponseAsync` gains post-loop code that:
- Extracts numeric values from the response into a local `List<double>`
- Computes the aggregate with LINQ
- Constructs and sends a synthetic `SnmpOidReceived`

The synthetic metric name and aggregation function (sum/diff/mean) must come from configuration.
Whether those live on `PollOptions` or elsewhere is a design decision for the roadmap, but the
data must flow into `MetricPollJob` via its existing `IJobExecutionContext.MergedJobDataMap`
(the established pattern for per-job configuration).

### 5. `PollOptions.cs` — New optional fields for synthetic definition

`PollOptions` currently holds `MetricNames`, `IntervalSeconds`, and `TimeoutMultiplier`. To
express "after polling these OIDs, compute this aggregate and emit it as a synthetic metric,"
the poll group needs:

```csharp
/// Optional. When non-null, a synthetic metric is computed from the poll response
/// and dispatched after individual varbinds.
public SyntheticMetricOptions? Synthetic { get; set; }
```

New supporting type:

```csharp
public sealed class SyntheticMetricOptions
{
    /// The metric name for the synthetic result. Must exist in the OID map is NOT required —
    /// it is a user-assigned name for the computed metric.
    public string MetricName { get; set; } = string.Empty;

    /// Aggregation function. Allowed: "Sum", "Mean", "Diff".
    /// "Diff" expects exactly 2 MetricNames in the enclosing PollOptions: result = A - B.
    public string Aggregation { get; set; } = string.Empty;
}
```

This follows the existing pattern of `PollOptions` holding polling behavior config, and avoids
coupling synthetic logic to tenant vectors or OID maps.

---

## What NOT to Add

| Omission | Rationale |
|----------|-----------|
| New `SyntheticMetricEngine` service | Aggregation is a local two-line LINQ operation; a service adds DI complexity for zero benefit |
| New `SnmpType` enum member (e.g., `Synthetic`) | `Integer32` or `Gauge32` covers synthetic numeric values; both route to `RecordGauge` |
| `null` for `Oid` on synthetic messages | OTel .NET had a null-tag-value bug (fixed in 1.4.0, but `string.Empty` is safer and explicit) |
| Separate MediatR request type for synthetic | Reusing `SnmpOidReceived` with `Source = Synthetic` reuses the full behavior chain; a second type doubles the behavior registration surface |
| `[AllowConcurrentExecution]` on `MetricPollJob` | Current concurrency guard is correct; relaxing it would require locking the aggregate computation |
| Pre-resolution of synthetic MetricName to OID | Synthetic metrics have no source OID; OID resolution is irrelevant and should be skipped (see OidResolutionBehavior guard above) |
| New NuGet packages | Every required type exists in SharpSnmpLib 12.5.7, .NET 9 BCL, and existing OTel 1.15.0 |

---

## Integration Fit

| Concern | Existing pattern | Synthetic fit |
|---------|-----------------|---------------|
| Source label in Prometheus | `"poll"` or `"trap"` | `"synthetic"` (from `SnmpSource.Synthetic.ToString().ToLowerInvariant()`) |
| OID label in Prometheus | The source OID string | `""` (empty string, valid OTel tag value) |
| MetricName routing | Set by OidResolutionBehavior | Pre-set by caller; OidResolutionBehavior bypassed via Source guard |
| Tenant vector fan-out | Routes by (ip, port, metricName) | Works unchanged if a tenant slot is configured with the synthetic MetricName |
| `MetricSlotHolder.WriteValue` | Called by TenantVectorFanOutBehavior | Called unchanged; synthetic `TypeCode = Integer32`, `Source = Synthetic` stored on holder |
| Duration recording in OtelMetricHandler | `PollDurationMs.HasValue` guard | `PollDurationMs = null` on synthetic — no duration recorded (correct: no round-trip) |

---

## Confidence Assessment

| Area | Confidence | Source |
|------|------------|--------|
| MediatR pipeline behavior chain order | HIGH | Read `ValidationBehavior.cs`, `OidResolutionBehavior.cs`, `ValueExtractionBehavior.cs`, `TenantVectorFanOutBehavior.cs`, `OtelMetricHandler.cs` directly |
| ValidationBehavior OID regex rejects `""` | HIGH | Read regex pattern directly from `ValidationBehavior.cs` line 23 |
| OidResolutionBehavior overwrites MetricName unconditionally | HIGH | Read behavior code directly; no existing guard |
| OTel empty string tag safety | MEDIUM | OTel specification states empty strings are meaningful and must be preserved; .NET SDK null-tag bug is fixed in 1.4.0; project uses 1.15.0. Direct SDK code read was rate-limited, so not personally verified at source level |
| `[DisallowConcurrentExecution]` thread-safety guarantee | HIGH | Read annotation directly in `MetricPollJob.cs` line 22; Quartz contract is well-documented |
| `SnmpType.Integer32` covers synthetic numeric values | HIGH | Read `OtelMetricHandler.cs` and `ValueExtractionBehavior.cs` — Integer32 case exists and routes to `RecordGauge` |
| No new NuGet packages needed | HIGH | All required types identified in existing source files |

---

## Sources

All sources read directly from repository (no hypothesis):

- `src/SnmpCollector/Pipeline/SnmpOidReceived.cs` — field inventory, `required ISnmpData Value`, `Source` type
- `src/SnmpCollector/Pipeline/SnmpSource.cs` — confirmed only `Poll` and `Trap` exist; `Synthetic` is missing
- `src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs` — OID regex pattern, rejection path
- `src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs` — unconditional `msg.MetricName =` assignment
- `src/SnmpCollector/Pipeline/Behaviors/ValueExtractionBehavior.cs` — numeric TypeCode switch, `ExtractedValue` assignment
- `src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` — routing logic, no SnmpSource check
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` — `source` label, `Integer32` → `RecordGauge` path
- `src/SnmpCollector/Jobs/MetricPollJob.cs` — `[DisallowConcurrentExecution]`, `DispatchResponseAsync` structure
- `src/SnmpCollector/Configuration/PollOptions.cs` — current fields, extension point for `SyntheticMetricOptions`
- `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` — `TagList` composition, `oid` tag, empty string safety
- `src/SnmpCollector/SnmpCollector.csproj` — OpenTelemetry SDK 1.15.0 version confirmed
- OTel specification (via WebSearch): empty string is a valid, meaningful attribute value
- opentelemetry-dotnet issues #3451 and #3846 (via WebSearch): null tag bug fixed in SDK 1.4.0; empty string is not the problematic case

---

*Stack research for: Combined / Synthetic Metrics milestone*
*Researched: 2026-03-15*
