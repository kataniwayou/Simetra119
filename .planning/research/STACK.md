# Technology Stack — SnapshotJob & SNMP SET Command Execution Milestone

**Project:** Simetra119 SNMP Collector
**Researched:** 2026-03-16
**Milestone scope:** Tenant evaluation (threshold-based) + SNMP SET control loop via SnapshotJob
**Out of scope:** Re-researching existing MediatR pipeline, Quartz scheduling, OTel, leader election

---

## Executive Decision

**Zero new NuGet packages.** Every API needed for SNMP SET, command worker queuing, suppression
caching, and priority-based SnapshotJob execution is present in the existing stack:
`Lextm.SharpSnmpLib 12.5.7`, `System.Threading.Channels` (BCL), `System.Collections.Concurrent` (BCL),
and `Quartz.Extensions.Hosting 3.15.1`.

---

## Existing Stack (Unchanged)

| Technology | Version | Role |
|------------|---------|------|
| .NET / C# | 9.0 | Runtime |
| Lextm.SharpSnmpLib | 12.5.7 | SNMP GET + **SET** via `Messenger` |
| MediatR | 12.5.0 | Pipeline dispatch (unchanged) |
| Quartz.Extensions.Hosting | 3.15.1 | `SnapshotJob` scheduling |
| System.Threading.Channels | BCL (.NET 9) | Already used by trap pipeline; reused for command worker queue |
| System.Collections.Concurrent | BCL (.NET 9) | `ConcurrentDictionary<K,V>` for suppression cache |
| OpenTelemetry SDK | 1.15.0 | Counter increments for SET outcomes |

---

## Question 1: How to call `Messenger.SetAsync` — exact API

**Verified directly from `SharpSnmpLib.dll` 12.5.7 via reflection. Not guessed.**

### Overloads

```csharp
// Without cancellation token
Task<IList<Variable>> Messenger.SetAsync(
    VersionCode version,
    IPEndPoint endpoint,
    OctetString community,
    IList<Variable> variables)

// With cancellation token (USE THIS ONE)
Task<IList<Variable>> Messenger.SetAsync(
    VersionCode version,
    IPEndPoint endpoint,
    OctetString community,
    IList<Variable> variables,
    CancellationToken token)
```

The return type is `Task<IList<Variable>>` — the response varbinds echoed back by the agent.
For a SET, the returned list contains the same OID/value pairs from the request if the agent
accepted the operation. This return value is informational; most callers discard it or log it.

### Variable construction for SET

`Variable` has two constructors. For SET use:

```csharp
new Variable(ObjectIdentifier id, ISnmpData data)
```

The `id` is built from the OID string resolved via `ICommandMapService.ResolveCommandOid`.
The `data` is the value to write — type must match the target OID's SNMP MIB type:

| `CommandSlotOptions.ValueType` | `ISnmpData` constructor |
|-------------------------------|------------------------|
| `"Integer32"` | `new Integer32(int value)` |
| `"OctetString"` | `new OctetString(string content)` |
| `"IpAddress"` | `new IP(string ip)` — **NOTE: type is `Lextm.SharpSnmpLib.IP`, not `IpAddress`** |

```csharp
// Building the variable list from a CommandSlotOptions entry:
ISnmpData snmpValue = slot.ValueType switch
{
    "Integer32"  => new Integer32(int.Parse(slot.Value)),
    "OctetString" => new OctetString(slot.Value),
    "IpAddress"  => new IP(slot.Value),
    _            => throw new InvalidOperationException($"Unknown ValueType: {slot.ValueType}")
};

var oid = new ObjectIdentifier(_commandMapService.ResolveCommandOid(slot.CommandName)
    ?? throw new InvalidOperationException($"Command {slot.CommandName} not in map"));

var variables = new List<Variable> { new Variable(oid, snmpValue) };

var response = await Messenger.SetAsync(
    VersionCode.V2,
    new IPEndPoint(IPAddress.Parse(slot.Ip), slot.Port),
    new OctetString(communityString),
    variables,
    cancellationToken);
```

### Exception handling for SET

SharpSnmpLib throws these on SET failure (verified via reflection):

