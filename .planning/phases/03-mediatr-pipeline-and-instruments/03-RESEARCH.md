# Phase 3: MediatR Pipeline and Instruments - Research

**Researched:** 2026-03-05
**Domain:** MediatR 12.5.0 behavior pipeline, SharpSnmpLib ISnmpData/SnmpType dispatch, System.Diagnostics.Metrics instruments, pipeline counter service
**Confidence:** HIGH

---

## Summary

Phase 3 adds all MediatR-specific infrastructure and all six OTel instruments (3 business + 6 pipeline counters) to the SnmpCollector project. Two NuGet packages not yet present in the project must be added: `MediatR 12.5.0` and `Lextm.SharpSnmpLib 12.5.7`. The notification shape (`SnmpOidReceived` as mutable class), behavior order, and instrument taxonomy are fully locked in CONTEXT.md — research was directed at verifying mechanics rather than exploring options.

The standard MediatR pattern for this use case is: open generic behaviors registered via `AddOpenBehavior()` in DI-registration order (first registered = outermost in the chain with Microsoft DI), `TaskWhenAllPublisher` set via `config.NotificationPublisher = new TaskWhenAllPublisher()`, and `INotificationHandler<SnmpOidReceived>` for terminal metric recording. The behavior chain is Logging → Exception → Validation → OidResolution, with `OtelMetricHandler` as the terminal handler. SharpSnmpLib's `SnmpType` enum (accessed via `ISnmpData.TypeCode`) maps directly to the three instrument buckets without any intermediate conversion.

The pipeline metric counters follow the same singleton service pattern as Simetra's `PipelineMetricService`: one singleton class owns the `Meter`, pre-creates all instruments in the constructor, and exposes typed methods (e.g., `IncrementPublished()`, `IncrementHandled()`). The `MetricFactory` for business instruments uses a `ConcurrentDictionary<string, object>` cache keyed by instrument name, identical to the Simetra reference.

**Primary recommendation:** Follow the Simetra `MetricFactory` + `PipelineMetricService` patterns verbatim; add MediatR and SharpSnmpLib to the project file; wire everything via a new `AddSnmpPipeline()` extension method on `IServiceCollection`.

---

## Standard Stack

### Core (packages not yet in SnmpCollector.csproj)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| MediatR | 12.5.0 | Behavior pipeline, notification dispatch, `IPublisher.Publish()` | Locked by [Init] decision — MIT license, last free OSS version |
| Lextm.SharpSnmpLib | 12.5.7 | `ISnmpData`, `SnmpType` enum, typed value access on the notification | Already in Simetra reference; needed for `SnmpOidReceived.Value` and `TypeCode` fields |

### Already in Project (no additions needed)

| Library | Version | Purpose |
|---------|---------|---------|
| System.Diagnostics.Metrics | (BCL .NET 9) | `Meter`, `Gauge<T>`, `Counter<T>` — creates `snmp_gauge`, `snmp_counter`, `snmp_info` |
| Microsoft.Extensions.Hosting | 9.0.0 | `IMeterFactory` injected into MetricFactory |
| OpenTelemetry SDK | 1.15.0 | MeterProvider subscribes to `SnmpCollector` meter by name (already configured in Phase 1) |

### Installation (additions to SnmpCollector.csproj)

```bash
dotnet add src/SnmpCollector/SnmpCollector.csproj package MediatR --version 12.5.0
dotnet add src/SnmpCollector/SnmpCollector.csproj package Lextm.SharpSnmpLib --version 12.5.7
```

And for the test project (for `Variable` and `ISnmpData` construction in tests):

```bash
dotnet add tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj package Lextm.SharpSnmpLib --version 12.5.7
```

---

## Architecture Patterns

### Recommended Project Structure (new files Phase 3 adds)

```
src/SnmpCollector/
├── Pipeline/
│   ├── SnmpOidReceived.cs          # INotification (mutable class)
│   ├── SnmpSource.cs               # enum: Poll, Trap
│   ├── Behaviors/
│   │   ├── LoggingBehavior.cs      # outermost: logs OID+IP+source at Debug
│   │   ├── ExceptionBehavior.cs    # catch-all: never propagates, logs Warning
│   │   ├── ValidationBehavior.cs   # OID regex + IPAddress.TryParse
│   │   └── OidResolutionBehavior.cs # sets MetricName from IOidMapService
│   └── Handlers/
│       └── OtelMetricHandler.cs    # INotificationHandler<SnmpOidReceived>
├── Telemetry/
│   ├── SnmpMetricFactory.cs        # creates/caches snmp_gauge, snmp_counter, snmp_info
│   └── PipelineMetricService.cs    # owns the 6 pipeline counters
└── Extensions/
    └── ServiceCollectionExtensions.cs  # add AddSnmpPipeline() method
```

