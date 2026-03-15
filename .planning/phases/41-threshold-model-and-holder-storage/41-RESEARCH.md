# Phase 41: Threshold Model & Holder Storage - Research

**Researched:** 2026-03-15
**Domain:** C# configuration model extension, runtime holder storage
**Confidence:** HIGH

## Summary

Phase 41 adds threshold configuration (Min/Max double?) to the metric slot config model and carries it through to MetricSlotHolder for future runtime access. The work is entirely additive — no existing behaviour changes, no validation logic is activated yet.

The codebase follows a consistent pattern for config properties that flow through to holders: they are declared on `MetricSlotOptions`, read in `TenantVectorRegistry.Reload`, passed as constructor arguments to `MetricSlotHolder`, and exposed as init-only properties. `ThresholdOptions` follows the same structural pattern as other nested config classes already in the project (e.g. `CommandSlotOptions`, `DeviceOptions`).

**Primary recommendation:** Add a `ThresholdOptions` sealed class with `Min` and `Max` nullable doubles, add a `Threshold` nullable property to `MetricSlotOptions`, add a `Threshold` property to `MetricSlotHolder` (set at construction time from the config), and pass it through in `TenantVectorRegistry.Reload`. No changes needed in `ValidateAndBuildTenants` — threshold is stored as-is, not validated.

## Standard Stack

No new libraries or packages. All changes use built-in C# language features and existing project conventions.

### Core
| Component | Version | Purpose | Why Standard |
|-----------|---------|---------|--------------|
| C# 12 / .NET 9 | Current | Nullable value types, sealed classes | Project target |
| `System.Text.Json` | Built-in | `PropertyNameCaseInsensitive = true` already set on `JsonOptions` in `TenantVectorWatcherService` | No `[JsonPropertyName]` needed |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Nullable double (`double?`) | Range-validated struct | Phase decision locks Min/Max as nullable doubles — no custom type needed |
| Constructor arg for Threshold | Mutable property | Constructor injection matches all other holder properties (Ip, Port, MetricName, IntervalSeconds, TimeSeriesSize) — keeps holder immutable at the identity level |

## Architecture Patterns

### Recommended Project Structure

No new files needed beyond `ThresholdOptions.cs` in `src/SnmpCollector/Configuration/`. All other changes are modifications to existing files.

```
src/SnmpCollector/Configuration/
├── ThresholdOptions.cs          ← NEW: sealed class { double? Min; double? Max }
├── MetricSlotOptions.cs         ← MODIFY: add ThresholdOptions? Threshold property
src/SnmpCollector/Pipeline/
├── MetricSlotHolder.cs          ← MODIFY: add ThresholdOptions? Threshold constructor arg + property
├── TenantVectorRegistry.cs      ← MODIFY: pass metric.Threshold to new MetricSlotHolder(...)
tests/SnmpCollector.Tests/
├── Pipeline/MetricSlotHolderTests.cs          ← MODIFY: add constructor/property tests
├── Pipeline/TenantVectorRegistryTests.cs      ← MODIFY: add Reload_ThresholdFromConfig_StoredInHolder test
```

### Pattern 1: Nested Config Class (sealed, no attributes)

All other config option classes in this project are `public sealed class` with `{ get; set; }` properties and simple defaults. `ThresholdOptions` follows exactly this convention.

```csharp
// Source: src/SnmpCollector/Configuration/CommandSlotOptions.cs (structural model)
public sealed class ThresholdOptions
{
    public double? Min { get; set; }
    public double? Max { get; set; }
}
```

No `[JsonPropertyName]` is needed because `TenantVectorWatcherService.JsonOptions` already uses `PropertyNameCaseInsensitive = true`. This is confirmed by the existing decision and by the fact that no other config class in the project uses `[JsonPropertyName]`.

### Pattern 2: Holder Property — Init-Style from Constructor

`MetricSlotHolder` constructor currently takes: `ip`, `port`, `metricName`, `intervalSeconds`, `timeSeriesSize`. All identity/config properties are set in the constructor and exposed as `public` get-only. `Threshold` follows the same pattern.

```csharp
// Analogous to existing: public int IntervalSeconds { get; }
public ThresholdOptions? Threshold { get; }

// Constructor extended:
public MetricSlotHolder(
    string ip, int port, string metricName,
    int intervalSeconds,
    int timeSeriesSize = 1,
    ThresholdOptions? threshold = null)
{
    // ...existing assignments...
    Threshold = threshold;
}
```