| Exception | Meaning |
|-----------|---------|
| `Lextm.SharpSnmpLib.Messaging.TimeoutException` | Agent did not respond within timeout. Has `Timeout` (int ms) and `Agent` (IPAddress) properties. Extends `OperationException`. |
| `Lextm.SharpSnmpLib.Messaging.ErrorException` | Agent responded with SNMP error PDU (e.g. `noAccess`, `notWritable`). Has `Body` (ISnmpMessage) and `Agent` (IPAddress) properties. |
| `Lextm.SharpSnmpLib.SnmpException` | Encoding or protocol error. Base class for library exceptions. |
| `OperationCanceledException` | `CancellationToken` was cancelled (host shutdown or linked timeout CTS). |

**Important naming collision:** `Lextm.SharpSnmpLib.Messaging.TimeoutException` and
`System.TimeoutException` share the name. Use a `using` alias or fully-qualified name to avoid
CS0104 ambiguity:

```csharp
using SnmpTimeout = Lextm.SharpSnmpLib.Messaging.TimeoutException;
```

### Extending `ISnmpClient` for SET

Add `SetAsync` to the interface following the same pattern as `GetAsync`:

```csharp
public interface ISnmpClient
{
    Task<IList<Variable>> GetAsync(
        VersionCode version, IPEndPoint endpoint, OctetString community,
        IList<Variable> variables, CancellationToken ct);

    Task<IList<Variable>> SetAsync(
        VersionCode version, IPEndPoint endpoint, OctetString community,
        IList<Variable> variables, CancellationToken ct);
}
```

`SharpSnmpClient` implementation:

```csharp
public Task<IList<Variable>> SetAsync(
    VersionCode version, IPEndPoint endpoint, OctetString community,
    IList<Variable> variables, CancellationToken ct)
    => Messenger.SetAsync(version, endpoint, community, variables, ct);
```

This is the only change to the SNMP client abstraction.

---

## Question 2: Queue pattern for command worker

**Recommendation: `Channel<CommandRequest>` (bounded, DropOldest), single consumer service.**

### Why `Channel<T>` over alternatives

The project already uses `Channel<VarbindEnvelope>` in `TrapChannel` for exactly this pattern:
bounded buffer, single reader, multiple potential writers, drop-on-full backpressure. Reusing
the same primitive keeps the codebase consistent and avoids new concepts.

| Pattern | Thread-safe writes | Backpressure | Cancellation | Used in project | Verdict |
|---------|-------------------|--------------|--------------|----------------|---------|
| `Channel<T>` (bounded) | Yes | DropOldest | Native `CancellationToken` | YES (TrapChannel) | **USE THIS** |
| `BlockingCollection<T>` | Yes | Bounded block | Manual token threading | No | Avoid — blocking semantics, no async drain |
| `ConcurrentQueue<T>` | Yes | None | Manual | No | Avoid — no built-in backpressure |
| Custom lock-based queue | Yes | Manual | Manual | No | Avoid — unnecessary complexity |

### Configuration

```csharp
var options = new BoundedChannelOptions(capacity: 256)
{
    FullMode = BoundedChannelFullMode.DropOldest,
    SingleWriter = false,   // SnapshotJob (multiple Quartz threads) writes
    SingleReader = true,    // CommandWorkerService single consumer
    AllowSynchronousContinuations = false,
};
_channel = Channel.CreateBounded<CommandRequest>(options, itemDropped: cmd =>
{
    // Increment snmp.command.dropped counter
    _metrics.IncrementCommandDropped(cmd.TenantId);
});
```

### `CommandRequest` record

```csharp
public sealed record CommandRequest(
    string TenantId,
    string CommandName,
    string Ip,
    int Port,
    string CommunityString,
    string Value,
    string ValueType,
    DateTimeOffset EnqueuedAt);
```

Immutable record — safe to pass across channel without copying.

### Consumer service

`CommandWorkerService : BackgroundService` (or `IHostedService`) drains the channel
using `await foreach` over `_channel.Reader.ReadAllAsync(stoppingToken)`, calling
`ISnmpClient.SetAsync` per item. This mirrors `ChannelConsumerService` exactly.

---

## Question 3: Suppression cache — thread-safe time-based deduplication

**Recommendation: `ConcurrentDictionary<string, DateTimeOffset>` with TTL check.**