### Pattern 1: MediatR AddOpenBehavior with Notification Pipeline

**What:** Open generic behaviors registered in the order they wrap the pipeline. With Microsoft.Extensions.DependencyInjection, first-registered = outermost (pre-processing runs first, post-processing runs last). MediatR internally reverses the `IEnumerable<IPipelineBehavior<,>>` before chaining, which means the first-registered item in DI becomes the outermost wrapper.

**Behavior registration order for PIPE-08 (Logging → Exception → Validation → OidResolution):**

```csharp
// Source: verified via github.com/jbogard/MediatR.Extensions.Microsoft.DependencyInjection/issues/105
// First registered = outermost (runs first pre-handler, last post-handler)
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<SnmpOidReceived>();
    cfg.NotificationPublisher = new TaskWhenAllPublisher();   // PIPE-09
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));          // 1st = outermost
    cfg.AddOpenBehavior(typeof(ExceptionBehavior<,>));        // 2nd
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));       // 3rd
    cfg.AddOpenBehavior(typeof(OidResolutionBehavior<,>));    // 4th = innermost
});
```

**Critical:** `AddOpenBehavior` uses `typeof(Behavior<,>)` syntax (open generic) — not `typeof(Behavior<SnmpOidReceived, Unit>)`. The framework closes the generic at resolve time.

**Source:** MediatR GitHub issue #105 confirms with Microsoft DI that behaviors are reversed before aggregation, making first-registered = outermost.

### Pattern 2: SnmpOidReceived as Mutable Class (Behavior Enrichment)

**What:** `SnmpOidReceived` is a class (not record) so behaviors can set `MetricName` in-place as the notification flows through the chain.

```csharp
// Source: CONTEXT.md decisions — mutable class, standard MediatR notification pattern
public sealed class SnmpOidReceived : INotification
{
    public required string Oid { get; init; }
    public required IPAddress AgentIp { get; set; }
    public string? DeviceName { get; set; }   // poll path sets at publish time
    public required ISnmpData Value { get; init; }
    public required SnmpSource Source { get; init; }
    public required SnmpType TypeCode { get; init; }
    public string? MetricName { get; set; }   // null until OidResolutionBehavior runs
}
```

Note: `AgentIp` is `set` not `init` because the trap path may not have it at construction time. `DeviceName` is nullable and set either at publish (poll path) or by a future behavior.

### Pattern 3: IPipelineBehavior Skeleton (Correct Generic Signature for INotification)

MediatR 12's `IPipelineBehavior<TNotification, TResponse>` has `TResponse = Unit` for notifications.

```csharp
// Source: MediatR 12.x API — INotification handlers use Unit response
public sealed class LoggingBehavior<TNotification, TResponse>
    : IPipelineBehavior<TNotification, TResponse>
    where TNotification : INotification
{
    private readonly ILogger<LoggingBehavior<TNotification, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TNotification, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(
        TNotification notification,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (notification is SnmpOidReceived msg)
        {
            _logger.LogDebug(
                "SnmpOidReceived OID={Oid} Agent={Agent} Source={Source}",
                msg.Oid, msg.AgentIp, msg.Source);
        }
        return await next();
    }
}
```

**Important:** For `INotification` (not `IRequest`), the generic constraint must be `where TNotification : INotification`. Do NOT use `where TNotification : IRequest<TResponse>`.

### Pattern 4: TaskWhenAllPublisher — Error Isolation

**What:** Setting `cfg.NotificationPublisher = new TaskWhenAllPublisher()` means all `INotificationHandler<SnmpOidReceived>` implementations run concurrently via `Task.WhenAll`. An exception in one handler does NOT prevent others from running.

```csharp
// Setting the instance directly makes it a Singleton (correct for this use case)
cfg.NotificationPublisher = new TaskWhenAllPublisher();

// Alternatively, set type (uses ServiceLifetime from cfg.ServiceLifetime):
// cfg.NotificationPublisherType = typeof(TaskWhenAllPublisher);
```

For Phase 3 there is only one handler (`OtelMetricHandler`), so parallel dispatch provides no immediate benefit, but is required by PIPE-09 for future multi-handler scenarios and for correct error isolation.

**CRITICAL:** `TaskWhenAllPublisher` fires handlers in parallel — if Phase 3 adds two handlers they share the same DI scope. Phase 3 has only `OtelMetricHandler` so this is not an issue yet.

### Pattern 5: TypeCode-to-Instrument Dispatch in OtelMetricHandler

