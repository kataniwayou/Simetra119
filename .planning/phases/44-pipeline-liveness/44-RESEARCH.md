# Phase 44: Pipeline Liveness - Research

**Researched:** 2026-03-15
**Domain:** Internal C# — new singleton service + OtelMetricHandler stamp point + LivenessHealthCheck extension
**Confidence:** HIGH

## Summary

Phase 44 adds a second liveness dimension alongside the existing job-completion liveness vector: pipeline arrival liveness. The existing `ILivenessVectorService` tracks whether the Quartz scheduler fires jobs on time; it cannot detect whether the heartbeat trap actually traverses the full pipeline (listener → channel → MediatR → handler). A blocked channel, a crashed `ChannelConsumerService`, or a broken MediatR registration would leave `ILivenessVectorService` stamps healthy while the pipeline is silent.

The solution is a dedicated `IHeartbeatLivenessService` that stamps `DateTimeOffset.UtcNow` when `OtelMetricHandler` processes a message whose `DeviceName == HeartbeatJobOptions.HeartbeatDeviceName` ("Simetra"). `LivenessHealthCheck` is then extended to also check this stamp: if `now - lastArrival > DefaultIntervalSeconds × 2.0` → unhealthy. The threshold uses `HeartbeatJobOptions.DefaultIntervalSeconds` (15s) and a grace multiplier of 2.0 (existing `LivenessOptions.GraceMultiplier` pattern), giving a 30s staleness window before K8s liveness probe begins counting failures.

All decisions are locked. No external libraries required. This is a pure in-codebase addition following existing patterns.

**Primary recommendation:** Create `IHeartbeatLivenessService` / `HeartbeatLivenessService` as a thread-safe singleton using `volatile DateTimeOffset?`, inject it into `OtelMetricHandler`, stamp in the switch case after gauge record, and add a check in `LivenessHealthCheck.CheckHealthAsync`.

## Standard Stack

No new NuGet packages. This phase uses only existing infrastructure.

### Files Changed

| File | What Changes |
|------|-------------|
| `src/SnmpCollector/Pipeline/IHeartbeatLivenessService.cs` | New — interface with `Stamp()` and `LastArrival` property |
| `src/SnmpCollector/Pipeline/HeartbeatLivenessService.cs` | New — thread-safe singleton impl using `volatile` field |
| `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` | Inject `IHeartbeatLivenessService`; call `Stamp()` when `DeviceName == HeartbeatDeviceName` |
| `src/SnmpCollector/HealthChecks/LivenessHealthCheck.cs` | Inject `IHeartbeatLivenessService` + `IOptions<HeartbeatJobOptions>`; add pipeline arrival staleness check |
| `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` | Register `IHeartbeatLivenessService` as singleton in `AddSnmpPipeline` |
| `tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs` | Add test: heartbeat message stamps `IHeartbeatLivenessService`; non-heartbeat does not |
| `tests/SnmpCollector.Tests/HealthChecks/LivenessHealthCheckTests.cs` | Add tests: pipeline arrival stale → Unhealthy; fresh → Healthy; never stamped → Unhealthy after interval |

### Files That Do NOT Change

| File | Why |
|------|-----|
| `src/SnmpCollector/Jobs/HeartbeatJob.cs` | `_liveness.Stamp(jobKey)` in `finally` is explicitly preserved per phase decisions |
| `src/SnmpCollector/Pipeline/OidMapService.cs` | Heartbeat seed (`HeartbeatOid → "Heartbeat"`) is correct and unchanged |
| `src/SnmpCollector/Configuration/HeartbeatJobOptions.cs` | Constants used as-is; `DefaultIntervalSeconds = 15` is the threshold source |
| `src/SnmpCollector/Pipeline/ILivenessVectorService.cs` | Job liveness continues unchanged |
| `src/SnmpCollector/Pipeline/LivenessVectorService.cs` | Unchanged |

## Architecture Patterns

### Pattern 1: IHeartbeatLivenessService Interface

Minimal interface — stamp and read. The stamp point is `OtelMetricHandler`; the read point is `LivenessHealthCheck`.