No `IMemoryCache`, no `Microsoft.Extensions.Caching.Memory` — those are already in the
`Microsoft.AspNetCore.App` framework reference but add unnecessary abstraction for a
single-purpose TTL map. `ConcurrentDictionary` is simpler, has no eviction background thread,
and the access pattern here is write-heavy at trigger time and read-heavy during evaluation.

### Pattern

```csharp
private readonly ConcurrentDictionary<string, DateTimeOffset> _suppressedUntil = new();

/// <summary>
/// Returns true if the command is currently suppressed (a SET was fired recently).
/// </summary>
public bool IsSuppressed(string tenantId, string commandName)
{
    var key = $"{tenantId}:{commandName}";
    return _suppressedUntil.TryGetValue(key, out var until)
        && DateTimeOffset.UtcNow < until;
}

/// <summary>
/// Marks the command as suppressed for the given duration.
/// </summary>
public void Suppress(string tenantId, string commandName, TimeSpan duration)
{
    var key = $"{tenantId}:{commandName}";
    _suppressedUntil[key] = DateTimeOffset.UtcNow + duration;
}
```

### Thread-safety analysis

- `ConcurrentDictionary` indexer write (`[key] = value`) is atomic (lock-free on the bucket).
- `TryGetValue` is lock-free read — safe to call from multiple Quartz threads simultaneously.
- No compound read-modify-write is needed: each evaluation either reads (check) or writes
  (set after fire). There is no "check then conditionally update" race that requires a
  transaction — if two Quartz threads simultaneously evaluate the same command slot, both
  may fire. To prevent double-firing use `AddOrUpdate` with a condition:

```csharp
public bool TrySuppress(string tenantId, string commandName, TimeSpan duration)
{
    var key = $"{tenantId}:{commandName}";
    var until = DateTimeOffset.UtcNow + duration;

    // Attempt to add. If key already exists with a future expiry, do not overwrite.
    var added = _suppressedUntil.TryAdd(key, until);
    if (added) return true;

    // Key exists — check if it is already active
    if (_suppressedUntil.TryGetValue(key, out var existing) && DateTimeOffset.UtcNow < existing)
        return false; // still suppressed, do not fire

    // Expired — overwrite
    _suppressedUntil[key] = until;
    return true;
}
```

This `TrySuppress` pattern returns `true` if the caller should fire the SET (either fresh
suppression or expired prior suppression). Returns `false` if still within TTL — skip the SET.

**Eviction:** Entries are never physically removed, only expire logically. For a fleet of
N tenants × M commands, the dictionary never exceeds N×M entries. This is bounded by config
size (validated at load time), not by runtime data volume. No background eviction needed.

### Suppression duration source

Suppression TTL must come from configuration. The natural location is `CommandSlotOptions`
or a per-tenant `SnapshotOptions`. Recommend a top-level `SnapshotJobOptions` with a default:

```csharp
public sealed class SnapshotJobOptions
{
    public const string SectionName = "SnapshotJob";

    /// <summary>
    /// How long to suppress repeat SETs after one fires. Default 60s.
    /// </summary>
    [Range(1, 3600)]
    public int SuppressionSeconds { get; set; } = 60;

    /// <summary>
    /// Interval for the SnapshotJob trigger in seconds. Default 30s.
    /// </summary>
    [Range(5, 3600)]
    public int IntervalSeconds { get; set; } = 30;
}
```

---

## Question 4: SnapshotJob Quartz configuration

**Recommendation: `[DisallowConcurrentExecution]`, single job key, simple interval trigger.**

### Why a new job instead of extending `MetricPollJob`

`MetricPollJob` is bound 1:1 to a device/poll-group pair and is focused on SNMP GET dispatch.
`SnapshotJob` reads tenant vectors (all tenants, all slots) and evaluates thresholds across
all of them in one pass. These are orthogonal concerns and different scheduling requirements.

### Job declaration

```csharp
[DisallowConcurrentExecution]
public sealed class SnapshotJob : IJob
{
    // Inject: ITenantVectorRegistry, ISnmpClient, ICommandMapService,
    //         ISuppressionCache, PipelineMetricService, ICorrelationService,
    //         ILivenessVectorService, ILogger<SnapshotJob>
}
```