```csharp
// Source: CONTEXT.md decisions — SnmpType enum from SharpSnmpLib
// Source: help.sharpsnmp.com SnmpType enumeration

private void Dispatch(SnmpOidReceived notification)
{
    switch (notification.TypeCode)
    {
        case SnmpType.Integer32:
        case SnmpType.Gauge32:
        case SnmpType.TimeTicks:
            RecordGauge(notification);
            break;

        case SnmpType.Counter32:
        case SnmpType.Counter64:
            // Phase 3: pass through, do NOT record — delta engine (Phase 4) must exist first
            // Log at Debug: "Counter OID {Oid} deferred to Phase 4 delta engine"
            break;

        case SnmpType.OctetString:
        case SnmpType.IPAddress:
        case SnmpType.ObjectIdentifier:
            RecordInfo(notification);
            break;

        default:
            _logger.LogWarning(
                "Unrecognized SnmpType {TypeCode} for OID {Oid}, dropping",
                notification.TypeCode, notification.Oid);
            break;
    }
}
```

**SnmpType integer codes** (from SharpSnmpLib official docs — HIGH confidence):
- `Integer32` = 2
- `OctetString` = 4
- `ObjectIdentifier` = 6
- `IPAddress` = 64
- `Counter32` = 65
- `Gauge32` = 66
- `TimeTicks` = 67
- `Counter64` = 70

**Note:** `Unsigned32` (code 71) maps to `Gauge32` in SharpSnmpLib's DataFactory — it arrives as `SnmpType.Gauge32` after deserialization, not as a distinct `Unsigned32` type code. Not a concern for dispatch.

### Pattern 6: MetricFactory Instrument Caching (from Simetra reference)

The Simetra `MetricFactory` pattern: `ConcurrentDictionary<string, object>` keyed by instrument name, `GetOrAdd` on every notification, `IMeterFactory` injection.

```csharp
// Source: src/Simetra/Pipeline/MetricFactory.cs (read directly — HIGH confidence)
public sealed class SnmpMetricFactory : ISnmpMetricFactory
{
    private readonly Meter _meter;
    private readonly string _siteName;
    private readonly ConcurrentDictionary<string, object> _instruments = new();

    public SnmpMetricFactory(
        IMeterFactory meterFactory,
        IOptions<SiteOptions> siteOptions)
    {
        _meter = meterFactory.Create(TelemetryConstants.MeterName);  // "SnmpCollector"
        _siteName = siteOptions.Value.Name;
    }

    private Gauge<double> GetOrCreateGauge(string name) =>
        (Gauge<double>)_instruments.GetOrAdd(name, n => _meter.CreateGauge<double>(n));

    private Counter<double> GetOrCreateCounter(string name) =>
        (Counter<double>)_instruments.GetOrAdd(name, n => _meter.CreateCounter<double>(n));
}
```

**Key:** The `_meter` is created from `IMeterFactory` (not `new Meter()`), which is the DI-friendly way that ties meter lifetime to the DI container. The same `TelemetryConstants.MeterName = "SnmpCollector"` meter is already registered in `AddSnmpTelemetry()` via `metrics.AddMeter(TelemetryConstants.MeterName)`.

### Pattern 7: snmp_info Instrument Implementation

Per CONTEXT.md decisions, `snmp_info` is a `Gauge<double>` always recording value `1.0` with the string representation in a `value` label truncated at 128 characters.

```csharp
// Source: CONTEXT.md decision — gauge=1 with string in value label
// Source: METR-03 requirement
private void RecordInfo(SnmpOidReceived n)
{
    var rawValue = n.Value.ToString() ?? string.Empty;
    var truncated = rawValue.Length > 128
        ? rawValue[..125] + "..."
        : rawValue;

    var gauge = GetOrCreateGauge("snmp_info");
    gauge.Record(1.0, new TagList
    {
        { "site_name",   _siteName },
        { "metric_name", n.MetricName ?? OidMapService.Unknown },
        { "oid",         n.Oid },
        { "agent",       n.DeviceName ?? n.AgentIp.ToString() },
        { "source",      n.Source.ToString().ToLowerInvariant() },
        { "value",       truncated }   // 6th label — info only
    });
}
```

### Pattern 8: PipelineMetricService (6 pipeline counters)

Follows the same singleton service pattern as Simetra's `PipelineMetricService`. One singleton class owns the `Meter` and all instruments. Typed record methods.