```csharp
namespace SnmpCollector.Pipeline;

/// <summary>
/// Tracks when the heartbeat trap last completed the full MediatR pipeline.
/// Stamped by OtelMetricHandler when DeviceName == HeartbeatJobOptions.HeartbeatDeviceName.
/// Read by LivenessHealthCheck to detect a silent pipeline (channel blocked, consumer crashed, etc.).
/// </summary>
public interface IHeartbeatLivenessService
{
    /// <summary>Records the current UTC time as the last heartbeat pipeline arrival.</summary>
    void Stamp();

    /// <summary>
    /// The last time a heartbeat completed the pipeline, or null if no heartbeat
    /// has been observed since startup.
    /// </summary>
    DateTimeOffset? LastArrival { get; }
}
```

### Pattern 2: HeartbeatLivenessService Implementation

Thread safety: `volatile` on a `DateTimeOffset?`-equivalent. Because `DateTimeOffset` is a struct and not an atomic type on all platforms, the canonical thread-safe pattern for a single writer / multiple readers is `Interlocked` via a `long` (ticks) or a reference-type box. However, for a single assignment that is only ever written from the pipeline thread (one at a time, no contention), `volatile` on a boxed reference is the simplest correct approach. The existing `LivenessVectorService` uses `ConcurrentDictionary` for multi-key stamps — this service holds one value and needs a simpler approach.

```csharp
using System.Threading;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Thread-safe single-value liveness stamp for heartbeat pipeline arrival.
/// Uses volatile long (UTC ticks) for lock-free read/write. A value of 0 means never stamped.
/// </summary>
public sealed class HeartbeatLivenessService : IHeartbeatLivenessService
{
    private volatile long _lastArrivalTicks; // 0 = never stamped

    public void Stamp()
    {
        Volatile.Write(ref _lastArrivalTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    public DateTimeOffset? LastArrival
    {
        get
        {
            var ticks = Volatile.Read(ref _lastArrivalTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }
}
```

**Why `volatile long` not `volatile DateTimeOffset?`:** `DateTimeOffset` is a struct (16 bytes on 64-bit). C# `volatile` is only valid on reference types and primitive types ≤ 8 bytes. Using `volatile` on a struct field does not compile. The `long` (UTC ticks) encodes the full timestamp and is atomically read/written on all supported platforms.

### Pattern 3: OtelMetricHandler Stamp Point

Stamp after successful gauge dispatch for the heartbeat device. The check goes in the `Counter32` / `Counter64` / numeric case because `HeartbeatJobOptions.HeartbeatOid` carries a `Counter32` value.

```csharp
// In OtelMetricHandler constructor — add IHeartbeatLivenessService
private readonly IHeartbeatLivenessService _heartbeatLiveness;

public OtelMetricHandler(
    ISnmpMetricFactory metricFactory,
    PipelineMetricService pipelineMetrics,
    IHeartbeatLivenessService heartbeatLiveness,
    ILogger<OtelMetricHandler> logger)
{
    _metricFactory = metricFactory;
    _pipelineMetrics = pipelineMetrics;
    _heartbeatLiveness = heartbeatLiveness;
    _logger = logger;
}

// In Handle(), after _pipelineMetrics.IncrementHandled(deviceName) in the numeric case:
if (deviceName == HeartbeatJobOptions.HeartbeatDeviceName)
    _heartbeatLiveness.Stamp();
```

The stamp goes AFTER `_pipelineMetrics.IncrementHandled(deviceName)` — proving the full handler logic ran. The `deviceName` local variable is already set from `notification.DeviceName ?? "unknown"` at line 37, so the comparison is correct even if `DeviceName` is null (it would be "unknown", not "Simetra").

### Pattern 4: LivenessHealthCheck Extension

`LivenessHealthCheck` currently checks job completion stamps via `ILivenessVectorService`. Add pipeline arrival check using `IHeartbeatLivenessService`. The threshold is `HeartbeatJobOptions.DefaultIntervalSeconds × _graceMultiplier` (reuses existing `LivenessOptions.GraceMultiplier`).

