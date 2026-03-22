# Phase 72: TenantMetricService & Meter Registration - Research

**Researched:** 2026-03-23
**Domain:** .NET 9 OpenTelemetry Metrics — System.Diagnostics.Metrics, IMeterFactory
**Confidence:** HIGH

## Summary

Phase 72 creates `TenantMetricService` — a new singleton that mirrors the existing
`PipelineMetricService` pattern exactly. The codebase already demonstrates all required
patterns: counter creation via `_meter.CreateCounter<long>`, push-gauge creation via
`_meter.CreateGauge<double>`, histogram creation via `_meter.CreateHistogram<double>`,
AddMeter registration in `ServiceCollectionExtensions`, and MeterListener-based unit testing.

The only novel element is the `TenantState` enum (renamed + extended from the internal
`TierResult` enum in `SnapshotJob`). The gauge instrument records the enum value as an
integer by casting to `int`, which is consistent with how `SnmpMetricFactory` already
records `Gauge<double>` values.

The `MetricRoleGatedExporter` gates only meters whose name equals
`TelemetryConstants.LeaderMeterName` (`"SnmpCollector.Leader"`). The new
`"SnmpCollector.Tenant"` meter name is different, so it passes through ungated on all
instances with zero exporter changes.

**Primary recommendation:** Copy the `PipelineMetricService` structure verbatim — new Telemetry
file, same constructor pattern (`IMeterFactory`), same TagList tag style, register as
`AddSingleton` in `AddSnmpPipeline`, add `AddMeter` in `AddSnmpTelemetry`.

## Standard Stack

All libraries already present in `SnmpCollector.csproj`. No new dependencies.

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Diagnostics.Metrics | .NET 9 BCL | Counter, Gauge, Histogram instruments | Built into runtime, no package needed |
| OpenTelemetry.Extensions.Hosting | 1.15.0 | IMeterFactory, AddMeter, MeterProvider | Already in project |
| Microsoft.Extensions.DependencyInjection | (ASP.NET Core) | AddSingleton, IMeterFactory injection | Already in project |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.Extensions.DependencyInjection (test) | 9.0.0 | services.AddMetrics() for test IMeterFactory | Every TenantMetricService unit test |
| xunit | 2.9.3 | Test framework | All tests |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Gauge<int> for tenant.state | Gauge<double> | Codebase uses Gauge<double> exclusively; cast enum to int then to double for consistency |
| ObservableGauge | Gauge<double> | ObservableGauge is pull-based (callback); push Gauge<T> matches existing SnmpMetricFactory pattern and is simpler to test |

**Installation:**
```bash
# No new packages needed — all dependencies already present
```

## Architecture Patterns

### Recommended Project Structure
```
src/SnmpCollector/
├── Telemetry/
│   ├── TelemetryConstants.cs        # Add TenantMeterName constant here
│   ├── ITenantMetricService.cs      # NEW: interface for testability
│   ├── TenantMetricService.cs       # NEW: singleton, mirrors PipelineMetricService
│   └── PipelineMetricService.cs     # Existing (reference model)
├── Pipeline/
│   └── TenantState.cs               # NEW: moved/renamed from SnapshotJob.TierResult (internal enum)
├── Jobs/
│   └── SnapshotJob.cs               # Modify: remove TierResult, use TenantState, add ITenantMetricService injection
└── Extensions/
    └── ServiceCollectionExtensions.cs  # Modify: AddMeter + AddSingleton registrations
```

### Pattern 1: Singleton Service with IMeterFactory

**What:** Service receives `IMeterFactory` in constructor, calls `meterFactory.Create(meterName)`,
then creates all instruments from the resulting `Meter`.
**When to use:** All metric services in this codebase.
**Example:**
```csharp
// Source: src/SnmpCollector/Telemetry/PipelineMetricService.cs
public sealed class TenantMetricService : ITenantMetricService, IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _tier1Stale;
    // ... other instruments

    public TenantMetricService(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(TelemetryConstants.TenantMeterName);
        _tier1Stale = _meter.CreateCounter<long>("tenant.tier1.stale");
        // ... create all 8 instruments
    }

    public void Dispose() => _meter.Dispose();
}
```

### Pattern 2: TagList for Recording

**What:** All `Add` / `Record` calls use `TagList { { "key", value }, ... }` inline.
**When to use:** Every public method on TenantMetricService.
**Example:**
```csharp
// Source: src/SnmpCollector/Telemetry/PipelineMetricService.cs (line 88-89)
public void IncrementTier1Stale(string tenantId, int priority)
    => _tier1Stale.Add(1, new TagList { { "tenant_id", tenantId }, { "priority", priority } });
```