```csharp
// Source: src/Simetra/Telemetry/PipelineMetricService.cs (read directly — HIGH confidence)
// Adapted: uses SnmpCollector meter, simpler label (site_name only per PMET-07)
public sealed class PipelineMetricService : IDisposable
{
    private readonly Meter _meter;
    private readonly string _siteName;
    private readonly Counter<long> _published;
    private readonly Counter<long> _handled;
    private readonly Counter<long> _errors;
    private readonly Counter<long> _rejected;
    private readonly Counter<long> _pollExecuted;
    private readonly Counter<long> _trapReceived;

    public PipelineMetricService(IMeterFactory meterFactory, IOptions<SiteOptions> siteOptions)
    {
        _siteName = siteOptions.Value.Name;
        _meter = meterFactory.Create(TelemetryConstants.MeterName);
        _published    = _meter.CreateCounter<long>("snmp.event.published");
        _handled      = _meter.CreateCounter<long>("snmp.event.handled");
        _errors       = _meter.CreateCounter<long>("snmp.event.errors");
        _rejected     = _meter.CreateCounter<long>("snmp.event.rejected");
        _pollExecuted = _meter.CreateCounter<long>("snmp.poll.executed");
        _trapReceived = _meter.CreateCounter<long>("snmp.trap.received");
    }

    // PMET-01: incremented at Publish() call site
    public void IncrementPublished() =>
        _published.Add(1, new TagList { { "site_name", _siteName } });

    // PMET-02: incremented by OtelMetricHandler on successful recording
    public void IncrementHandled() =>
        _handled.Add(1, new TagList { { "site_name", _siteName } });

    // PMET-03: flat counter, no error_type tag (per CONTEXT.md decision)
    public void IncrementErrors() =>
        _errors.Add(1, new TagList { { "site_name", _siteName } });

    // PMET-04: incremented by ValidationBehavior on reject
    public void IncrementRejected() =>
        _rejected.Add(1, new TagList { { "site_name", _siteName } });

    // PMET-05: for Phase 6 (poll jobs) — instrument created here, used later
    public void IncrementPollExecuted() =>
        _pollExecuted.Add(1, new TagList { { "site_name", _siteName } });

    // PMET-06: for Phase 5 (trap listener) — instrument created here, used later
    public void IncrementTrapReceived() =>
        _trapReceived.Add(1, new TagList { { "site_name", _siteName } });

    public void Dispose() => _meter.Dispose();
}
```

**Important:** `snmp.event.published` must be incremented at the `IPublisher.Publish()` call site, NOT inside any behavior. This means the call site in Phase 5/6 will call `_pipelineMetrics.IncrementPublished()` before or after `await _publisher.Publish(...)`. For Phase 3 testing purposes, tests simulate this by calling the method directly.

### Pattern 9: Behavior-Specific Responsibilities

| Behavior | Core Logic | Increments Metric | Pipeline Action |
|----------|-----------|------------------|-----------------|
| LoggingBehavior | LogDebug OID+IP+Source | None | Always calls `next()` |
| ExceptionBehavior | try/catch around `next()`, LogWarning on catch | `IncrementErrors()` | Swallows exception, never re-throws |
| ValidationBehavior | OID regex `^\d+(\.\d+){1,}$`; `IPAddress.TryParse` | `IncrementRejected()` | Returns without calling `next()` on failure |
| OidResolutionBehavior | `IOidMapService.Resolve(oid)` → sets `MetricName` | None | Always calls `next()` |

**OID regex rationale:** Pattern `^\d+(\.\d+){1,}$` requires at least 2 arcs (e.g., `1.3`). The `+` after the group means one or more repetitions, ensuring minimum-2-arc constraint from CONTEXT.md. The `{1,}` is equivalent to `+`.

**Validation rejection log format (CONTEXT.md decision):** Log OID + IP + reason at Warning level:
```csharp
_logger.LogWarning(
    "Rejecting SnmpOidReceived: OID={Oid} Agent={Agent} Reason={Reason}",
    notification.Oid,
    notification.AgentIp,
    "InvalidOidFormat");  // or "UnknownDevice" or "InvalidIpFormat"
```

**Unknown device rejection:** If `DeviceName` is null AND the IP is not in `IDeviceRegistry`, reject with Warning. For the trap path (Phase 5), the listener will set `DeviceName` only for known devices — the behavior is a safety net for notifications that slip through.

### Pattern 10: SnmpSource Enum (Claude's Discretion)

**Recommendation:** Use a dedicated `SnmpSource` enum in `SnmpCollector/Pipeline/SnmpSource.cs`. String constants are error-prone (typos, case sensitivity). Enum provides compile-time safety and integrates cleanly with label serialization via `.ToString()`.

```csharp
// Source: Claude's Discretion — enum recommended over string constants
// Parallels Simetra's MetricPollSource enum pattern
namespace SnmpCollector.Pipeline;

/// <summary>
/// Indicates the origination path of an <see cref="SnmpOidReceived"/> notification.
/// Not serialized to/from configuration — set programmatically by the publish call site.
/// </summary>
public enum SnmpSource
{
    Poll,
    Trap
}
```