```csharp
// New constructor parameters
private readonly IHeartbeatLivenessService _heartbeatLiveness;
private readonly int _heartbeatIntervalSeconds;

public LivenessHealthCheck(
    ILivenessVectorService liveness,
    IJobIntervalRegistry intervals,
    IOptions<LivenessOptions> options,
    IHeartbeatLivenessService heartbeatLiveness,
    IOptions<HeartbeatJobOptions> heartbeatOptions,
    ILogger<LivenessHealthCheck> logger)
{
    _liveness = liveness;
    _intervals = intervals;
    _graceMultiplier = options.Value.GraceMultiplier;
    _heartbeatLiveness = heartbeatLiveness;
    _heartbeatIntervalSeconds = heartbeatOptions.Value.IntervalSeconds;
    _logger = logger;
}

// In CheckHealthAsync, after the existing job-stamp loop:
var pipelineThreshold = TimeSpan.FromSeconds(_heartbeatIntervalSeconds * _graceMultiplier);
var pipelineArrival = _heartbeatLiveness.LastArrival;

if (pipelineArrival is null)
{
    // Never arrived — only stale if we're past startup grace window
    // (same threshold: if no heartbeat within 2× interval, something is wrong)
    // Use process start time as baseline: if uptime > threshold and never stamped → unhealthy
    // Simplest correct approach: treat "never" as epoch (age = ∞ → always stale after threshold)
    staleEntries["pipeline-heartbeat"] = new { ageSeconds = double.MaxValue, thresholdSeconds = pipelineThreshold.TotalSeconds, lastStamp = (string?)null, stale = true };
    allEntries["pipeline-heartbeat"] = staleEntries["pipeline-heartbeat"];
}
else
{
    var pipelineAge = now - pipelineArrival.Value;
    var pipelineEntry = new
    {
        ageSeconds = Math.Round(pipelineAge.TotalSeconds, 1),
        thresholdSeconds = pipelineThreshold.TotalSeconds,
        lastStamp = pipelineArrival.Value.ToString("O"),
        stale = pipelineAge > pipelineThreshold
    };
    allEntries["pipeline-heartbeat"] = pipelineEntry;
    if (pipelineAge > pipelineThreshold)
        staleEntries["pipeline-heartbeat"] = pipelineEntry;
}
```

**Note on "never stamped" behavior:** The phase decision says `> DefaultIntervalSeconds × 2.0 → unhealthy`. If `LastArrival` is null (process just started), immediately marking unhealthy would cause spurious startup failures. The practical approach: use `DateTimeOffset.UtcNow` minus a startup timestamp. However, to keep it simple and consistent with the existing pattern (which also never fires before the first job run), the cleanest approach is to **skip the check when null** during the startup window — but this requires knowing uptime. Alternatively, **treat null as "never arrived, always stale"** and rely on K8s `failureThreshold=3` (45s) to absorb the startup period. Since `HeartbeatJob` starts immediately (`StartNow()`) and the heartbeat interval is 15s, the first stamp should arrive within 15–30s of startup, well within the K8s failure threshold window.

The planner should decide: "null = immediately stale" vs "null = skip check". The phase context says `now - lastArrival > threshold → unhealthy`, implying null means never arrived which is unhealthy if past threshold. Recommend: null → treat as `DateTimeOffset.UnixEpoch` (age = huge → stale). This is consistent with the threshold math.

### Pattern 5: DI Registration in AddSnmpPipeline

```csharp
// After the existing ILivenessVectorService registration:
services.AddSingleton<IHeartbeatLivenessService, HeartbeatLivenessService>();
```

`LivenessHealthCheck` is registered via `AddCheck<LivenessHealthCheck>` in `AddSnmpHealthChecks`. DI automatically injects the new constructor parameters since both `IHeartbeatLivenessService` and `IOptions<HeartbeatJobOptions>` are already registered (`HeartbeatJobOptions` is bound in `AddSnmpConfiguration`).

### Pattern 6: OtelMetricHandler Test Construction

The existing `OtelMetricHandlerTests` creates `OtelMetricHandler` directly with `new`:

```csharp
_handler = new OtelMetricHandler(
    _testFactory,
    _pipelineMetrics,
    NullLogger<OtelMetricHandler>.Instance);
```

After this phase it becomes:

```csharp
_heartbeatLiveness = new HeartbeatLivenessService();
_handler = new OtelMetricHandler(
    _testFactory,
    _pipelineMetrics,
    _heartbeatLiveness,
    NullLogger<OtelMetricHandler>.Instance);
```

All existing tests pass unchanged (stamp call is additive, not breaking). New tests verify stamp behavior.

### Anti-Patterns to Avoid

- **Stamping in HeartbeatJob.Execute:** Explicitly prohibited by phase decisions. The `_liveness.Stamp(jobKey)` in `finally` tests job execution, not pipeline flow. Do not add a second stamp there.
- **Stamping in TenantVectorFanOutBehavior:** Heartbeat is not in DeviceRegistry so fan-out silently skips it. Would not reliably fire.
- **Stamping in ValidationBehavior:** Fires too early — does not prove the full handler ran.
- **Using `volatile DateTimeOffset?` directly:** Not valid C# — structs cannot be `volatile`. Use `volatile long` (ticks) as shown in Pattern 2.
- **Stamping in both Counter32 and OctetString cases:** Heartbeat OID carries `Counter32` only. The stamp only needs to be in the numeric case. Adding it to both is harmless but unnecessary.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Thread-safe single timestamp | Lock or mutex | `volatile long` + `Volatile.Read/Write` | Lock-free, correct, zero contention |
| Stale detection | Custom timer | Existing threshold math (`interval × graceMultiplier`) | Already tested in `LivenessHealthCheck` |

## Common Pitfalls

### Pitfall 1: "Never Stamped" Causes Immediate Unhealthy at Startup

**What goes wrong:** If `LastArrival == null` is treated as maximally stale, the health check returns Unhealthy for the first 15–30s after startup until the first heartbeat pipeline arrival.

**Why it happens:** K8s liveness probe starts immediately. If `initialDelaySeconds` is not set generously, the pod gets killed before the heartbeat arrives.

**How to avoid:** Rely on K8s `initialDelaySeconds` and `failureThreshold`. With `failureThreshold=3` at `periodSeconds=15`, the pod has 45s of consecutive failures before restart. The heartbeat arrives within 15s. Treat null as `DateTimeOffset.UnixEpoch` (always stale) — the K8s margin absorbs it. OR: set `initialDelaySeconds=30` in the probe config (out of scope for this phase).

**Warning signs:** Pod restart loop on fresh deploy before any heartbeat has arrived.

### Pitfall 2: Wrong Stamp Point — Stamping Before Handler Completes

**What goes wrong:** If stamp is placed at the top of `Handle()` before `_metricFactory.RecordGauge(...)`, a metric recording exception would leave the stamp fresh even though the handler failed.

**How to avoid:** Place stamp AFTER `_pipelineMetrics.IncrementHandled(deviceName)`. This is the last operation in the successful case.

### Pitfall 3: `HeartbeatJobOptions.IntervalSeconds` vs `DefaultIntervalSeconds`

**What goes wrong:** Using `DefaultIntervalSeconds` (compile-time const = 15) in `LivenessHealthCheck` when the operator has configured a different `IntervalSeconds` in appsettings. The threshold would be wrong.

**How to avoid:** Inject `IOptions<HeartbeatJobOptions>` and use `options.Value.IntervalSeconds`, not the const. The const is only for scenarios where IOptions is unavailable (like TenantVectorRegistry constructor).

### Pitfall 4: OtelMetricHandlerTests Construction Breaks All Existing Tests

**What goes wrong:** Adding `IHeartbeatLivenessService` to `OtelMetricHandler` constructor without updating the test `new OtelMetricHandler(...)` call causes a compile error, breaking all 20+ existing handler tests.

**How to avoid:** Update `OtelMetricHandlerTests` constructor immediately in the same task as the handler change. The fix is one line: add `new HeartbeatLivenessService()` as the third argument.

### Pitfall 5: LivenessHealthCheck Test Construction Breaks All Existing Tests

**What goes wrong:** Same as Pitfall 4 — adding parameters to `LivenessHealthCheck` breaks `LivenessHealthCheckTests.CreateCheck()`.

