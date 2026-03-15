# Phase 37: Config and Runtime Models - Research

**Researched:** 2026-03-15
**Domain:** C# type modeling — configuration POCO + runtime record + enum, .NET 9, System.Text.Json
**Confidence:** HIGH

## Summary

Phase 37 is a purely additive type-modeling task with no behavior changes. The domain is well-understood and all patterns are established in the existing codebase. Research confirmed the exact conventions in use for configuration POCOs, runtime records, and enums, and identified the one non-trivial decision: how the `Aggregator` string in JSON is parsed into `AggregationKind`.

The standard approach is: add two nullable string properties to `PollOptions`, define `AggregationKind` as a plain enum in the `SnmpCollector.Pipeline` namespace, define `CombinedMetricDefinition` as a `sealed record` in the `SnmpCollector.Pipeline` namespace, and extend `MetricPollInfo` with an `AggregatedMetrics` property defaulting to an empty list. The Aggregator→AggregationKind parse is deferred to `DeviceWatcherService.BuildPollGroups`, which is the existing site for PollOptions-to-MetricPollInfo translation.

**Primary recommendation:** Follow established codebase patterns exactly — `sealed record` for runtime, `sealed class` for config POCO, plain enum for kind. The only discretionary decision is whether `CombinedMetricDefinition` should be a positional `record(...)` or init-only properties; the positional form matches `MetricPollInfo`, `MetricSlot`, `PriorityGroup`, and `DeviceInfo`.

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET 9 C# records | Built-in | Immutable runtime types | Already used for MetricPollInfo, DeviceInfo, MetricSlot, PriorityGroup |
| System.Text.Json | Built-in .NET 9 | JSON deserialization of PollOptions | Already used in DeviceWatcherService with `PropertyNameCaseInsensitive = true` |

### Supporting

No new package dependencies required. Phase 37 is pure type additions.

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Plain enum | `[JsonConverter(JsonStringEnumConverter)]` on enum | Adding the attribute would make the enum self-deserializing, but the Aggregator property on PollOptions is `string`, so enum parsing happens at build-time in BuildPollGroups, not at deserialization. Keep the attribute off the enum. |
| `sealed record` | `sealed class` with init | Records are the established pattern for all runtime Pipeline types. |
| `IReadOnlyList<CombinedMetricDefinition>` | `ImmutableList` | IReadOnlyList + AsReadOnly() is the pattern used consistently throughout the codebase. |

## Architecture Patterns

### File Placement

The existing codebase has a clear split:

```
src/SnmpCollector/
├── Configuration/       # Config POCOs (PollOptions.cs, DeviceOptions.cs, etc.)
│   └── PollOptions.cs   # ← ADD AggregatedMetricName + Aggregator here
└── Pipeline/            # Runtime types (records, enums, services)
    ├── MetricPollInfo.cs # ← ADD AggregatedMetrics property here
    ├── DeviceInfo.cs     # (reference pattern for positional records)
    ├── SnmpSource.cs     # (reference pattern for plain enums)
    ├── AggregationKind.cs   # ← NEW enum here
    └── CombinedMetricDefinition.cs  # ← NEW record here
```

### Pattern 1: Config POCO — two optional nullable strings on PollOptions

`PollOptions` is a `sealed class` with mutable init-style setters. Add nullable strings with `= null` defaults so existing devices without these fields continue to deserialize cleanly.

```csharp
// Source: src/SnmpCollector/Configuration/PollOptions.cs (observed pattern)
/// <summary>
/// Optional. When set, names the aggregate metric to compute from MetricNames values.
/// Both AggregatedMetricName and Aggregator must be non-empty to activate aggregation.
/// </summary>
public string? AggregatedMetricName { get; set; }

/// <summary>
/// Optional. Aggregation function: "sum", "subtract", "absDiff", "mean".
/// Both AggregatedMetricName and Aggregator must be non-empty to activate aggregation.
/// </summary>
public string? Aggregator { get; set; }
```

Both null = disabled. This is the stated decision in CONTEXT.md ("Both null/empty = disabled").

### Pattern 2: Plain enum — AggregationKind in Pipeline namespace