Label value: `n.Source.ToString().ToLowerInvariant()` → `"poll"` or `"trap"`.

### Anti-Patterns to Avoid

- **Creating instruments in OtelMetricHandler.Handle():** Each call creates a new instrument. Use `ConcurrentDictionary.GetOrAdd()` in a dedicated factory. (Source: Anti-Pattern 4 from Architecture research, confirmed by Simetra MetricFactory pattern.)
- **Registering behaviors with specific generic types:** `typeof(LoggingBehavior<SnmpOidReceived, Unit>)` instead of `typeof(LoggingBehavior<,>)`. The open generic form is required for `AddOpenBehavior()`.
- **Not constraining the open generic behavior with `where TNotification : INotification`:** Without the constraint the behavior may also apply to request types. Add the constraint explicitly.
- **Using `cfg.NotificationPublisherType` when you want Singleton scope:** The type form uses `cfg.ServiceLifetime` (default: Transient). Setting the instance directly (`cfg.NotificationPublisher = new TaskWhenAllPublisher()`) makes it a Singleton — correct for a stateless publisher.
- **Wrapping `AgentIp.ToString()` inside TagList on every call without caching:** `TagList` is a value type; it is cheap to create. Do not pre-cache `TagList` instances — they hold item arrays that must be fresh per-measurement.
- **Calling `_meter.CreateGauge()` twice for the same name on the same meter:** The SDK throws or warns on duplicate instrument names for the same meter. `ConcurrentDictionary.GetOrAdd()` prevents this race.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Behavior pipeline ordering | Custom middleware chain | MediatR `AddOpenBehavior()` + DI registration order | Well-tested; ordering semantics documented and verified |
| Parallel handler dispatch | `Task.WhenAll()` inside custom publisher | `TaskWhenAllPublisher` (built-in MediatR 12+) | Handles `AggregateException`; integrates with DI lifecycle |
| SNMP type code dispatch | String-based OID type lookups | `ISnmpData.TypeCode` (SharpSnmpLib `SnmpType` enum) | Type is authoritative — comes from the wire, already parsed |
| Instrument caching | `static Dictionary` or lazy field | `ConcurrentDictionary<string, object>.GetOrAdd()` | Thread-safe; handles concurrent first-call races from parallel handlers |
| OID regex validation | Recursive parser or OID tree traversal | `Regex.IsMatch(oid, @"^\d+(\.\d+){1,}$")` | CONTEXT.md specifies simple dot-notation; no RFC 2578 compliance required |

**Key insight:** MediatR's behavior pipeline already solves ordering, error isolation, and registration — do not replicate these concerns inside behaviors or the handler.

---

## Common Pitfalls

### Pitfall 1: Behavior Generic Constraint Mismatch

**What goes wrong:** Using `IPipelineBehavior<TNotification, TResponse> where TNotification : IRequest<TResponse>` instead of `where TNotification : INotification`. The behavior will never fire for notifications.

**Why it happens:** MediatR tutorials focus on request/response (CQRS commands/queries). Notification pipeline behaviors need the `INotification` constraint.

**How to avoid:** Always write:
```csharp
where TNotification : INotification
```

**Warning signs:** Behavior type registered, MediatR configured, but no behavior logs appear during testing.

### Pitfall 2: Registration Order Produces Wrong Execution Order

**What goes wrong:** Logging → Exception → Validation → OidResolution is the correct outer-to-inner order. Registering in reverse produces OidResolution first (before validation), meaning `MetricName` is set on notifications that will be rejected.

**Why it happens:** Confusion between "outermost = runs first pre-handler" and DI registration order.

**How to avoid:** Register in the order you want execution to BEGIN (not end):
```
AddOpenBehavior(LoggingBehavior)     // outermost → runs first
AddOpenBehavior(ExceptionBehavior)   // second
AddOpenBehavior(ValidationBehavior)  // third
AddOpenBehavior(OidResolutionBehavior) // innermost → closest to handler
```

**Verified:** First-registered = outermost in Microsoft.Extensions.DependencyInjection (MediatR reverses the resolved `IEnumerable` before chaining). Source: github.com/jbogard/MediatR.Extensions.Microsoft.DependencyInjection/issues/105.

### Pitfall 3: ExceptionBehavior Must Wrap `next()` — Not the Whole Behavior Body

**What goes wrong:** Placing try/catch around code BEFORE `next()` instead of around the `next()` call itself. Exceptions in the behaviors AFTER ExceptionBehavior (Validation, OidResolution, Handler) will not be caught.