Using a default parameter `threshold = null` keeps backward compatibility with all existing call sites (including `MetricSlotHolderTests` helper `CreateHolder`, the heartbeat holder in `TenantVectorRegistry.Reload`, etc.) — no existing test or production code breaks.

### Pattern 3: Registry Reload — Pass-Through Only

In `TenantVectorRegistry.Reload`, the only change is passing `metric.Threshold` when constructing the new holder:

```csharp
var newHolder = new MetricSlotHolder(
    metric.Ip,
    metric.Port,
    metric.MetricName,
    metric.IntervalSeconds,
    metric.TimeSeriesSize,
    metric.Threshold);       // ← add this
```

No changes to `CopyFrom` — threshold is an identity-level config property, not runtime state. The carry-over mechanism (`CopyFrom`) copies value/series/TypeCode/Source; threshold identity comes from the new config on each reload, just like `Ip`, `Port`, and `MetricName`.

### Pattern 4: ValidateAndBuildTenants — No Change Required

`ValidateAndBuildTenants` in `TenantVectorWatcherService` passes `MetricSlotOptions` directly to the clean list. Since `ThresholdOptions?` is a nullable property on `MetricSlotOptions`, it is automatically preserved in the clean `metric` that is added to `cleanMetrics`. No additional code is needed there — this is confirmed by how `TimeSeriesSize` and `IntervalSeconds` are handled: they flow through without any validation logic in that method.

### Anti-Patterns to Avoid

- **Do not add threshold validation in `ValidateAndBuildTenants`**: Phase 41 is purely storage. Validation (e.g., Min <= Max) is deferred to a later phase.
- **Do not add `[JsonPropertyName]` attributes**: `PropertyNameCaseInsensitive = true` is already set and the project convention has zero `[JsonPropertyName]` usages.
- **Do not make `Threshold` mutable on the holder**: Config-sourced identity properties are all get-only. Consistency with `Ip`, `Port`, `MetricName`, `IntervalSeconds`, `TimeSeriesSize`.
- **Do not change `CopyFrom`**: Threshold is not runtime state — it is configuration identity. Each reload provides the authoritative threshold from the new config.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| Case-insensitive JSON property mapping | Custom `JsonConverter` | `PropertyNameCaseInsensitive = true` already in place |
| Carry-over of threshold across reload | Custom merge logic | Simply not needed — threshold is config identity, not runtime state |

## Common Pitfalls

### Pitfall 1: Breaking existing MetricSlotHolder call sites
**What goes wrong:** Adding a required constructor parameter for `Threshold` forces edits to every `new MetricSlotHolder(...)` call in production and test code.
**Why it happens:** Constructor signature change without a default value.
**How to avoid:** Add `ThresholdOptions? threshold = null` as an optional last parameter. All existing call sites compile unchanged. Only `Reload` explicitly passes the value.
**Warning signs:** Build error CS7036 (no argument matching required parameter).

### Pitfall 2: Forgetting to pass Threshold in Reload
**What goes wrong:** Config correctly has `ThresholdOptions` but holder always has `null` because `Reload` was not updated.
**Why it happens:** Adding property to model and holder but missing the wire-up in `Reload`.
**How to avoid:** The registry test `Reload_ThresholdFromConfig_StoredInHolder` directly verifies this flow end-to-end.
**Warning signs:** Test passes for null case but fails when non-null threshold is expected on holder.

### Pitfall 3: Adding threshold to CopyFrom
**What goes wrong:** `CopyFrom` incorrectly copies `Threshold` from the old holder, overwriting the new config's threshold.
**Why it happens:** Misclassifying threshold as runtime state instead of config identity.
**How to avoid:** Never touch `CopyFrom`. The new holder is constructed with `metric.Threshold` from the fresh config; that is authoritative.

## Code Examples

### ThresholdOptions (new file)
```csharp
// Source: codebase pattern from CommandSlotOptions.cs / DeviceOptions.cs
namespace SnmpCollector.Configuration;

/// <summary>
/// Optional min/max bounds for a metric slot. Stored in MetricSlotHolder
/// for future runtime threshold evaluation. Neither Min nor Max is required.
/// </summary>
public sealed class ThresholdOptions
{
    public double? Min { get; set; }
    public double? Max { get; set; }
}
```

### MetricSlotOptions addition
```csharp
/// <summary>
/// Optional threshold bounds (Min and/or Max) for this metric slot.
/// Stored in MetricSlotHolder for future evaluation. Not validated at load time.
/// </summary>
public ThresholdOptions? Threshold { get; set; }
```

