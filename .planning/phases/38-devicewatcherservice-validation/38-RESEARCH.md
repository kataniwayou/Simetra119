# Phase 38: DeviceWatcherService Validation - Research

**Researched:** 2026-03-15
**Domain:** C# / xUnit / NSubstitute — extension of existing BuildPollGroups validation in DeviceWatcherService
**Confidence:** HIGH

## Summary

Phase 38 is a pure C# code change with no new dependencies. All decisions are locked in CONTEXT.md; research confirms the exact insertion points, the data flow, and the test harness patterns already used for this service.

The work is a targeted extension of `BuildPollGroups` in `DeviceWatcherService.cs`. That method currently resolves MetricNames to OIDs and drops poll groups with zero OIDs. Phase 38 adds a post-OID-resolution validation pass that validates the `AggregatedMetricName` + `Aggregator` pair on each poll, builds `CombinedMetricDefinition` records, and populates `MetricPollInfo.AggregatedMetrics`. New tests go into the existing `DeviceWatcherValidationTests.cs` file, which already has 12 tests covering the synchronous `BuildPollGroups` path.

**Primary recommendation:** Insert combined-metric validation as a new block at the bottom of `BuildPollGroups`, after OID resolution succeeds, before the `result.Add(new MetricPollInfo(...))` call. Use a `HashSet<string>` scoped to the `BuildPollGroups` call to detect per-device duplicate `AggregatedMetricName` values.

## Standard Stack

No new packages required. All tools are already in the project.

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| xunit | 2.9.3 | Test framework | Project standard |
| NSubstitute | 5.3.0 | Mock `IOidMapService` | Project standard |
| Microsoft.Extensions.Logging.Abstractions | 9.0.0 | `NullLogger.Instance`, `ILogger` mock | Project standard |

### No Alternatives Needed

This phase adds no new infrastructure. All required types (`CombinedMetricDefinition`, `AggregationKind`, `IOidMapService.ContainsMetricName`) exist from Phase 37.

## Architecture Patterns

### Existing `BuildPollGroups` Structure

```csharp
// src/SnmpCollector/Services/DeviceWatcherService.cs  line 310
private static ReadOnlyCollection<MetricPollInfo> BuildPollGroups(
    List<PollOptions> polls,
    string deviceName,
    IOidMapService oidMapService,
    ILogger logger)
{
    var result = new List<MetricPollInfo>();
    for (var index = 0; index < polls.Count; index++)
    {
        var poll = polls[index];
        // ... OID resolution loop ...

        if (resolvedOids.Count == 0) { ... continue; }

        result.Add(new MetricPollInfo(
            PollIndex: index,
            Oids: resolvedOids.AsReadOnly(),
            IntervalSeconds: poll.IntervalSeconds,
            TimeoutMultiplier: poll.TimeoutMultiplier));
    }
    return result.AsReadOnly();
}
```

### Pattern: Validate-Then-Build Combined Metric

**What:** After the `resolvedOids.Count == 0` guard, check whether the poll has aggregation config. Run all validation rules; on any failure, log Error, set `combinedMetric = null`, fall through to the `result.Add` without it. On success, build `CombinedMetricDefinition` and pass it via the `init` property.