**How to avoid:**
```csharp
// CORRECT — wraps the entire downstream pipeline
public async Task<TResponse> Handle(TNotification n, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
{
    try
    {
        return await next();  // everything downstream is inside this try
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Pipeline exception for {Type}", typeof(TNotification).Name);
        _pipelineMetrics.IncrementErrors();
        return default!;  // Unit.Value for INotification
    }
}
```

### Pitfall 4: Double-Counting `snmp.event.published` if Incremented Inside a Behavior

**What goes wrong:** Incrementing `snmp.event.published` inside LoggingBehavior means the counter fires once per notification AND once for each retry or re-publish. The CONTEXT.md decision places this increment at the `Publish()` call site.

**How to avoid:** `snmp.event.published` is incremented by the code that calls `_publisher.Publish(new SnmpOidReceived(...))` — Phase 5 (trap path) and Phase 6 (poll path). Do not put it in any behavior.

### Pitfall 5: snmp_info `value` Label Cardinality Risk

**What goes wrong:** OctetString values from devices can be arbitrarily long strings (firmware descriptions, sysDescr). Adding them as labels without truncation creates unbounded cardinality.

**How to avoid:** Truncate at 128 characters with `"..."` suffix. This is a CONTEXT.md locked decision. Implement defensively:
```csharp
var truncated = rawValue.Length > 128 ? rawValue[..125] + "..." : rawValue;
```

### Pitfall 6: OtelMetricHandler Not Receiving ISnmpMetricFactory or PipelineMetricService

**What goes wrong:** These are Singletons. If registered as Transient (the default MediatR handler lifetime), they are re-created per request, potentially causing the instrument cache to be reset.

**How to avoid:** `OtelMetricHandler` itself can be Transient (it holds no state). `SnmpMetricFactory` and `PipelineMetricService` MUST be registered as `AddSingleton`. The `ConcurrentDictionary` instrument cache lives on the factory singleton — transient handlers accessing it is safe.

### Pitfall 7: MediatR Does Not Register IPublisher/IMediator Automatically for Notification-Only Use

**What goes wrong:** `AddMediatR` with `RegisterServicesFromAssemblyContaining<T>()` scans the assembly for handlers and behaviors. If no `IRequest`/`IRequestHandler` types exist (Phase 3 is notification-only), MediatR still registers correctly. However, `IPublisher` (simpler interface, notifications only) vs `IMediator` (full interface) matters — both are registered; prefer injecting `IPublisher` at publish call sites.

**How to avoid:** Inject `IPublisher` (not `IMediator`) at the Publish call sites to express intent.

---

## Code Examples

### SnmpOidReceived Notification (complete shape)

```csharp
// Source: CONTEXT.md decisions
using Lextm.SharpSnmpLib;
using MediatR;
using System.Net;

namespace SnmpCollector.Pipeline;

/// <summary>
/// MediatR notification published for every SNMP OID received from any source.
/// Mutable class: behaviors enrich MetricName in-place as the notification flows through the pipeline.
/// Poll path: sets DeviceName at publish time (knows the device).
/// Trap path: sets AgentIp; DeviceName resolved by future DeviceResolutionBehavior (Phase 5+).
/// </summary>
public sealed class SnmpOidReceived : INotification
{
    public required string Oid { get; init; }
    public required IPAddress AgentIp { get; init; }
    public string? DeviceName { get; set; }
    public required ISnmpData Value { get; init; }
    public required SnmpSource Source { get; init; }
    public required SnmpType TypeCode { get; init; }

    /// <summary>Populated by OidResolutionBehavior. Null until that behavior runs.</summary>
    public string? MetricName { get; set; }
}
```

### ValidationBehavior (OID and IP validation)

```csharp
// Source: CONTEXT.md decisions — simple regex, IPAddress.TryParse
using System.Net;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Pipeline.Behaviors;

public sealed class ValidationBehavior<TNotification, TResponse>
    : IPipelineBehavior<TNotification, TResponse>
    where TNotification : INotification
{
    private static readonly Regex OidPattern =
        new(@"^\d+(\.\d+){1,}$", RegexOptions.Compiled);

    private readonly ILogger<ValidationBehavior<TNotification, TResponse>> _logger;
    private readonly PipelineMetricService _metrics;

    public ValidationBehavior(
        ILogger<ValidationBehavior<TNotification, TResponse>> logger,
        PipelineMetricService metrics)
    {
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<TResponse> Handle(
        TNotification notification,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (notification is not SnmpOidReceived msg)
            return await next();  // pass-through for non-SNMP notifications

        if (!OidPattern.IsMatch(msg.Oid))
        {
            _logger.LogWarning(
                "Rejecting SnmpOidReceived: OID={Oid} Agent={Agent} Reason=InvalidOidFormat",
                msg.Oid, msg.AgentIp);
            _metrics.IncrementRejected();
            return default!;
        }

        // Unknown device check: if DeviceName is null AND IP not recognizable
        // Note: Full device registry lookup is Phase 5 trap path concern;
        // Phase 3 ValidationBehavior validates format only (IPAddress.TryParse already passed via ISnmpData)
        // AgentIp is IPAddress type so format is already validated at construction

        return await next();
    }
}
```