### Pattern 3: Gauge Record with Enum Cast

**What:** Push `Gauge<double>` records enum state by casting to int/double.
**When to use:** tenant.state gauge.
**Example:**
```csharp
// Source: src/SnmpCollector/Telemetry/SnmpMetricFactory.cs (CreateGauge pattern)
private readonly Gauge<double> _state;
// in constructor:
_state = _meter.CreateGauge<double>("tenant.state");

// public method:
public void RecordTenantState(string tenantId, int priority, TenantState state)
    => _state.Record((double)(int)state, new TagList { { "tenant_id", tenantId }, { "priority", priority } });
```

### Pattern 4: Histogram Record

**What:** `_meter.CreateHistogram<double>(name, description: "...")` with no unit parameter for ms histograms.
**When to use:** tenant.evaluation.duration.milliseconds histogram.
**Example:**
```csharp
// Source: src/SnmpCollector/Telemetry/PipelineMetricService.cs (line 82-85)
_evaluationDuration = _meter.CreateHistogram<double>(
    "tenant.evaluation.duration.milliseconds",
    description: "Duration of one tenant evaluation in milliseconds");

public void RecordEvaluationDuration(string tenantId, int priority, double durationMs)
    => _evaluationDuration.Record(durationMs, new TagList { { "tenant_id", tenantId }, { "priority", priority } });
```

### Pattern 5: AddMeter Registration

**What:** `metrics.AddMeter(TelemetryConstants.TenantMeterName)` in `AddSnmpTelemetry` alongside existing AddMeter calls.
**When to use:** Every new meter needs AddMeter so OpenTelemetry collects it.
**Example:**
```csharp
// Source: src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs (line 86-87)
metrics.AddMeter(TelemetryConstants.MeterName);        // existing
metrics.AddMeter(TelemetryConstants.LeaderMeterName);  // existing
metrics.AddMeter(TelemetryConstants.TenantMeterName);  // NEW — Phase 72
```

### Pattern 6: Singleton Registration

**What:** `services.AddSingleton<ITenantMetricService, TenantMetricService>()` in `AddSnmpPipeline`.
**When to use:** Mirrors `PipelineMetricService` registration (line 407 of ServiceCollectionExtensions).
**Example:**
```csharp
// Source: src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs (line 407)
// PipelineMetricService is registered as concrete type (no interface). TenantMetricService
// should be registered via its interface for testability (see Decisions).
services.AddSingleton<ITenantMetricService, TenantMetricService>();
```

### Pattern 7: MeterListener Unit Test

**What:** `services.AddMetrics()`, construct service with `IMeterFactory`, create `MeterListener`
filtered by meter name, `listener.Start()`, call methods, assert on captured measurements.
**When to use:** All TenantMetricService tests.
**Example:**
```csharp
// Source: tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs
[Collection(NonParallelCollection.Name)]
public sealed class TenantMetricServiceTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly TenantMetricService _service;
    private readonly MeterListener _listener;
    private readonly List<(string InstrumentName, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = new();

    public TenantMetricServiceTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        _sp = services.BuildServiceProvider();
        _service = new TenantMetricService(_sp.GetRequiredService<IMeterFactory>());

        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TelemetryConstants.TenantMeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            _measurements.Add((instrument.Name, value, tags.ToArray())));
        // also double callback for histogram/gauge
        _listener.Start();
    }

    public void Dispose() { _listener.Dispose(); _service.Dispose(); _sp.Dispose(); }
}
```

### Anti-Patterns to Avoid

- **Not calling `_listener.Start()` before exercising the service:** MeterListener does not capture any measurements until `Start()` is called. The `InstrumentPublished` callback fires on `Start()` for already-created instruments.
- **Creating the service before `_listener.Start()`:** If `TenantMetricService` is constructed before the listener starts, `InstrumentPublished` fires during `Start()` — this is fine. But not calling `Start()` at all means zero measurements are captured.
- **Parallel test execution with MeterListener:** `MeterListener` is a global listener. Tests using it must be in `[Collection(NonParallelCollection.Name)]` (already established in the codebase).
- **Using `AddSingleton<TenantMetricService>()` without an interface:** PipelineMetricService was registered without an interface — but the Decisions lock in ITenantMetricService for testability. Register via interface.
- **Registering TenantMetricService outside AddSnmpPipeline:** PipelineMetricService lives in `AddSnmpPipeline`. TenantMetricService belongs there too for consistency.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Meter lifecycle / disposal | Custom dispose wrapper | IMeterFactory + `_meter.Dispose()` in `IDisposable.Dispose()` | Factory handles scoped lifetime; existing pattern in PipelineMetricService and SnmpMetricFactory |
| Tag construction | string concatenation or Dictionary | `TagList { { "key", val } }` | OTel-native, stack-allocated for small counts, no heap pressure |
| Test meter observation | Mocking counters | `MeterListener` + `services.AddMetrics()` | Captures real OTel measurements; already in test infrastructure |
| Custom histogram buckets | `AddView` + `ExplicitBucketHistogramConfiguration` | Default OTel buckets | No custom bucket pattern exists in this codebase; consistent with `snmp.snapshot.cycle_duration_ms` |