The only existing enum in the codebase is `SnmpSource` in `SnmpCollector.Pipeline` — a minimal, undocumented two-member enum. `AggregationKind` follows the same pattern but deserves XML doc since it is user-facing.

```csharp
// Source: src/SnmpCollector/Pipeline/SnmpSource.cs (observed pattern)
namespace SnmpCollector.Pipeline;

/// <summary>
/// Specifies the arithmetic operation to apply when combining multiple metric values
/// into a single aggregate metric.
/// </summary>
public enum AggregationKind
{
    /// <summary>Sum all source values.</summary>
    Sum,
    /// <summary>First minus second (requires exactly 2 sources).</summary>
    Subtract,
    /// <summary>Absolute difference between two values (requires exactly 2 sources).</summary>
    AbsDiff,
    /// <summary>Arithmetic mean of all source values.</summary>
    Mean
}
```

### Pattern 3: Runtime record — CombinedMetricDefinition in Pipeline namespace

All runtime types in the Pipeline folder are positional `sealed record` types. `MetricPollInfo`, `DeviceInfo`, `MetricSlot`, and `PriorityGroup` all use positional parameter syntax. `CombinedMetricDefinition` should match.

```csharp
// Source: src/SnmpCollector/Pipeline/MetricPollInfo.cs (observed pattern)
namespace SnmpCollector.Pipeline;

/// <summary>
/// Immutable definition of one aggregate metric to compute at poll time.
/// Produced by DeviceWatcherService.BuildPollGroups from PollOptions when
/// AggregatedMetricName and Aggregator are both non-empty.
/// </summary>
/// <param name="MetricName">Name of the aggregate metric to emit.</param>
/// <param name="Kind">Aggregation function to apply to SourceOids values.</param>
/// <param name="SourceOids">Resolved OID strings whose values are combined.</param>
public sealed record CombinedMetricDefinition(
    string MetricName,
    AggregationKind Kind,
    IReadOnlyList<string> SourceOids);
```

### Pattern 4: MetricPollInfo extension — AggregatedMetrics property

`MetricPollInfo` is a positional `sealed record` with a single additional computed method (`JobKey`). The new `AggregatedMetrics` property must be backward-compatible: existing construction sites in `DeviceWatcherService.BuildPollGroups` and in tests pass positional arguments, so the new property must not be a positional parameter. Add it as a property with an init default.

```csharp
// Source: src/SnmpCollector/Pipeline/MetricPollInfo.cs (observed pattern)
public sealed record MetricPollInfo(
    int PollIndex,
    IReadOnlyList<string> Oids,
    int IntervalSeconds,
    double TimeoutMultiplier = 0.8)
{
    /// <summary>
    /// Aggregate metrics to compute from the polled OID values in this group.
    /// Empty by default; populated when PollOptions has AggregatedMetricName + Aggregator.
    /// </summary>
    public IReadOnlyList<CombinedMetricDefinition> AggregatedMetrics { get; init; }
        = Array.Empty<CombinedMetricDefinition>();

    public string JobKey(string configAddress, int port) => ...
}
```