**Note:** IP format validation is implicitly handled by the `IPAddress` type on `AgentIp` — you cannot construct an `IPAddress` from an invalid string. The ValidationBehavior focuses on OID format. Unknown device (IP not in registry) validation may be done in a separate DeviceResolutionBehavior (Phase 5) or in the ValidationBehavior itself by injecting `IDeviceRegistry`. For Phase 3, OID format validation is the primary responsibility.

### DI Registration (AddSnmpPipeline extension method)

```csharp
// Source: follows AddSnmpConfiguration pattern from Phase 2
using MediatR;
using MediatR.NotificationPublishers;
using SnmpCollector.Pipeline;
using SnmpCollector.Pipeline.Behaviors;
using SnmpCollector.Pipeline.Handlers;
using SnmpCollector.Telemetry;

public static IServiceCollection AddSnmpPipeline(this IServiceCollection services)
{
    // MediatR: scan current assembly, TaskWhenAllPublisher, behaviors in order
    services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssemblyContaining<SnmpOidReceived>();
        cfg.NotificationPublisher = new TaskWhenAllPublisher();  // PIPE-09
        cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));         // outermost
        cfg.AddOpenBehavior(typeof(ExceptionBehavior<,>));
        cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        cfg.AddOpenBehavior(typeof(OidResolutionBehavior<,>));   // innermost
    });

    // Pipeline metric service: Singleton owns all 6 counters
    services.AddSingleton<PipelineMetricService>();

    // Business metric factory: Singleton owns instrument cache
    services.AddSingleton<SnmpMetricFactory>();

    return services;
}
```

### Test Helper: FakeSnmpData for Unit Tests

SharpSnmpLib's `ISnmpData` implementations are not easily mockable — they require constructor arguments. Use the real concrete types from the library.

```csharp
// Source: src/Simetra/Services/SnmpExtractorService.cs — shows how ISnmpData types are used
// Use real SharpSnmpLib types in tests (Lextm.SharpSnmpLib package in test project)
using Lextm.SharpSnmpLib;

// Integer32
var intValue = new Integer32(42);
Assert.Equal(SnmpType.Integer32, intValue.TypeCode);
Assert.Equal(42, intValue.ToInt32());

// Counter32
var counter = new Counter32(1_000_000);
Assert.Equal(SnmpType.Counter32, counter.TypeCode);

// OctetString
var str = new OctetString("router-01");
Assert.Equal(SnmpType.OctetString, str.TypeCode);

// Gauge32
var gauge = new Gauge32(75);
Assert.Equal(SnmpType.Gauge32, gauge.TypeCode);
```

### Test: Verifying Behavior Execution Order

```csharp
// Use a List<string> captured by behaviors to assert execution order
// This pattern works without mocking framework — behaviors are thin classes
// with constructor-injected ILogger (use NullLogger) and PipelineMetricService

// Build the mediator with real behaviors against real DI container
var services = new ServiceCollection();
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<SnmpOidReceived>();
    cfg.NotificationPublisher = new TaskWhenAllPublisher();
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(ExceptionBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    cfg.AddOpenBehavior(typeof(OidResolutionBehavior<,>));
});
// ... register all dependencies
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MyBehavior<,>))` | `cfg.AddOpenBehavior(typeof(MyBehavior<,>))` inside `AddMediatR` | MediatR 12.0 (2023) | Cleaner single-configuration entry point |
| `INotificationPublisher` via custom class | `cfg.NotificationPublisher = new TaskWhenAllPublisher()` | MediatR 12.0 (2023) | Built-in parallel publisher; no custom class needed |
| `new Meter("name")` directly | `IMeterFactory.Create("name")` | .NET 8 / OTel SDK 1.x | DI-managed meter lifetime; compatible with `ITestOutputHelper` in tests |

**Deprecated/outdated:**
- `ForeachAwaitPublisher` (still default, but wrong for this use case): sequential + fail-fast, breaks handler error isolation.
- Manual `AddTransient(typeof(IPipelineBehavior<,>), ...)` registration: works but bypasses MediatR's configuration ordering guarantees.

---

## Open Questions