**Placement:**
```csharp
// After: if (resolvedOids.Count == 0) { ... continue; }
// Before: result.Add(new MetricPollInfo(...))

CombinedMetricDefinition? combinedMetric = null;

var hasName = !string.IsNullOrEmpty(poll.AggregatedMetricName);
var hasAggregator = !string.IsNullOrEmpty(poll.Aggregator);

if (hasName || hasAggregator)
{
    // 1. Co-presence
    if (!hasName || !hasAggregator) { /* Error + skip */ }
    // 2. Enum.TryParse
    else if (!Enum.TryParse<AggregationKind>(poll.Aggregator, ignoreCase: true, out var kind)) { /* Error + skip */ }
    // 3. Minimum 2 MetricNames (use resolvedOids, not MetricNames, to avoid phantom OID problem — see pitfalls)
    else if (resolvedOids.Count < 2) { /* Error + skip */ }
    // 4. Duplicate AggregatedMetricName on this device
    else if (!seenAggregatedNames.Add(poll.AggregatedMetricName!)) { /* Error + skip */ }
    // 5. OID map name collision
    else if (oidMapService.ContainsMetricName(poll.AggregatedMetricName!)) { /* Error + skip */ }
    else
    {
        combinedMetric = new CombinedMetricDefinition(
            poll.AggregatedMetricName!,
            kind,
            resolvedOids.AsReadOnly());
    }
}

result.Add(new MetricPollInfo(
    PollIndex: index,
    Oids: resolvedOids.AsReadOnly(),
    IntervalSeconds: poll.IntervalSeconds,
    TimeoutMultiplier: poll.TimeoutMultiplier)
{
    AggregatedMetrics = combinedMetric is null ? [] : [combinedMetric]
});
```

The `seenAggregatedNames` HashSet must be declared before the loop so it persists across all poll groups for the device. Pass it as a parameter or declare in the outer scope of `BuildPollGroups`.

### Pattern: Per-Device Duplicate Name Tracking

`BuildPollGroups` is a `static` method already receiving `deviceName`. Add `var seenAggregatedNames = new HashSet<string>(StringComparer.Ordinal);` at the top of the method, before the `for` loop. This matches the `byIpPortSeen` pattern in `ValidateAndBuildDevicesAsync`. StringComparer.Ordinal is correct — metric names are case-sensitive identifiers.

### Pattern: Structured Error Log

Existing error logs in `BuildPollGroups` use message templates with named params for structured logging. Match this style:

```csharp
logger.LogError(
    "Combined metric '{AggregatedMetricName}' on device '{DeviceName}' poll {PollGroupIndex} {Reason} -- skipping combined metric",
    poll.AggregatedMetricName, deviceName, index, "reason text");
```

The CONTEXT.md lists: DeviceName, PollGroupIndex, AggregatedMetricName, Reason — all are available in scope.

### Test Pattern: `DeviceWatcherValidationTests`

All new tests belong in `tests/SnmpCollector.Tests/Services/DeviceWatcherValidationTests.cs`. The file's current test helper is:

```csharp
private static async Task<List<DeviceInfo>> BuildAsync(
    List<DeviceOptions> devices,
    IOidMapService? oidMapService = null)
{
    return await DeviceWatcherService.ValidateAndBuildDevicesAsync(
        devices,
        oidMapService ?? CreatePassthroughOidMapService(),
        NullLogger.Instance,
        CancellationToken.None);
}
```

The passthrough `IOidMapService` returns `callInfo.Arg<string>()` for `ResolveToOid` — i.e., returns the metric name itself as the OID. For combined metric tests, `ContainsMetricName` needs to be set up separately on the substitute.

**Logger capture pattern** (used in existing tests for error/warning assertions):
```csharp
var logger = Substitute.For<ILogger>();
await DeviceWatcherService.ValidateAndBuildDevicesAsync(devices, svc, logger, CancellationToken.None);
logger.Received(1).Log(
    LogLevel.Error,
    Arg.Any<EventId>(),
    Arg.Is<object>(o => o.ToString()!.Contains("some substring")),
    null,
    Arg.Any<Func<object, Exception?, string>>());
```

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Case-insensitive enum parse | Custom string→enum switch | `Enum.TryParse<AggregationKind>(value, ignoreCase: true)` | Already decided in CONTEXT.md; handles all 4 values and returns false for invalid |
| OID map name collision check | Manual dictionary iteration | `IOidMapService.ContainsMetricName(name)` | Interface method exists from Phase 37, implemented in `OidMapService` |
| Duplicate tracking | LINQ GroupBy | `HashSet<string>.Add()` returns false on collision | O(1), mutation-safe during loop |