**Key insight:** Every piece of infrastructure needed for Phase 72 already exists in the codebase. This phase is purely additive — no new patterns, no new test infrastructure.

## Common Pitfalls

### Pitfall 1: TierResult Enum Rename Side Effects

**What goes wrong:** `TierResult` is used in `SnapshotJob` at 7+ call sites (local variable declarations, comparisons, return statements). Renaming to `TenantState` without updating all usages causes compile errors.
**Why it happens:** The enum is internal to `SnapshotJob` and tests reference it as `SnapshotJob.TierResult`.
**How to avoid:** After moving/renaming, grep for all `TierResult` references in both `SnapshotJob.cs` and `SnapshotJobTests.cs` and update to `TenantState`. Tests reference `SnapshotJob.TierResult.Healthy`, `SnapshotJob.TierResult.Resolved`, `SnapshotJob.TierResult.Unresolved` — these become `TenantState.Healthy`, `TenantState.Resolved`, `TenantState.Unresolved`.
**Warning signs:** Compiler error `CS0246: The type or namespace name 'TierResult' could not be found`.

### Pitfall 2: TenantState Value Ordering

**What goes wrong:** The existing `TierResult` has values `{ Resolved, Healthy, Unresolved }` (0, 1, 2). The new `TenantState` must have `{ NotReady=0, Healthy=1, Resolved=2, Unresolved=3 }`. If the ordering is wrong, the gauge will report incorrect numeric states in Prometheus.
**Why it happens:** Adding `NotReady` at the beginning shifts existing values unless explicit integer assignments are used.
**How to avoid:** Use explicit integer assignments in the enum definition:
```csharp
public enum TenantState { NotReady = 0, Healthy = 1, Resolved = 2, Unresolved = 3 }
```
**Warning signs:** Gauge values in dashboards offset by 1 vs. documentation.

### Pitfall 3: SnapshotJob TierResult Logic Must Still Work

**What goes wrong:** `SnapshotJob.EvaluateTenant` returns `TierResult.Unresolved` for the pre-tier NotReady case. After renaming, it returns `TenantState.Unresolved` but the pre-tier case semantically represents `NotReady`. The advance-gate logic checks only for `Unresolved` — so returning `Unresolved` for NotReady is functionally correct for gate behavior but the metric instrumentation in Phase 73 will need to distinguish NotReady.
**Why it happens:** The pre-tier return was `TierResult.Unresolved` because `NotReady` did not exist yet.
**How to avoid:** In Phase 72, the `EvaluateTenant` return type changes to `TenantState`. The pre-tier path should return `TenantState.NotReady` (not `TenantState.Unresolved`) once the enum is in place. The advance-gate check should then block on BOTH `NotReady` AND `Unresolved`:
```csharp
if (results[i] == TenantState.Unresolved || results[i] == TenantState.NotReady)
    shouldAdvance = false;
```
This is a behavior-preserving change (both values block advance) while enabling correct metric recording in Phase 73.

### Pitfall 4: AddMeter Must Precede MetricRoleGatedExporter Registration

**What goes wrong:** If `AddMeter(TenantMeterName)` is placed after the `AddReader` call in `AddSnmpTelemetry`, the MeterProvider may not subscribe to the new meter.
**Why it happens:** `AddReader` closes the pipeline configuration; `AddMeter` calls must precede it.
**How to avoid:** Insert `metrics.AddMeter(TelemetryConstants.TenantMeterName)` alongside the two existing `AddMeter` calls on lines 86-87, not after the `AddReader` lambda.

### Pitfall 5: Interface Namespace