Using `Array.Empty<CombinedMetricDefinition>()` (or `[]` in C# 12+) ensures zero allocation for the common case. The `init;` accessor allows `with` expressions and construction-time assignment without requiring a positional parameter.

### Anti-Patterns to Avoid

- **Making Aggregator a positional parameter on MetricPollInfo:** All existing construction sites (`new MetricPollInfo(index, oids, interval, timeout)`) would break. Use `init` property with default.
- **Putting AggregationKind in the Configuration namespace:** Enums that describe runtime behavior belong in Pipeline, matching `SnmpSource`.
- **Using `JsonStringEnumConverter` on AggregationKind:** The JSON shape stores `Aggregator` as a raw string on the config POCO; enum parsing is done by `BuildPollGroups`. Adding the attribute would be misleading since the enum is never directly deserialized from JSON.
- **Nullable `IReadOnlyList<CombinedMetricDefinition>?`:** The CONTEXT.md decision is `IReadOnlyList<CombinedMetricDefinition>` with default empty. Non-nullable with empty default is cleaner and avoids null checks at every use site.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Case-insensitive string → enum parse | Custom switch/dictionary | `Enum.TryParse<AggregationKind>(value, ignoreCase: true, out var kind)` | .NET built-in, handles all valid values, returns bool for error handling |
| Empty collection default | `new List<>()` | `Array.Empty<CombinedMetricDefinition>()` or `[]` | Zero allocation; established .NET pattern |

**Key insight:** The Aggregator-to-AggregationKind parsing in `BuildPollGroups` is a single `Enum.TryParse` call. This is not phase 37 work (which is type-only), but the planner should note that the type design makes this trivial when phase 38 implements it.

## Common Pitfalls

### Pitfall 1: Breaking MetricPollInfo construction sites

**What goes wrong:** Adding `AggregatedMetrics` as a positional parameter breaks `new MetricPollInfo(...)` in `DeviceWatcherService.BuildPollGroups` and in all tests.

**Why it happens:** Positional record parameters are constructor parameters; adding one changes the constructor signature.

**How to avoid:** Add as `init` property with default value (not positional parameter). C# records support mixing positional parameters and property-body members.

**Warning signs:** Compiler errors at `result.Add(new MetricPollInfo(PollIndex: index, Oids: ..., IntervalSeconds: ..., TimeoutMultiplier: ...))` in DeviceWatcherService.

### Pitfall 2: Aggregator string casing mismatch at parse time

**What goes wrong:** CONTEXT.md specifies JSON uses lowercase (`"sum"`, `"absDiff"`, `"mean"`) but C# enum members are PascalCase (`Sum`, `AbsDiff`, `Mean`). Naive `Enum.Parse` without `ignoreCase: true` would fail on `"sum"`.

**Why it happens:** JSON convention vs C# naming convention mismatch.

**How to avoid:** Use `Enum.TryParse<AggregationKind>(aggregator, ignoreCase: true, out var kind)` when building CombinedMetricDefinition in BuildPollGroups. Phase 37 only defines the types; phase 38 does the parsing — but the type design must not preclude this approach.

**Warning signs:** Tests passing `"sum"` getting no aggregation; `ArgumentException` from strict `Enum.Parse`.

### Pitfall 3: Adding `[JsonPropertyName]` attributes to PollOptions

**What goes wrong:** Cargo-culting JSON attributes when they aren't needed.

**Why it happens:** DeviceWatcherService already uses `PropertyNameCaseInsensitive = true`, so `AggregatedMetricName` in C# deserializes from both `"AggregatedMetricName"` and `"aggregatedMetricName"` in JSON without any attribute.

**How to avoid:** Don't add `[JsonPropertyName]` unless the JSON key differs from the C# property name. The existing pattern (no attributes on PollOptions) is correct.

### Pitfall 4: CombinedMetricDefinition in Configuration namespace

**What goes wrong:** Placing the runtime type alongside config POCOs. It's a runtime type, not a config type.

**Why it happens:** Confusion because it's derived from config (PollOptions).

**How to avoid:** `CombinedMetricDefinition` goes in `SnmpCollector.Pipeline`, same as `MetricPollInfo` and `DeviceInfo`. Config types are only the classes directly deserialized from JSON (PollOptions, DeviceOptions, etc.).

## Code Examples

### AggregationKind enum (complete)

```csharp
// Source: codebase pattern from src/SnmpCollector/Pipeline/SnmpSource.cs
namespace SnmpCollector.Pipeline;

public enum AggregationKind
{
    Sum,
    Subtract,
    AbsDiff,
    Mean
}
```

### CombinedMetricDefinition record (complete)

```csharp
// Source: codebase pattern from src/SnmpCollector/Pipeline/MetricPollInfo.cs
namespace SnmpCollector.Pipeline;

public sealed record CombinedMetricDefinition(
    string MetricName,
    AggregationKind Kind,
    IReadOnlyList<string> SourceOids);
```

### MetricPollInfo with AggregatedMetrics (diff)

```csharp
// Before (src/SnmpCollector/Pipeline/MetricPollInfo.cs):
public sealed record MetricPollInfo(
    int PollIndex,
    IReadOnlyList<string> Oids,
    int IntervalSeconds,
    double TimeoutMultiplier = 0.8)
{
    public string JobKey(string configAddress, int port) => ...
}

// After (add init property, no positional changes):
public sealed record MetricPollInfo(
    int PollIndex,
    IReadOnlyList<string> Oids,
    int IntervalSeconds,
    double TimeoutMultiplier = 0.8)
{
    public IReadOnlyList<CombinedMetricDefinition> AggregatedMetrics { get; init; }
        = [];

    public string JobKey(string configAddress, int port) => ...
}
```

### PollOptions additions (diff)

```csharp
// Add to src/SnmpCollector/Configuration/PollOptions.cs:
public string? AggregatedMetricName { get; set; }
public string? Aggregator { get; set; }
```

### Enum.TryParse pattern (for phase 38 reference)

```csharp
// Source: .NET 9 built-in — confirmed API, HIGH confidence
if (!string.IsNullOrEmpty(poll.AggregatedMetricName)
    && !string.IsNullOrEmpty(poll.Aggregator)
    && Enum.TryParse<AggregationKind>(poll.Aggregator, ignoreCase: true, out var kind))
{
    // build CombinedMetricDefinition
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Mutable class for all types | `sealed record` for immutable runtime types | C# 9 (.NET 5) | Zero-allocation equality, `with` expressions, structural equality |
| `new T[0]` or `new List<T>()` for empty defaults | `Array.Empty<T>()` / `[]` | .NET Core 2+ / C# 12 | Zero allocation, idiomatic |

## Open Questions

1. **`Array.Empty<CombinedMetricDefinition>()` vs `[]` (collection expression)**
   - What we know: The codebase uses `[]` for `List<string> MetricNames { get; set; } = []` in PollOptions (C# 12 collection expression targeting `List<T>`). For `IReadOnlyList<T>` the `[]` collection expression resolves to `Array.Empty<T>()` equivalently.
   - What's unclear: The team's C# 12 collection expression preference for `IReadOnlyList` init defaults.
   - Recommendation: Use `[]` to match PollOptions style. Both compile to the same IL.

2. **Whether to add `SourceMetricNames` alongside `SourceOids` on CombinedMetricDefinition**
   - What we know: CONTEXT.md specifies `SourceOids (IReadOnlyList<string>)` — OID strings, not metric names.
   - What's unclear: Whether future diagnostic logging will want the original metric names as well.
   - Recommendation: Follow CONTEXT.md exactly — `SourceOids` only. Phase 38 can add SourceMetricNames if needed for logging.

## Sources

### Primary (HIGH confidence)

- `src/SnmpCollector/Pipeline/MetricPollInfo.cs` — positional record pattern with `init` body member (JobKey)
- `src/SnmpCollector/Pipeline/DeviceInfo.cs` — positional sealed record reference pattern
- `src/SnmpCollector/Pipeline/SnmpSource.cs` — plain enum pattern in Pipeline namespace
- `src/SnmpCollector/Configuration/PollOptions.cs` — sealed class config POCO pattern
- `src/SnmpCollector/Services/DeviceWatcherService.cs` — BuildPollGroups: the translation site where PollOptions → MetricPollInfo happens; JsonOptions uses `PropertyNameCaseInsensitive = true`
- `src/SnmpCollector/SnmpCollector.csproj` — .NET 9, no new package needed
- `.planning/phases/37-config-and-runtime-models/37-CONTEXT.md` — locked decisions confirmed

### Secondary (MEDIUM confidence)

- .NET 9 docs (training knowledge, stable API): `Enum.TryParse<T>(string, bool, out T)` ignoreCase overload

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new libraries, all patterns observed directly in codebase
- Architecture: HIGH — all placement decisions confirmed from codebase conventions
- Pitfalls: HIGH — MetricPollInfo construction-site risk confirmed by reading BuildPollGroups; enum casing confirmed from CONTEXT.md JSON shape decision

**Research date:** 2026-03-15
**Valid until:** Stable — no external dependencies, pure type modeling