## Common Pitfalls

### Pitfall 1: Checking `MetricNames.Count` Instead of `resolvedOids.Count` for the Minimum-2 Rule

**What goes wrong:** The minimum-2 check uses `poll.MetricNames.Count` (raw config), but some names may have failed OID resolution. A poll with 3 MetricNames and 1 resolved OID would pass the minimum-2 check incorrectly, then produce a `CombinedMetricDefinition` with only 1 `SourceOid` — a silent semantic error.

**How to avoid:** Check `resolvedOids.Count < 2` (the already-computed list), not `poll.MetricNames.Count < 2`. The combined metric can only use OIDs that actually resolved.

**Note:** The CONTEXT.md states "at least 2 entries in MetricNames[]" as the user-level requirement. At implementation time this translates to 2 _resolved_ OIDs because the `CombinedMetricDefinition.SourceOids` is built from `resolvedOids`, not from raw names.

**Update:** If the team interprets "minimum 2 MetricNames" literally (raw count, not resolved), the check should be `poll.MetricNames.Count < 2`. Either way, document the choice clearly. The safer interpretation is `resolvedOids.Count < 2` to prevent a 1-source combined metric at runtime. Flag as an open question below.

### Pitfall 2: HashSet Scope — Per-Device vs. Per-Reload

**What goes wrong:** If `seenAggregatedNames` is declared outside `BuildPollGroups` and reused across devices, names that are valid on device B could incorrectly collide with device A's names.

**How to avoid:** Declare `seenAggregatedNames` inside `BuildPollGroups` (per-device scope). The CONTEXT.md notes this is Claude's discretion; per-device scope is correct per the "Duplicate AggregatedMetricName on same device" rule.

### Pitfall 3: `init` Property Assignment Syntax

**What goes wrong:** `MetricPollInfo` uses a positional record with `AggregatedMetrics` as an `init` property (added in Phase 37). The assignment must use object initializer syntax at construction time:

```csharp
// CORRECT
result.Add(new MetricPollInfo(...) { AggregatedMetrics = [combinedMetric] });

// WRONG — MetricPollInfo is sealed record, cannot set init after construction
var info = new MetricPollInfo(...);
info.AggregatedMetrics = [...]; // compile error
```

### Pitfall 4: NSubstitute `ContainsMetricName` Default

**What goes wrong:** In tests using the passthrough `IOidMapService`, `ContainsMetricName` is not set up, so it returns `false` by default (NSubstitute default for `bool` returns). This means the OID map collision check will not fire in tests that don't explicitly set it up — which is correct behavior for most tests. For collision tests, explicitly set:

```csharp
svc.ContainsMetricName("colliding_name").Returns(true);
```

### Pitfall 5: Log Message Substring Match

**What goes wrong:** Existing test assertions use `o.ToString()!.Contains("some substring")` against the structured log message. The `ILogger.Log` call produces an `IReadOnlyList<KeyValuePair<string, object?>>` as the state, whose `ToString()` is the formatted message string. The substring must match what `LogError`/`LogWarning` produces from the template — not the raw template itself.

**How to avoid:** Use distinctive substrings from the formatted output (device name, reason phrase) rather than template parameter names like `{AggregatedMetricName}`.

## Code Examples

### Verified: `MetricPollInfo` `init` Property Population (from CombinedMetricModelTests.cs)

```csharp
// Source: tests/SnmpCollector.Tests/Pipeline/CombinedMetricModelTests.cs line 76
var info = new MetricPollInfo(
    PollIndex: 0,
    Oids: new[] { "1.3.6.1.2.1.1.0", "1.3.6.1.2.1.2.0" },
    IntervalSeconds: 10)
{
    AggregatedMetrics = new[] { definition }
};
```