**How to avoid:** Update `LivenessHealthCheckTests.CreateCheck()` to supply `IHeartbeatLivenessService` and `IOptions<HeartbeatJobOptions>`. A `NeverStampedHeartbeatService` test double (returns null) can be used for tests that don't care about pipeline liveness.

## Code Examples

### Full HeartbeatLivenessService

```csharp
// Source: in-codebase design, pattern from LivenessVectorService.cs
namespace SnmpCollector.Pipeline;

/// <summary>
/// Thread-safe single-value liveness stamp for heartbeat pipeline arrival.
/// Stamped by OtelMetricHandler when the heartbeat OID completes the pipeline.
/// Uses volatile long (UTC ticks) for lock-free single-value read/write.
/// A ticks value of 0 means no heartbeat has been observed since startup.
/// </summary>
public sealed class HeartbeatLivenessService : IHeartbeatLivenessService
{
    private long _lastArrivalTicks; // 0 = never stamped

    public void Stamp()
    {
        Volatile.Write(ref _lastArrivalTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    public DateTimeOffset? LastArrival
    {
        get
        {
            var ticks = Volatile.Read(ref _lastArrivalTicks);
            return ticks == 0L ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }
}
```

### OtelMetricHandler Diff (numeric case only, other case unchanged)

```csharp
case SnmpType.Integer32:
case SnmpType.Gauge32:
case SnmpType.TimeTicks:
case SnmpType.Counter32:
case SnmpType.Counter64:
    _metricFactory.RecordGauge(...);
    if (notification.PollDurationMs.HasValue)
        _metricFactory.RecordGaugeDuration(...);
    _pipelineMetrics.IncrementHandled(deviceName);
    // NEW: stamp pipeline arrival when heartbeat completes handler
    if (deviceName == HeartbeatJobOptions.HeartbeatDeviceName)
        _heartbeatLiveness.Stamp();
    break;
```

### LivenessHealthCheck Pipeline Check Addition

```csharp
// After the foreach loop over job stamps, before the staleEntries.Count check:
var pipelineThreshold = TimeSpan.FromSeconds(_heartbeatIntervalSeconds * _graceMultiplier);
var pipelineArrival = _heartbeatLiveness.LastArrival;
var pipelineAge = pipelineArrival.HasValue
    ? now - pipelineArrival.Value
    : now - DateTimeOffset.UnixEpoch; // null → treat as epoch → always stale

var pipelineEntry = new
{
    ageSeconds = pipelineArrival.HasValue ? Math.Round(pipelineAge.TotalSeconds, 1) : (double?)null,
    thresholdSeconds = pipelineThreshold.TotalSeconds,
    lastStamp = pipelineArrival?.ToString("O"),
    stale = pipelineAge > pipelineThreshold
};

allEntries["pipeline-heartbeat"] = pipelineEntry;
if (pipelineAge > pipelineThreshold)
    staleEntries["pipeline-heartbeat"] = pipelineEntry;
```

### Test: OtelMetricHandler stamps on heartbeat

```csharp
[Fact]
public async Task Heartbeat_StampsPipelineLiveness()
{
    var heartbeatLiveness = new HeartbeatLivenessService();
    // Rebuild handler with real heartbeat liveness service
    var handler = new OtelMetricHandler(_testFactory, _pipelineMetrics, heartbeatLiveness, NullLogger<OtelMetricHandler>.Instance);

    var notification = new SnmpOidReceived
    {
        Oid = HeartbeatJobOptions.HeartbeatOid,
        AgentIp = IPAddress.Parse("127.0.0.1"),
        Value = new Counter32(1),
        Source = SnmpSource.Trap,
        TypeCode = SnmpType.Counter32,
        DeviceName = HeartbeatJobOptions.HeartbeatDeviceName,
        MetricName = "Heartbeat",
        ExtractedValue = 1.0
    };

    Assert.Null(heartbeatLiveness.LastArrival); // before
    await handler.Handle(notification, CancellationToken.None);
    Assert.NotNull(heartbeatLiveness.LastArrival); // after
}

[Fact]
public async Task NonHeartbeat_DoesNotStampPipelineLiveness()
{
    var heartbeatLiveness = new HeartbeatLivenessService();
    var handler = new OtelMetricHandler(_testFactory, _pipelineMetrics, heartbeatLiveness, NullLogger<OtelMetricHandler>.Instance);

    var notification = MakeNotification(new Integer32(42), SnmpType.Integer32, deviceName: "some-router");
    await handler.Handle(notification, CancellationToken.None);

    Assert.Null(heartbeatLiveness.LastArrival); // NOT stamped
}
```