1. **Unknown device validation placement**
   - What we know: CONTEXT.md says "Unknown devices: reject with Warning log — only configured devices get metrics." ValidationBehavior is where this fires.
   - What's unclear: Should `ValidationBehavior` inject `IDeviceRegistry`? This means it becomes SNMP-specific (can't be a true open generic). Or should a separate `DeviceResolutionBehavior` be added between Validation and OidResolution?
   - Recommendation: Keep ValidationBehavior SNMP-specific (it already pattern-matches `is SnmpOidReceived`), inject `IDeviceRegistry`, and do the unknown-device check there. This avoids an extra behavior and matches the locked requirement list (only 4 behaviors in PIPE-08).

2. **`snmp.event.published` increment placement in Phase 3 tests**
   - What we know: The counter increments at the `Publish()` call site (Phase 5/6), not inside any behavior.
   - What's unclear: Phase 3 tests simulate Publish — should tests call `IncrementPublished()` before `await mediator.Publish(...)` to verify counter behavior, or just leave it for Phase 5/6?
   - Recommendation: Phase 3 tests verify the counter INCREMENT happens and the counter is zero before it's called. Tests for `snmp.event.published` can be deferred to Phase 5 integration tests. Phase 3 unit tests focus on the 3 counters behaviors touch: `handled`, `errors`, `rejected`.

3. **IDeviceRegistry injection into ValidationBehavior vs being open-generic**
   - What we know: Open generic behaviors work best when they're truly generic. Injecting `IDeviceRegistry` makes `ValidationBehavior` SNMP-specific.
   - What's unclear: Does this create issues with MediatR's `AddOpenBehavior` if the behavior has SNMP-specific constructor dependencies?
   - Recommendation: Not an issue — `AddOpenBehavior` registers the open generic, and DI injects concrete dependencies at resolve time regardless of whether the behavior is semantically "generic." This is already the pattern for `LoggingBehavior<,>` (which injects `ILogger<T>`).

---

## Sources

### Primary (HIGH confidence)
- `src/Simetra/Pipeline/MetricFactory.cs` — `ConcurrentDictionary<string, object>` instrument cache, `IMeterFactory`, `GetOrAdd` pattern (read directly)
- `src/Simetra/Telemetry/PipelineMetricService.cs` — singleton counter service, `IMeterFactory.Create()`, typed record methods (read directly)
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — existing DI registration patterns (`AddSingleton`, `AddHostedService`, extension method structure) (read directly)
- `src/SnmpCollector/Pipeline/OidMapService.cs` — `IOidMapService.Resolve()` returns `string` (metric name or "Unknown"), `OidMapService.Unknown` constant (read directly)
- `src/SnmpCollector/Pipeline/IDeviceRegistry.cs` — `TryGetDevice(IPAddress)`, `TryGetDeviceByName(string)` interfaces (read directly)
- `src/SnmpCollector/Telemetry/TelemetryConstants.cs` — `MeterName = "SnmpCollector"` (read directly)
- [SharpSnmpLib SnmpType Enum](https://help.sharpsnmp.com/html/T_Lextm_SharpSnmpLib_SnmpType.htm) — all enum values and integer codes verified (official SharpSnmpLib docs)
- [SharpSnmpLib DataFactory](https://github.com/lextudio/sharpsnmplib/blob/master/SharpSnmpLib/DataFactory.cs) — SnmpType → ISnmpData class mapping (official source)

### Secondary (MEDIUM confidence)
- [MediatR GitHub Issue #105 (MediatR.Extensions.Microsoft.DI)](https://github.com/jbogard/MediatR.Extensions.Microsoft.DependencyInjection/issues/105) — confirms first-registered = outermost with Microsoft DI (verified by reading the discussion, includes MediatR source code showing `.Reverse()` before aggregation)
- [Milan Jovanović: Publish MediatR Notifications In Parallel](https://www.milanjovanovic.tech/blog/how-to-publish-mediatr-notifications-in-parallel) — `cfg.NotificationPublisher = new TaskWhenAllPublisher()` syntax verified (cross-references MediatR 12 API)
- [MediatR v12.5.0 Release Notes](https://github.com/jbogard/MediatR/releases/tag/v12.5.0) — verified v12.5.0 release date and features (does not add new TaskWhenAllPublisher API — that was v12.0)

### Tertiary (LOW confidence)
- WebSearch results for behavior execution order — consensus matches HIGH confidence source above; no contradictions found.

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — packages verified in prior research (STACK.md), behavior API verified via MediatR GitHub
- Architecture: HIGH — MetricFactory and PipelineMetricService patterns read directly from Simetra reference implementation
- Pitfalls: HIGH — most are from prior research (PITFALLS.md) and codebase inspection; behavior order confirmed via GitHub issue

**Research date:** 2026-03-05
**Valid until:** 2026-06-05 (stable library versions; 90 days reasonable for MediatR 12.5.0 + SharpSnmpLib 12.5.7)