### MetricSlotHolder constructor and property
```csharp
public ThresholdOptions? Threshold { get; }

public MetricSlotHolder(
    string ip, int port, string metricName,
    int intervalSeconds,
    int timeSeriesSize = 1,
    ThresholdOptions? threshold = null)
{
    Ip = ip;
    Port = port;
    MetricName = metricName;
    IntervalSeconds = intervalSeconds;
    TimeSeriesSize = timeSeriesSize;
    Threshold = threshold;
}
```

### TenantVectorRegistry.Reload change (single-line addition)
```csharp
var newHolder = new MetricSlotHolder(
    metric.Ip,
    metric.Port,
    metric.MetricName,
    metric.IntervalSeconds,
    metric.TimeSeriesSize,
    metric.Threshold);   // ← add this line
```

### Test: Reload_ThresholdFromConfig_StoredInHolder (in TenantVectorRegistryTests)
```csharp
[Fact]
public void Reload_ThresholdFromConfig_StoredInHolder()
{
    var registry = CreateRegistry();
    var options = new TenantVectorOptions
    {
        Tenants = new List<TenantOptions>
        {
            new()
            {
                Priority = 1,
                Metrics = new List<MetricSlotOptions>
                {
                    new()
                    {
                        Ip = "10.0.0.1", Port = 161, MetricName = "hrProcessorLoad",
                        Role = "Evaluate",
                        Threshold = new ThresholdOptions { Min = 0.0, Max = 100.0 }
                    },
                    new() { Ip = "10.0.0.1", Port = 161, MetricName = "auto_resolved", Role = "Resolved" }
                },
                Commands = new List<CommandSlotOptions>
                {
                    new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd", Value = "1", ValueType = "Integer32" }
                }
            }
        }
    };

    registry.Reload(options);

    registry.TryRoute("10.0.0.1", 161, "hrProcessorLoad", out var holders);
    Assert.NotNull(holders);
    var threshold = holders[0].Threshold;
    Assert.NotNull(threshold);
    Assert.Equal(0.0, threshold.Min);
    Assert.Equal(100.0, threshold.Max);
}
```

### Test: Constructor_StoresThreshold (in MetricSlotHolderTests)
```csharp
[Fact]
public void Constructor_StoresThreshold()
{
    var threshold = new ThresholdOptions { Min = 10.0, Max = 90.0 };
    var holder = new MetricSlotHolder("10.0.0.1", 161, "m", 30, threshold: threshold);

    Assert.NotNull(holder.Threshold);
    Assert.Equal(10.0, holder.Threshold.Min);
    Assert.Equal(90.0, holder.Threshold.Max);
}

[Fact]
public void Constructor_NullThreshold_DefaultsToNull()
{
    var holder = new MetricSlotHolder("10.0.0.1", 161, "m", 30);
    Assert.Null(holder.Threshold);
}
```

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| No threshold on metric config | ThresholdOptions? on MetricSlotOptions | Purely additive; no behaviour change |
| No threshold on holder | ThresholdOptions? on MetricSlotHolder | Available for future evaluation phases |

## Open Questions

No open questions. All decisions are locked in STATE.md and the implementation pattern is fully determined by examining the existing codebase.

## Sources

### Primary (HIGH confidence)
- Direct codebase inspection:
  - `src/SnmpCollector/Configuration/MetricSlotOptions.cs` — existing property pattern
  - `src/SnmpCollector/Configuration/CommandSlotOptions.cs` — structural template for ThresholdOptions
  - `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — constructor signature and property conventions
  - `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — Reload wire-up pattern
  - `src/SnmpCollector/Services/TenantVectorWatcherService.cs` — JsonOptions (PropertyNameCaseInsensitive), ValidateAndBuildTenants pass-through
  - `tests/SnmpCollector.Tests/Pipeline/MetricSlotHolderTests.cs` — test patterns
  - `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` — Reload test patterns (especially `Reload_IntervalSecondsFromConfig_StoredInHolder`)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new libraries; all patterns confirmed from live codebase
- Architecture: HIGH — directly derived from existing analogous properties (IntervalSeconds, TimeSeriesSize) in the same files
- Pitfalls: HIGH — derived from structural analysis of call sites and carry-over semantics

**Research date:** 2026-03-15
**Valid until:** Stable — pure additive model extension, no external dependencies