### Verified: `Enum.TryParse` ignoreCase (from CombinedMetricModelTests.cs)

```csharp
// Source: tests/SnmpCollector.Tests/Pipeline/CombinedMetricModelTests.cs line 27
var result = Enum.TryParse<AggregationKind>(input, ignoreCase: true, out var kind);
```

### Verified: `IOidMapService.ContainsMetricName` signature (from IOidMapService.cs)

```csharp
// Source: src/SnmpCollector/Pipeline/IOidMapService.cs line 28
bool ContainsMetricName(string metricName);
```

### Verified: `CombinedMetricDefinition` positional construction (from Phase 37)

```csharp
// Source: src/SnmpCollector/Pipeline/CombinedMetricDefinition.cs
new CombinedMetricDefinition(
    MetricName: "combined_power",
    Kind: AggregationKind.Sum,
    SourceOids: resolvedOids.AsReadOnly())
```

## State of the Art

This is entirely internal C# — no library evolution concerns.

| Old Approach | Current Approach | When Changed |
|--------------|------------------|--------------|
| No aggregation support | `AggregatedMetrics` init property on `MetricPollInfo` | Phase 37 |
| No `AggregatedMetricName`/`Aggregator` on `PollOptions` | Both nullable fields present | Phase 37 |
| No `ContainsMetricName` on `IOidMapService` | Interface method present | Phase 37 |

## Open Questions

1. **Minimum-2 check: raw `MetricNames.Count` vs. resolved `resolvedOids.Count`**
   - What we know: CONTEXT.md says "at least 2 entries in MetricNames[]". Phase 37 model tests use `new[] { "1.3.6.1.2.1.1.0", "1.3.6.1.2.1.2.0" }` as SourceOids.
   - What's unclear: Should the check gate on raw config names (user intent) or resolved OIDs (runtime safety)?
   - Recommendation: Use `resolvedOids.Count < 2` for runtime safety. If 2 names were configured but only 1 resolved, the combined metric would compute over a single value — arguably meaningless. Log with reason "fewer than 2 OIDs resolved".

2. **`AggregatedMetrics` init property type**: `IReadOnlyList<CombinedMetricDefinition>` — each poll can theoretically carry multiple combined metrics, but Phase 38 only ever creates 0 or 1 per poll group (one `AggregatedMetricName` per `PollOptions`). Assign as `new[] { combinedMetric }` or use collection expression `[combinedMetric]`.

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Services/DeviceWatcherService.cs` — exact insertion point verified by reading current implementation
- `src/SnmpCollector/Pipeline/MetricPollInfo.cs` — `AggregatedMetrics` init property confirmed
- `src/SnmpCollector/Configuration/PollOptions.cs` — `AggregatedMetricName` and `Aggregator` fields confirmed
- `src/SnmpCollector/Pipeline/AggregationKind.cs` — 4 enum members confirmed
- `src/SnmpCollector/Pipeline/CombinedMetricDefinition.cs` — positional record signature confirmed
- `src/SnmpCollector/Pipeline/IOidMapService.cs` — `ContainsMetricName` method confirmed
- `tests/SnmpCollector.Tests/Services/DeviceWatcherValidationTests.cs` — test patterns, helper methods, logger assertion style confirmed
- `tests/SnmpCollector.Tests/Pipeline/CombinedMetricModelTests.cs` — `init` property and `Enum.TryParse` patterns confirmed

### Secondary (MEDIUM confidence)
- `.planning/phases/38-devicewatcherservice-validation/38-CONTEXT.md` — locked decisions, per-entry skip semantics

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new libraries; all tools confirmed present in project
- Architecture: HIGH — insertion point identified, method signature unchanged, patterns from existing tests replicated
- Pitfalls: HIGH — derived from reading actual code and test patterns, not speculation

**Research date:** 2026-03-15
**Valid until:** Stable (no external dependencies) — valid until DeviceWatcherService or MetricPollInfo change