`[DisallowConcurrentExecution]` is required. SnapshotJob walks the full tenant registry and
may fire multiple SET commands. If a previous execution is still running (e.g. blocked on a
slow SNMP agent), a new execution would double-evaluate and double-fire suppressed commands.
The `[DisallowConcurrentExecution]` guarantee from Quartz prevents pile-up on the same job key.

### Quartz registration (inside `AddSnmpScheduling`)

```csharp
// SnapshotJob: evaluates tenant vectors and fires SNMP SET commands.
// Registered inside AddQuartz(...) alongside CorrelationJob and MetricPollJob.
var snapshotKey = new JobKey("snapshot");
q.AddJob<SnapshotJob>(j => j.WithIdentity(snapshotKey));
q.AddTrigger(t => t
    .ForJob(snapshotKey)
    .WithIdentity("snapshot-trigger")
    .StartNow()
    .WithSimpleSchedule(s => s
        .WithIntervalInSeconds(snapshotOptions.IntervalSeconds)
        .RepeatForever()
        .WithMisfireHandlingInstructionNextWithRemainingCount()));

intervalRegistry.Register("snapshot", snapshotOptions.IntervalSeconds);
```

`WithMisfireHandlingInstructionNextWithRemainingCount` is the established pattern in this
codebase for all jobs — it skips stale fires rather than catching up.

### Priority-based evaluation pass

The tenant registry exposes `ITenantVectorRegistry.Groups` as `IReadOnlyList<PriorityGroup>`,
already sorted ascending by priority integer (lowest value = highest priority = evaluated first).
SnapshotJob iterates `Groups` in order:

```csharp
foreach (var group in _registry.Groups)         // priority 1 before priority 2
    foreach (var tenant in group.Tenants)
        foreach (var holder in tenant.Holders)
            EvaluateSlot(tenant, holder, ct);
```

No additional sorting or external priority service is needed — the registry already provides
the ordered structure from when it was loaded by `TenantVectorRegistry.Reload`.

### Thread pool sizing

Add 1 to `initialJobCount` in `AddSnmpScheduling` for the SnapshotJob:

```csharp
var initialJobCount = 3; // CorrelationJob + HeartbeatJob + SnapshotJob
foreach (var device in devicesOptions.Devices)
    initialJobCount += device.Polls.Count;
var threadPoolSize = Math.Max(initialJobCount, 50);
```

The existing `Math.Max(..., 50)` floor means this change only matters for very small fleets.

---

## What NOT to Add

| Omission | Rationale |
|----------|-----------|
| New NuGet packages | Every required API exists in SharpSnmpLib 12.5.7 + .NET 9 BCL |
| `IMemoryCache` for suppression | `ConcurrentDictionary<string, DateTimeOffset>` is simpler, no background thread, bounded by config size |
| `BlockingCollection<T>` for command queue | `Channel<T>` already used in codebase; blocking semantics are inferior for async drain |
| `[AllowConcurrentExecution]` on `SnapshotJob` | Would require locking the suppression cache write path; `[DisallowConcurrentExecution]` makes locking unnecessary |
| Separate MediatR message for SET dispatch | SNMP SET is a side effect, not an observable metric; routing through MediatR pipeline adds unnecessary overhead and behavior overhead |
| `IpAddress` as the SharpSnmpLib IP type name | The actual type is `Lextm.SharpSnmpLib.IP`; using `IpAddress` will produce CS0246 |
| `System.TimeoutException` catch for SNMP timeout | SharpSnmpLib throws `Lextm.SharpSnmpLib.Messaging.TimeoutException` (extends `OperationException`), not BCL `System.TimeoutException` |

---

## Required Changes Summary

### 1. `ISnmpClient.cs` — Add `SetAsync`

Add one method to the existing interface. `SharpSnmpClient.cs` delegates directly to
`Messenger.SetAsync`. No other files change for this addition.

### 2. New `ISuppressionCache.cs` + `SuppressionCache.cs`

Singleton. Internal state: `ConcurrentDictionary<string, DateTimeOffset>`. No DI dependencies.
Registered as `AddSingleton<ISuppressionCache, SuppressionCache>()` in `AddSnmpPipeline` or
`AddSnmpScheduling`.

### 3. New `ICommandChannel.cs` + `CommandChannel.cs`

Mirrors `ITrapChannel` / `TrapChannel` exactly. `BoundedChannelOptions` with `DropOldest`.
Registered as `AddSingleton<ICommandChannel, CommandChannel>()`.