**What goes wrong:** `ITenantMetricService` placed in `SnmpCollector.Telemetry` namespace but injected in `SnmpCollector.Jobs` (SnapshotJob) — requires `using SnmpCollector.Telemetry;` in SnapshotJob.
**Why it happens:** SnapshotJob already has `using SnmpCollector.Telemetry;` (for `PipelineMetricService`), so this is not actually a problem.
**How to avoid:** Keep both interface and implementation in `SnmpCollector.Telemetry` namespace, same as `PipelineMetricService`.

### Pitfall 6: Test File Must Be in NonParallelMeterTests Collection

**What goes wrong:** `TenantMetricServiceTests` without `[Collection(NonParallelCollection.Name)]` can cause flaky test failures due to cross-test `MeterListener` measurement contamination.
**Why it happens:** `MeterListener` is a global listener; parallel tests for different meters may fire each other's callbacks if meter names overlap or if thread timing interleaves.
**How to avoid:** Always add `[Collection(NonParallelCollection.Name)]` on every test class that uses `MeterListener`.

## Code Examples

Verified patterns from official sources:

### TelemetryConstants addition
```csharp
// Source: src/SnmpCollector/Telemetry/TelemetryConstants.cs (existing file, add new constant)
/// <summary>
/// Tenant metrics meter -- exported by ALL instances (no leader gate).
/// Used by TenantMetricService for tenant.* instruments.
/// </summary>
public const string TenantMeterName = "SnmpCollector.Tenant";
```

### TenantState enum (new file in Pipeline/)
```csharp
// New file: src/SnmpCollector/Pipeline/TenantState.cs
namespace SnmpCollector.Pipeline;

/// <summary>
/// Encodes the outcome of one tenant's 4-tier evaluation cycle.
/// Values are stored as gauge integers: NotReady=0, Healthy=1, Resolved=2, Unresolved=3.
/// Replaces the former internal SnapshotJob.TierResult enum.
/// </summary>
public enum TenantState
{
    NotReady   = 0,
    Healthy    = 1,
    Resolved   = 2,
    Unresolved = 3
}
```

### ITenantMetricService interface
```csharp
// New file: src/SnmpCollector/Telemetry/ITenantMetricService.cs
namespace SnmpCollector.Telemetry;

public interface ITenantMetricService
{
    void IncrementTier1Stale(string tenantId, int priority);
    void IncrementTier2Resolved(string tenantId, int priority);
    void IncrementTier3Evaluate(string tenantId, int priority);
    void IncrementCommandDispatched(string tenantId, int priority);
    void IncrementCommandFailed(string tenantId, int priority);
    void IncrementCommandSuppressed(string tenantId, int priority);
    void RecordTenantState(string tenantId, int priority, TenantState state);
    void RecordEvaluationDuration(string tenantId, int priority, double durationMs);
}
```

### TenantMetricService constructor (instrument creation)
```csharp
// New file: src/SnmpCollector/Telemetry/TenantMetricService.cs
public sealed class TenantMetricService : ITenantMetricService, IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _tier1Stale;
    private readonly Counter<long> _tier2Resolved;
    private readonly Counter<long> _tier3Evaluate;
    private readonly Counter<long> _commandDispatched;
    private readonly Counter<long> _commandFailed;
    private readonly Counter<long> _commandSuppressed;
    private readonly Gauge<double> _state;
    private readonly Histogram<double> _evaluationDuration;

    public TenantMetricService(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(TelemetryConstants.TenantMeterName);
        _tier1Stale        = _meter.CreateCounter<long>("tenant.tier1.stale");
        _tier2Resolved     = _meter.CreateCounter<long>("tenant.tier2.resolved");
        _tier3Evaluate     = _meter.CreateCounter<long>("tenant.tier3.evaluate");
        _commandDispatched = _meter.CreateCounter<long>("tenant.command.dispatched");
        _commandFailed     = _meter.CreateCounter<long>("tenant.command.failed");
        _commandSuppressed = _meter.CreateCounter<long>("tenant.command.suppressed");
        _state             = _meter.CreateGauge<double>("tenant.state");
        _evaluationDuration = _meter.CreateHistogram<double>(
            "tenant.evaluation.duration.milliseconds",
            description: "Duration of one tenant evaluation cycle in milliseconds");
    }

    public void Dispose() => _meter.Dispose();
}
```

### ServiceCollectionExtensions changes

In `AddSnmpTelemetry`, inside `WithMetrics`:
```csharp
// Add after line 87 (LeaderMeterName AddMeter):
metrics.AddMeter(TelemetryConstants.TenantMeterName);  // Tenant metrics (always exported)
```