## Requirements Coverage

| Requirement | Implementation |
|-------------|----------------|
| HB-04: New `IHeartbeatLivenessService` stamps when heartbeat completes pipeline | `HeartbeatLivenessService.Stamp()` called from `OtelMetricHandler` |
| HB-05: Stamp point is `OtelMetricHandler` when `DeviceName == HeartbeatDeviceName` | Explicit `if (deviceName == HeartbeatJobOptions.HeartbeatDeviceName)` check |
| HB-06: Liveness check `now - lastArrival > interval × 2.0 → unhealthy` | `LivenessHealthCheck` extended with `_heartbeatLiveness.LastArrival` check |
| HB-07: `ILivenessVectorService.Stamp()` in `HeartbeatJob.finally` unchanged | `HeartbeatJob.cs` not touched |
| HB-08: `HeartbeatJob`, OID, Source=Trap unchanged | No changes to those files |
| HB-09: OidMapService heartbeat seed unchanged | `OidMapService.cs` not touched |
| HB-10: Phase 43 removed heartbeat bypass — verify nothing broken | Test `Heartbeat_ExportedAsGauge_WithSimetraDevice` in `OtelMetricHandlerTests` already covers this |

## Open Questions

1. **"Never stamped" behavior at startup**
   - What we know: Phase decisions say `now - lastArrival > threshold → unhealthy`. Null means never arrived.
   - What's unclear: Whether to treat null as epoch (immediately stale) or skip check for first N seconds.
   - Recommendation: Treat null as `DateTimeOffset.UnixEpoch` (always stale). K8s `failureThreshold=3` + `periodSeconds=15` provides 45s of margin. HeartbeatJob fires at startup (`StartNow()`), so first arrival within 15–30s. No pod restart loop expected.

2. **`ageSeconds` for null arrival in diagnostic data**
   - What we know: The existing pattern uses `Math.Round(age.TotalSeconds, 1)`.
   - What's unclear: What to put in the data dictionary when `LastArrival` is null (age is effectively infinite).
   - Recommendation: Use `(double?)null` for `ageSeconds` and `(string?)null` for `lastStamp` when never stamped. This is human-readable in the K8s health check JSON response.

## Sources

### Primary (HIGH confidence)
- Direct codebase reading — all findings verified against actual source files
- `src/SnmpCollector/Pipeline/ILivenessVectorService.cs` — interface pattern for `IHeartbeatLivenessService`
- `src/SnmpCollector/Pipeline/LivenessVectorService.cs` — impl pattern (ConcurrentDictionary → single volatile long)
- `src/SnmpCollector/HealthChecks/LivenessHealthCheck.cs` — extension pattern for pipeline check
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` — exact stamp insertion point
- `src/SnmpCollector/Configuration/HeartbeatJobOptions.cs` — `DefaultIntervalSeconds = 15`, `HeartbeatDeviceName = "Simetra"`
- `src/SnmpCollector/Jobs/HeartbeatJob.cs` — confirmed `_liveness.Stamp(jobKey)` in `finally` stays
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — DI registration point (`AddSnmpPipeline`)
- `tests/SnmpCollector.Tests/HealthChecks/LivenessHealthCheckTests.cs` — test pattern for health check tests
- `tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs` — test pattern; existing `Heartbeat_ExportedAsGauge_WithSimetraDevice` test covers HB-10

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new libraries, all existing code read directly
- Architecture: HIGH — patterns derived from existing `ILivenessVectorService`, `LivenessVectorService`, and `LivenessHealthCheck`
- Pitfalls: HIGH — based on reading actual code and test construction patterns

**Research date:** 2026-03-15
**Valid until:** Stable (no external dependencies)