### 4. New `CommandWorkerService.cs`

`BackgroundService`. Drains `ICommandChannel.Reader` via `ReadAllAsync`. Calls
`ISnmpClient.SetAsync` per `CommandRequest`. Increments OTel counters on success/failure.

### 5. New `SnapshotJob.cs`

`[DisallowConcurrentExecution] IJob`. Iterates `ITenantVectorRegistry.Groups`, evaluates
`MetricSlotHolder.Threshold` against `ReadSlot().Value`, checks `ISuppressionCache.TrySuppress`,
enqueues `CommandRequest` to `ICommandChannel.Writer`. Does NOT call `SetAsync` directly —
that is the worker's concern.

### 6. New `SnapshotJobOptions.cs`

Two fields: `IntervalSeconds` (default 30), `SuppressionSeconds` (default 60). Bound and
validated in `AddSnmpScheduling` following the existing `CorrelationJobOptions` pattern.

### 7. `ServiceCollectionExtensions.cs` — Registration additions

- `AddSingleton<ISuppressionCache, SuppressionCache>()` in `AddSnmpPipeline`
- `AddSingleton<ICommandChannel, CommandChannel>()` in `AddSnmpPipeline`
- `AddHostedService<CommandWorkerService>()` in `AddSnmpPipeline`
- SnapshotJob + trigger in `AddSnmpScheduling` inside `AddQuartz(...)`
- Bump `initialJobCount` by 1

---

## Confidence Assessment

| Area | Confidence | Source |
|------|------------|--------|
| `Messenger.SetAsync` exact signatures | HIGH | Reflected from `SharpSnmpLib.dll` 12.5.7 at runtime via `dotnet run` |
| `Lextm.SharpSnmpLib.IP` type name (not `IpAddress`) | HIGH | Reflected from DLL; `IP.TypeCode` returns `SnmpType.IPAddress` |
| `Variable(ObjectIdentifier, ISnmpData)` constructor | HIGH | Reflected from DLL |
| `ErrorException`, `TimeoutException` hierarchy | HIGH | Reflected from DLL; both extend `OperationException` |
| `Channel<T>` as command queue | HIGH | Read `TrapChannel.cs` directly; same pattern |
| `ConcurrentDictionary` suppression cache thread safety | HIGH | BCL documentation; `TryAdd`/indexer are atomic per bucket |
| `[DisallowConcurrentExecution]` guarantee from Quartz | HIGH | Read in `MetricPollJob.cs`, `CorrelationJob.cs`; Quartz 3.x contract |
| `ITenantVectorRegistry.Groups` already priority-sorted | HIGH | Read `TenantVectorRegistry.Reload` — uses `SortedDictionary<int, List<Tenant>>` ascending |
| No new NuGet packages needed | HIGH | All required types identified in existing source + BCL |

---

## Sources

All authoritative sources read directly:

- `src/SnmpCollector/Pipeline/ISnmpClient.cs` — existing interface, extension point
- `src/SnmpCollector/Pipeline/SharpSnmpClient.cs` — `Messenger.GetAsync` delegation pattern
- `src/SnmpCollector/Pipeline/TrapChannel.cs` — `Channel<T>` bounded channel pattern
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — `SortedDictionary` priority ordering
- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — `ThresholdOptions`, `ReadSlot()`
- `src/SnmpCollector/Configuration/CommandSlotOptions.cs` — `Ip`, `Port`, `CommandName`, `Value`, `ValueType`
- `src/SnmpCollector/Configuration/ThresholdOptions.cs` — `Min`/`Max` fields
- `src/SnmpCollector/Jobs/MetricPollJob.cs` — `[DisallowConcurrentExecution]`, Quartz `Execute` pattern
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — full registration flow
- `SharpSnmpLib.dll` 12.5.7 via reflection (`dotnet run` against NuGet cache):
  - `Messenger.SetAsync` overloads and return types
  - `Variable` constructors
  - `IP`, `Integer32`, `OctetString`, `Gauge32` constructors
  - `ErrorException`, `TimeoutException`, `SnmpException` hierarchy

---

*Stack research for: SnapshotJob & SNMP SET Command Execution milestone*
*Researched: 2026-03-16*