In `AddSnmpPipeline`, after `PipelineMetricService` registration:
```csharp
// Add after: services.AddSingleton<PipelineMetricService>();
services.AddSingleton<ITenantMetricService, TenantMetricService>();
```

### SnapshotJob constructor update
```csharp
// Add ITenantMetricService parameter alongside PipelineMetricService
public SnapshotJob(
    ITenantVectorRegistry registry,
    ISuppressionCache suppressionCache,
    ICommandChannel commandChannel,
    ICorrelationService correlation,
    ILivenessVectorService liveness,
    PipelineMetricService pipelineMetrics,
    ITenantMetricService tenantMetrics,    // NEW
    IOptions<SnapshotJobOptions> options,
    ILogger<SnapshotJob> logger)
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `ObservableGauge<T>` (pull-based) | `Gauge<T>` (push, .NET 8+) | .NET 8 | `Gauge<T>` allows direct `Record()` calls; no callback needed; already used in SnmpMetricFactory |

**Deprecated/outdated:**
- `ObservableGauge`: Pull-based, requires a callback delegate. Do not use for tenant.state — use `Gauge<double>` with explicit `Record()` like SnmpMetricFactory does.

## Open Questions

1. **Method grouping for counter increments (Claude's Discretion)**
   - What we know: PipelineMetricService has one method per counter (`IncrementCommandDispatched`, `IncrementCommandFailed`, `IncrementCommandSuppressed` as separate methods). This is the established pattern.
   - What's unclear: Whether grouped-per-tier methods (`RecordTier1(string tenantId, int priority)` that increments `tier1.stale`) would be cleaner since Phase 73 will call per-tier.
   - Recommendation: Follow existing one-method-per-counter pattern for consistency. Phase 73 callers will be explicit about which counter to increment.

2. **TelemetryConstants placement**
   - What we know: `TelemetryConstants.cs` currently has `MeterName` and `LeaderMeterName`. Adding `TenantMeterName` there is the natural placement.
   - What's unclear: Nothing — this is straightforward.
   - Recommendation: Add `TenantMeterName = "SnmpCollector.Tenant"` to the existing `TelemetryConstants` class.

3. **SnapshotJob return type for pre-tier (NotReady vs Unresolved)**
   - What we know: The current code returns `TierResult.Unresolved` for the pre-tier NotReady case. After renaming, the advance-gate logic must still block on this value.
   - What's unclear: Whether Phase 72 should change the pre-tier return to `TenantState.NotReady` now (Phase 73 depends on it for metric recording) or defer to Phase 73.
   - Recommendation: Change it in Phase 72 since the enum value exists and the advance-gate update is trivial — returning `NotReady` while keeping the gate blocking on both `NotReady` and `Unresolved` is a safe, complete change.

## Sources

### Primary (HIGH confidence)
- Direct codebase inspection: `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — exact constructor, instrument creation, and TagList patterns
- Direct codebase inspection: `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` — Gauge<double> pattern, CreateGauge, histogram without unit override
- Direct codebase inspection: `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — AddMeter call sites, AddSingleton registration patterns
- Direct codebase inspection: `src/SnmpCollector/Telemetry/MetricRoleGatedExporter.cs` — gate logic uses `string.Equals(metric.MeterName, _gatedMeterName)` — new meter name passes ungated
- Direct codebase inspection: `src/SnmpCollector/Telemetry/TelemetryConstants.cs` — existing constant structure
- Direct codebase inspection: `src/SnmpCollector/Jobs/SnapshotJob.cs` — TierResult enum location (line 33), all 7 usage sites
- Direct codebase inspection: `tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs` — MeterListener test pattern
- Direct codebase inspection: `tests/SnmpCollector.Tests/Helpers/NonParallelCollection.cs` — collection fixture for MeterListener isolation
- Direct codebase inspection: `src/SnmpCollector/Properties/AssemblyInfo.cs` — `InternalsVisibleTo("SnmpCollector.Tests")` confirms tests can access internal types
- Direct codebase inspection: `src/SnmpCollector/SnmpCollector.csproj` — net9.0, OpenTelemetry 1.15.0

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all libraries already present, versions confirmed from .csproj
- Architecture: HIGH — all patterns directly observed in existing production code
- Pitfalls: HIGH — derived from reading actual code paths and existing test patterns
- TenantState enum values: HIGH — explicitly specified in CONTEXT.md decisions

**Research date:** 2026-03-23
**Valid until:** 2026-06-23 (stable .NET/OTel stack, no fast-moving dependencies)
