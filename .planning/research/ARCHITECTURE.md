# Architecture Patterns: SnapshotJob Integration

**Domain:** SNMP monitoring — scheduled tenant evaluation with SNMP SET command execution
**Researched:** 2026-03-16
**Source:** Direct codebase analysis (all claims derived from actual source files in src/SnmpCollector)

---

## Existing Architecture Baseline

### Current Quartz Job Registration Pattern

Three jobs are registered in `AddSnmpScheduling` inside `ServiceCollectionExtensions.cs`:

- `CorrelationJob` — static key `"correlation"`, stamped interval, `[DisallowConcurrentExecution]`
- `HeartbeatJob` — static key `"heartbeat"`, configurable via `HeartbeatJobOptions`, `[DisallowConcurrentExecution]`
- `MetricPollJob` — dynamic key `"metric-poll-{addr}_{port}-{index}"`, one per device/poll-group pair

Thread pool ceiling: `Math.Max(initialJobCount, 50)`. Each job's interval is registered in `JobIntervalRegistry` for staleness threshold calculation in `LivenessHealthCheck`.

**SnapshotJob follows the HeartbeatJob pattern exactly**: static key, configurable interval via options POCO, `[DisallowConcurrentExecution]`, `intervalRegistry.Register("snapshot", ...)`, thread pool += 1.

### Current ISender.Send Dispatch Pattern

`MetricPollJob.DispatchResponseAsync` constructs `SnmpOidReceived` per varbind and calls `await _sender.Send(msg, ct)`. The full pipeline executes synchronously on the calling thread (MediatR `ISender.Send` is sequential, not fire-and-forget).

```
ISender.Send(SnmpOidReceived)
  0. LoggingBehavior          — outermost; increments snmp.event.published
  1. ExceptionBehavior        — wraps next() in try/catch
  2. ValidationBehavior       — OID regex + DeviceName null check; short-circuits on fail
  3. OidResolutionBehavior    — msg.MetricName = _oidMapService.Resolve(msg.Oid)
                                 BYPASS: if msg.Source == SnmpSource.Synthetic → skip lookup
  4. ValueExtractionBehavior  — sets msg.ExtractedValue / msg.ExtractedStringValue
  5. TenantVectorFanOutBehavior — routes by (ip, port, metricName); always calls next()
  6. OtelMetricHandler        — records snmp_gauge or snmp_info; stamps heartbeat liveness
```

### Current TenantVectorRegistry Access Pattern

`TenantVectorRegistry.Groups` returns a `volatile IReadOnlyList<PriorityGroup>`. Accessing `.Groups` is a single volatile read that returns the current snapshot. The returned list is immutable for its lifetime (a reload builds a new list and swaps the volatile field). SnapshotJob reads `.Groups` at job start and iterates the snapshot without holding a lock.

`MetricSlotHolder.ReadSlot()` performs a `Volatile.Read` of the internal `SeriesBox` and returns `series[^1]` or null. Thread-safe for concurrent reads from SnapshotJob (evaluation) and writes from `TenantVectorFanOutBehavior` (poll ingestion). No lock needed.

### Current SnmpSource Enum

```csharp
public enum SnmpSource { Poll, Trap, Synthetic }
```

`Synthetic` was added for aggregate metrics dispatched by `MetricPollJob.DispatchAggregatedMetricAsync`. `OidResolutionBehavior` has an explicit bypass: `if (msg.Source == SnmpSource.Synthetic) { return await next(); }`. `OtelMetricHandler` passes `source` as a lowercase string label on every instrument (`source="poll"`, `source="trap"`, `source="synthetic"`).

### Current ISnmpClient Contract

```csharp
public interface ISnmpClient
{
    Task<IList<Variable>> GetAsync(VersionCode version, IPEndPoint endpoint,
        OctetString community, IList<Variable> variables, CancellationToken ct);
}
```

`SharpSnmpClient` delegates to `Messenger.GetAsync`. No SET method exists today.

### Current CommandSlotOptions (loaded but not executed)

`TenantOptions.Commands` is `List<CommandSlotOptions>`. Bound from `tenants.json`. Each entry carries: `Ip`, `Port`, `CommandName` (resolves to OID via `ICommandMapService`), `Value` (string), `ValueType` (`"Integer32"` / `"IpAddress"` / `"OctetString"`). This data exists in memory but nothing calls it.

### Current MetricSlotHolder Properties Relevant to Evaluation

```
Ip, Port, MetricName  — routing key (set at construction)
IntervalSeconds       — from MetricSlotOptions.IntervalSeconds (device poll group value)
GraceMultiplier       — from MetricSlotOptions.GraceMultiplier (device poll group value)
Threshold             — ThresholdOptions? (Min double?, Max double?)
Source                — SnmpSource (last write source, not config role)
TypeCode              — SnmpType (last write type)
ReadSlot()            — MetricSlot? (Value, StringValue, Timestamp) or null
```

Missing today: the config-time **Role** (`"Evaluate"` / `"Resolved"` from `MetricSlotOptions.Role`) is validated at load time but is NOT stored on `MetricSlotHolder`. SnapshotJob needs to distinguish role at evaluation time.

---

## New Components Needed

### 1. `SnapshotJob` — `Jobs/SnapshotJob.cs`

**Responsibility:** Periodic `[DisallowConcurrentExecution]` Quartz `IJob` that drives the 4-tier tenant evaluation loop across all priority groups.

**Constructor dependencies:**
```csharp
ITenantVectorRegistry         // reads Groups + holder slots
ICommandWorker                // enqueues validated commands
ISuppressionCache             // checks/sets per-tenant suppression
ILivenessVectorService        // Stamp(jobKey) in finally
ICorrelationService           // operation correlation ID
PipelineMetricService         // new command counters
IOptions<SnapshotJobOptions>  // interval + suppression window config
ILogger<SnapshotJob>
```

**4-tier evaluation logic per tenant (executed for all tenants within a priority group, parallelisable within a group):**

```
Tier 1 — Staleness gate (Evaluate-role holders only):
  For each holder where Role == "Evaluate":
    if ReadSlot() is null: tenant is stale → skip this tenant
    if (UtcNow - slot.Timestamp) > (holder.IntervalSeconds * holder.GraceMultiplier):
      tenant is stale → skip this tenant
  If no Evaluate-role holders exist: tenant is not stale (no data to go stale)

Tier 2 — Resolved threshold check (gate: if any Resolved metric is healthy, no trigger):
  For each holder where Role == "Resolved":
    if ReadSlot() is null: skip tenant (missing data = cannot evaluate)
    if holder.Threshold is null: continue (no constraint)
    slot = ReadSlot()
    if slot.Value >= threshold.Min AND slot.Value <= threshold.Max: tenant healthy → skip tenant
  (If all Resolved slots are violated, or no Resolved slots exist, continue to Tier 3)

Tier 3 — Evaluate threshold check (trigger condition):
  For each holder where Role == "Evaluate":
    if ReadSlot() is null: skip tenant
    if holder.Threshold is null: skip (no threshold = cannot evaluate trigger)
    slot = ReadSlot()
    if slot.Value is in threshold range: tenant healthy → skip tenant
  (Only proceed to Tier 4 if ALL Evaluate-role thresholds are violated)

Tier 4 — Command queueing:
  For each CommandSlotOptions cmd in tenant.Commands:
    oid = _commandMap.ResolveCommandOid(cmd.CommandName)
    if oid is null: log Warning, skip
    if _suppressionCache.IsSuppressed(cmd.Ip, cmd.Port, cmd.CommandName):
      _pipelineMetrics.IncrementCommandSuppressed(deviceName)
    else:
      _commandWorker.Enqueue(new CommandExecution(cmd.Ip, cmd.Port,
          communityString, oid, cmd.Value, cmd.ValueType, deviceName, cmd.CommandName))
      _suppressionCache.Suppress(cmd.Ip, cmd.Port, cmd.CommandName,
          TimeSpan.FromSeconds(_options.SuppressionWindowSeconds))
      _pipelineMetrics.IncrementCommandQueued(deviceName)
```

**Priority group loop:**

```csharp
var groups = _registry.Groups; // single volatile read → snapshot
bool anyGroupFullyViolated = true;

foreach (var group in groups) // ascending priority value = highest priority first
{
    bool allTenantsViolated = true;

    await Parallel.ForEachAsync(group.Tenants, ct, async (tenant, token) =>
    {
        bool thisViolated = await EvaluateTenantAsync(tenant, token);
        if (!thisViolated) Interlocked.Exchange(ref allTenantsViolated, false); // see thread-safety below
    });

    if (!allTenantsViolated) break; // do not advance to lower-priority groups
}
```

Note: `bool` is not safely set via `Interlocked.Exchange`. Use a `volatile bool` field or `Interlocked.CompareExchange(ref intFlag, 0, 1)` pattern. Alternatively, collect bool results from parallel tasks and `All()` them — this avoids shared mutable state entirely.

**Job registration (in `AddSnmpScheduling`):**

```csharp
var snapshotOptions = new SnapshotJobOptions();
configuration.GetSection(SnapshotJobOptions.SectionName).Bind(snapshotOptions);

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
threadPoolSize += 1;
```

---

### 2. `SnapshotJobOptions` — `Configuration/SnapshotJobOptions.cs`

```csharp
public sealed class SnapshotJobOptions
{
    public const string SectionName = "SnapshotJob";

    [Range(1, 3600)]
    public int IntervalSeconds { get; set; } = 15;

    [Range(1, 86400)]
    public int SuppressionWindowSeconds { get; set; } = 300; // 5 minutes
}
```

Registered in `AddSnmpConfiguration` with `ValidateDataAnnotations().ValidateOnStart()`.

---

### 3. `ICommandWorker` / `CommandWorker` — `Pipeline/ICommandWorker.cs`, `Services/CommandWorker.cs`

**Responsibility:** Receives enqueued SNMP SET commands from SnapshotJob and executes them asynchronously. Dispatches the SET response through the full MediatR pipeline with `Source = SnmpSource.Command`.

**Interface:**

```csharp
public interface ICommandWorker
{
    void Enqueue(CommandExecution command);
}
```

**CommandExecution record (`Pipeline/CommandExecution.cs`):**

```csharp
public sealed record CommandExecution(
    string Ip,
    int Port,
    string CommunityString,
    string Oid,
    string Value,
    string ValueType,
    string DeviceName,
    string CommandName);
```

**`CommandWorker` implementation:**
- `IHostedService` backed by `Channel<CommandExecution>` (bounded, capacity from `ChannelsOptions`)
- `ExecuteAsync` loops `await foreach (var cmd in _channel.Reader.ReadAllAsync(stoppingToken))`
- For each command:
  1. Construct typed `ISnmpData` from `(cmd.Value, cmd.ValueType)` — see ValueType encoding below
  2. Call `await _snmpClient.SetAsync(VersionCode.V2, endpoint, community, [new Variable(oid, typed)], ct)`
  3. Success: dispatch each response varbind as `SnmpOidReceived { Source = SnmpSource.Command }` via `ISender.Send`; increment `snmp.command.sent`
  4. Failure: log Warning + increment `snmp.command.failed`; no re-enqueue

**ValueType encoding (new private method):**

```csharp
private static ISnmpData BuildSnmpData(string value, string valueType) => valueType switch
{
    "Integer32"   => new Integer32(int.Parse(value)),
    "IpAddress"   => new IP(IPAddress.Parse(value)),
    "OctetString" => new OctetString(value),
    _             => throw new InvalidOperationException($"Unknown ValueType: {valueType}")
};
```

**Registration in `AddSnmpPipeline`:**

```csharp
// CRITICAL: Register concrete type FIRST, then resolve same instance for both interfaces.
// See K8sLeaseElection registration pattern in AddSnmpConfiguration.
services.AddSingleton<CommandWorker>();
services.AddSingleton<ICommandWorker>(sp => sp.GetRequiredService<CommandWorker>());
services.AddHostedService(sp => sp.GetRequiredService<CommandWorker>());
```

---

### 4. `ISuppressionCache` / `SuppressionCache` — `Pipeline/`

**Responsibility:** Per-command suppression to prevent rapid repeated SET commands for the same target. Key is `(Ip, Port, CommandName)`. Suppressed entries expire after the configured window.

**Interface:**

```csharp
public interface ISuppressionCache
{
    bool IsSuppressed(string ip, int port, string commandName);
    void Suppress(string ip, int port, string commandName, TimeSpan window);
    void Clear(string ip, int port, string commandName);
}
```

**Implementation:** `ConcurrentDictionary<SuppressionKey, DateTimeOffset>` where the value is the expiry time. `IsSuppressed` checks `_dict.TryGetValue(key, out expiry) && expiry > DateTimeOffset.UtcNow`. No background cleanup thread — lazy expiry on read is correct given small key space (bounded by tenant × command count).

**Registration:** `AddSingleton<ISuppressionCache, SuppressionCache>()` in `AddSnmpPipeline`.

**SuppressionKey:**

```csharp
private readonly record struct SuppressionKey(string Ip, int Port, string CommandName);
```

---

## Modified Components

### `SnmpSource` — add `Command`

```csharp
public enum SnmpSource { Poll, Trap, Synthetic, Command }
```

`Command` identifies SET response varbinds dispatched by `CommandWorker`. It flows through the **full pipeline with no bypasses**. `OidResolutionBehavior` resolves SET response OIDs to metric names exactly like poll OIDs. `OtelMetricHandler` records `source="command"` on the instrument labels.

The `Synthetic` bypass in `OidResolutionBehavior` (`if (msg.Source == SnmpSource.Synthetic)`) is unchanged — `Command` is a distinct value and does not trigger this guard.

---

### `ISnmpClient` — add `SetAsync`

```csharp
public interface ISnmpClient
{
    Task<IList<Variable>> GetAsync(...);  // existing
    Task<IList<Variable>> SetAsync(      // new
        VersionCode version,
        IPEndPoint endpoint,
        OctetString community,
        IList<Variable> variables,
        CancellationToken ct);
}
```

`SharpSnmpClient` delegates to `Messenger.SetAsync` (SharpSnmpLib exposes this method with the same signature pattern as `GetAsync`, using a SET PDU type internally).

---

### `MetricSlotHolder` — add `Role` property

`MetricSlotOptions.Role` (`"Evaluate"` or `"Resolved"`) is validated at load time but is NOT stored on `MetricSlotHolder`. SnapshotJob reads this at evaluation time.

**Change:**
1. Add `public string Role { get; }` to `MetricSlotHolder`.
2. Add `string role` parameter to the `MetricSlotHolder` constructor (after `threshold`).
3. In `TenantVectorRegistry.Reload`, pass `metric.Role` when constructing each holder.

This is additive — no existing callers of `MetricSlotHolder` are broken. `CopyFrom` already copies `TypeCode` and `Source`; add `Role` copy too (though Role is immutable, CopyFrom should copy it for consistency).

Default value for `role` can be `string.Empty` for backward compatibility in tests that construct holders without a role.

---

### `PipelineMetricService` — add 4 command counters

Four new counters symmetric to the poll counters:

| Counter Name | When Fired | Who Fires It |
|---|---|---|
| `snmp.command.queued` | Command enqueued by SnapshotJob | `SnapshotJob` (Tier 4, non-suppressed path) |
| `snmp.command.suppressed` | Command skipped due to suppression window | `SnapshotJob` (Tier 4, suppressed path) |
| `snmp.command.sent` | SET request completed successfully | `CommandWorker` (after `SetAsync` success) |
| `snmp.command.failed` | SET request failed | `CommandWorker` (catch block) |

All four use the `device_name` tag, matching existing counter tag conventions.

---

### `ServiceCollectionExtensions` — registration additions

**`AddSnmpConfiguration`:**
```csharp
services.AddOptions<SnapshotJobOptions>()
    .Bind(configuration.GetSection(SnapshotJobOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**`AddSnmpPipeline`:**
```csharp
services.AddSingleton<ISuppressionCache, SuppressionCache>();
services.AddSingleton<CommandWorker>();
services.AddSingleton<ICommandWorker>(sp => sp.GetRequiredService<CommandWorker>());
services.AddHostedService(sp => sp.GetRequiredService<CommandWorker>());
```

**`AddSnmpScheduling`:**
- Bind `SnapshotJobOptions` at registration time (same pattern as `HeartbeatJobOptions`)
- Register `SnapshotJob` with Quartz
- `intervalRegistry.Register("snapshot", snapshotOptions.IntervalSeconds)`
- `threadPoolSize += 1`

---

## Data Flow Diagram

```
Quartz Scheduler (every 15s default)
         │
         ▼
  SnapshotJob.Execute()
         │
         ├─ _correlation.OperationCorrelationId = CurrentCorrelationId
         │
         ├─ groups = _registry.Groups  [single volatile read → immutable snapshot]
         │
         ├─ foreach PriorityGroup in groups (ascending priority value):
         │     │
         │     ├─ await Parallel.ForEachAsync(group.Tenants):
         │     │     │
         │     │     ├─ TIER 1: staleness check (Evaluate-role holders)
         │     │     │   ReadSlot() null OR age > IntervalSeconds × GraceMultiplier
         │     │     │   → stale: this tenant is NOT violated → mark group not-all-violated
         │     │     │
         │     │     ├─ TIER 2: Resolved-role threshold check
         │     │     │   ReadSlot() null → skip tenant (cannot evaluate)
         │     │     │   value in threshold range → tenant healthy → mark not-violated
         │     │     │
         │     │     ├─ TIER 3: Evaluate-role threshold check
         │     │     │   any Evaluate-role holder value in threshold range → not violated
         │     │     │
         │     │     └─ TIER 4: if tenant fully violated:
         │     │           foreach cmd in tenant.Commands:
         │     │             oid = _commandMap.ResolveCommandOid(cmd.CommandName)
         │     │             if suppressed → IncrementCommandSuppressed
         │     │             else → _commandWorker.Enqueue(CommandExecution{...})
         │     │                    _suppressionCache.Suppress(...)
         │     │                    IncrementCommandQueued
         │     │
         │     └─ if any tenant was NOT violated → break (stop iterating groups)
         │
         └─ finally:
               _liveness.Stamp("snapshot")
               _correlation.OperationCorrelationId = null
               _pipelineMetrics.IncrementSnapshotExecuted()  [optional]

                    │  [fire-and-forget into channel]
                    ▼
         CommandWorker (IHostedService, background loop)
                    │
                    ├─ Dequeue CommandExecution from Channel<CommandExecution>
                    │
                    ├─ BuildSnmpData(cmd.Value, cmd.ValueType) → ISnmpData typed value
                    │
                    ├─ ISnmpClient.SetAsync(endpoint, community, [Variable(oid, typed)], ct)
                    │     │
                    │     ├─ SUCCESS:
                    │     │   foreach varbind in SET response:
                    │     │     new SnmpOidReceived
                    │     │     {
                    │     │       Oid        = varbind.Id.ToString()
                    │     │       AgentIp    = IPAddress.Parse(cmd.Ip)
                    │     │       DeviceName = cmd.DeviceName
                    │     │       Value      = varbind.Data
                    │     │       Source     = SnmpSource.Command   ← new enum value
                    │     │       TypeCode   = varbind.Data.TypeCode
                    │     │     }
                    │     │     ISender.Send(msg, ct)
                    │     │           │
                    │     │           ▼  FULL MediatR pipeline:
                    │     │     LoggingBehavior
                    │     │     ExceptionBehavior
                    │     │     ValidationBehavior      (OID format + DeviceName)
                    │     │     OidResolutionBehavior   (resolves SET OID → MetricName; no bypass)
                    │     │     ValueExtractionBehavior (extracts numeric/string value)
                    │     │     TenantVectorFanOutBehavior (routes to matching slots)
                    │     │     OtelMetricHandler       (records snmp_gauge, source="command")
                    │     │
                    │     │   IncrementCommandSent(cmd.DeviceName)
                    │     │
                    │     └─ FAILURE:
                    │         LogWarning
                    │         IncrementCommandFailed(cmd.DeviceName)
                    │         (no re-enqueue)
```

---

## Integration Points with Existing Pipeline Behaviors

### ValidationBehavior

No changes. `SnmpOidReceived{Source=Command}` from `CommandWorker` provides:
- `Oid`: the SET OID string from `ICommandMapService.ResolveCommandOid` — a valid numeric OID
- `DeviceName`: set from `CommandExecution.DeviceName`

Both validation checks pass unchanged.

### OidResolutionBehavior

No changes. The `Source == Synthetic` bypass does NOT apply to `Command`. SET response OIDs flow through `IOidMapService.Resolve()` exactly like poll OIDs. If the OID is in the OID map (likely, since the same OID is used for polling), `MetricName` resolves to the known name and fan-out + recording proceed correctly. If the OID is absent, it resolves to `"Unknown"` — same behavior as an unmapped poll OID.

### TenantVectorFanOutBehavior

No changes. Routing key is `(ip, port, metricName)`. A SET response from the same device+OID combination will match the same routing key as the poll response. This is the intended behavior — the SET confirmation updates the same tenant slot that poll data updates.

### OtelMetricHandler

No changes. The `source` label will be `"command"` for SET response dispatches. Prometheus and Grafana can filter or group by `source` label to distinguish command-originated samples from poll-originated samples.

---

## Suggested Build Order

Dependencies flow upward. Each step must compile and be tested before the next begins.

| Step | Component | What Changes | Why This Order |
|------|-----------|-------------|---------------|
| 1 | `SnmpSource.Command` | Enum: add `Command` value | All new components reference this value; must exist first |
| 2 | `MetricSlotHolder.Role` | Add `string Role` property + constructor parameter | SnapshotJob reads it; TenantVectorRegistry sets it |
| 3 | `TenantVectorRegistry.Reload` | Pass `metric.Role` to `MetricSlotHolder` constructor | Depends on Step 2 |
| 4 | `SnapshotJobOptions` | New configuration POCO | SnapshotJob and AddSnmpScheduling depend on it |
| 5 | `ISuppressionCache` + `SuppressionCache` | New interface + implementation | No external deps; SnapshotJob depends on it |
| 6 | `ISnmpClient.SetAsync` + `SharpSnmpClient` | Interface extension + implementation | CommandWorker depends on it |
| 7 | `PipelineMetricService` new counters | 4 new Counter fields | SnapshotJob and CommandWorker both inject it |
| 8 | `CommandExecution` record | New data carrier | CommandWorker queue type; ICommandWorker interface uses it |
| 9 | `ICommandWorker` interface | New interface | SnapshotJob depends on the interface, not the implementation |
| 10 | `CommandWorker` hosted service | Full implementation | Requires Steps 6, 7, 8, 9 |
| 11 | `SnapshotJob` | Full implementation | Requires Steps 1–5, 7, 9 |
| 12 | `ServiceCollectionExtensions` updates | 3 registration additions | Wires all components into DI; final integration point |
| 13 | Unit tests | — | Cover suppression logic, tier evaluation, command dispatch, SET pipeline flow |

---

## Thread-Safety Analysis

| Component | Shared State | Access Pattern | Safety Mechanism |
|-----------|-------------|---------------|-----------------|
| `TenantVectorRegistry._groups` | `volatile IReadOnlyList<PriorityGroup>` | SnapshotJob reads; watcher writes (reload) | `volatile` field — readers see either old or new list, never partial |
| `MetricSlotHolder._box` | `volatile SeriesBox` | SnapshotJob reads via `ReadSlot()`; `TenantVectorFanOutBehavior` writes via `WriteValue()` | `Volatile.Read/Write` — acquire/release semantics |
| `SuppressionCache._dict` | `ConcurrentDictionary<key, expiry>` | SnapshotJob reads and writes | `ConcurrentDictionary` — inherently thread-safe |
| `CommandWorker._channel` | `Channel<CommandExecution>` | SnapshotJob writes (Enqueue); CommandWorker reads | `System.Threading.Channels` — designed for producer/consumer |
| `PipelineMetricService` counters | OTel `Counter<long>` | SnapshotJob + CommandWorker + behaviors all write | OTel SDK thread-safe by specification |
| `SnapshotJob` allViolated flag | Local to each parallel eval | `Parallel.ForEachAsync` tasks write | Use task-returning pattern (return bool per task, aggregate with `.All()`) to avoid shared mutable state |

**Key design observation:** SnapshotJob is a **pure reader** of `TenantVectorRegistry` and `MetricSlotHolder`. It never calls `WriteValue()`. Concurrent execution of `MetricPollJob` (writing via fan-out) and `SnapshotJob` (reading via `ReadSlot()`) is thread-safe by design.

**`[DisallowConcurrentExecution]` on SnapshotJob:** Prevents overlapping evaluations from double-queuing commands within the same suppression window check. If a prior SnapshotJob execution is still running when the trigger fires, Quartz skips the fire — exactly the same as `MetricPollJob` and `HeartbeatJob`.

**Suppression check-then-suppress is not atomic:** Two SnapshotJob executions could theoretically both see `IsSuppressed = false` and both enqueue a command before either sets the suppression entry. However, because `[DisallowConcurrentExecution]` guarantees only one SnapshotJob runs at a time, this race condition cannot occur in practice.

**CommandWorker channel backpressure:** The channel is bounded (capacity from `ChannelsOptions`). If `CommandWorker` is processing slowly and the channel is full, `ICommandWorker.Enqueue` should use `TryWrite` rather than a blocking `WriteAsync`. A failed `TryWrite` (channel full) should increment `snmp.command.failed` and log a Warning — same treatment as a SET network failure. Do not block SnapshotJob on channel capacity.

---

## Architecture Patterns to Follow

### Pattern: Singleton-then-HostedService (CommandWorker)

The existing `K8sLeaseElection` registration in `ServiceCollectionExtensions.cs` carries an explicit comment explaining why two `AddSingleton` calls are needed before `AddHostedService`. `CommandWorker` must follow this pattern precisely:

```csharp
services.AddSingleton<CommandWorker>();
services.AddSingleton<ICommandWorker>(sp => sp.GetRequiredService<CommandWorker>());
services.AddHostedService(sp => sp.GetRequiredService<CommandWorker>());
```

If `AddSingleton<ICommandWorker, CommandWorker>()` and `AddHostedService<CommandWorker>()` are called separately, DI creates two instances. The hosted service drains commands but `SnapshotJob` enqueues to the non-running instance's channel.

### Pattern: Channel-backed Background Worker (CommandWorker)

`CommandWorker` should follow `ChannelConsumerService`'s implementation:
- `Channel<T>` created in constructor with `BoundedChannelOptions`
- `ExecuteAsync` loops `await foreach (var cmd in _channel.Reader.ReadAllAsync(stoppingToken))`
- On cancellation: the `ReadAllAsync` loop exits cleanly on `OperationCanceledException`; no additional drain needed

### Pattern: Job Liveness Stamp (SnapshotJob)

All three existing jobs call `_liveness.Stamp(jobKey)` in `finally`. SnapshotJob must follow this exactly. The `"snapshot"` key must be registered in `intervalRegistry` so `LivenessHealthCheck.CheckHealthAsync` knows the expected staleness threshold.

### Pattern: Operation Correlation ID (SnapshotJob)

```csharp
_correlation.OperationCorrelationId = _correlation.CurrentCorrelationId;
// ... evaluation ...
finally
{
    _liveness.Stamp(jobKey);
    _correlation.OperationCorrelationId = null;
}
```

This is identical to `MetricPollJob.Execute`, `HeartbeatJob.Execute`, and `CorrelationJob.Execute`.

---

## Anti-Patterns to Avoid

### Anti-Pattern 1: Blocking SNMP SET Inside SnapshotJob

**What goes wrong:** Calling `ISnmpClient.SetAsync` directly in the evaluation loop (Tier 4) instead of via `CommandWorker` channel.
**Why bad:** Blocks the evaluation loop on network I/O. If a device is slow (100ms–1s per SET), and there are multiple commands to issue, a 15s SnapshotJob interval with `[DisallowConcurrentExecution]` will miss triggers entirely. The entire evaluation is delayed until all SETs complete.
**Instead:** Enqueue into `CommandWorker` channel. Evaluation is non-blocking (channel write is O(1)). `CommandWorker` processes SETs at its own pace independently.

### Anti-Pattern 2: Suppression State as SnapshotJob Instance Field

**What goes wrong:** Storing suppression timestamps as a `Dictionary` field on `SnapshotJob` itself.
**Why bad:** Quartz uses DI to resolve job instances. Even with `[DisallowConcurrentExecution]`, each execution goes through the DI lifecycle. If `SnapshotJob` is registered as a transient or scoped service, a new instance is created each execution and the suppression state is lost. Even if registered as singleton, the pattern is unclear and the suppression service has no testable interface.
**Instead:** `ISuppressionCache` registered as a DI singleton, injected into `SnapshotJob`. Clear lifecycle, easily mockable in tests.

### Anti-Pattern 3: Bypassing OID Resolution for Source == Command

**What goes wrong:** Adding `if (msg.Source == SnmpSource.Command) { return await next(); }` in `OidResolutionBehavior`, mirroring the `Synthetic` bypass.
**Why bad:** SET response OIDs are real device OIDs that appear in the OID map. Bypassing resolution means `MetricName` is null, `TenantVectorFanOutBehavior` skips fan-out (the fan-out behavior checks `metricName is not null && metricName != Unknown`), and `OtelMetricHandler` records `"Unknown"` for a named metric. The `Synthetic` bypass is for the `"0.0"` sentinel OID which cannot resolve — Command uses real OIDs.
**Instead:** Let `Source = Command` flow through OID resolution identically to `Source = Poll`.

### Anti-Pattern 4: CommunityString Lookup During SnapshotJob Evaluation

**What goes wrong:** Looking up the CommunityString from `IDeviceRegistry` inside `SnapshotJob` during the evaluation loop (Tier 4).
**Why bad:** `CommandSlotOptions` has `Ip` and `Port` but no `CommunityString`. Looking it up in Tier 4 adds a registry lookup inside a hot evaluation loop that's running parallel tenants. More importantly, mixing evaluation concerns with execution concerns in one method makes the evaluation harder to test.
**Instead:** Pass the Ip and Port in `CommandExecution`. `CommandWorker` resolves the `CommunityString` from `IDeviceRegistry.TryGetByIpPort` just before calling `ISnmpClient.SetAsync`. If the device is not found at execution time (was removed from registry after enqueue), log a Warning and drop the command.

### Anti-Pattern 5: Enum Ordinal Comparisons on SnmpSource

**What goes wrong:** Using `msg.Source > SnmpSource.Synthetic` or hardcoded integer comparisons to detect the new `Command` value.
**Why bad:** Breaks if enum values are reordered. All existing source checks use `== SnmpSource.Synthetic` or `!= X` pattern.
**Instead:** Always use named enum value comparisons.

---

## Open Questions for Phase Design

1. **"All violated" semantics for groups:** Does "advance to next group only if all tenants in the group are violated" mean: (a) all tenants whose evaluation completed without staleness, OR (b) all tenants including stale ones? Stale tenants indicate missing data, which could mean the device is down — treat as "not violated" (i.e., stale = healthy assumption) to avoid sending commands to unreachable devices.

2. **Tier 2 "all Resolved violated" vs "any Resolved violated":** PROJECT.md states "all violated → end, no command". The logical inverse is: if ANY Resolved metric is healthy (in range), stop and do not trigger commands. This matches a safety gate pattern — the monitored system state is OK on at least one metric, so do not intervene.

3. **CommunityString in CommandExecution:** `CommandSlotOptions` has `Ip` + `Port` but no CommunityString. Two approaches: (A) resolve in SnapshotJob from DeviceRegistry before enqueue — simpler CommandWorker but more coupling in SnapshotJob. (B) resolve in CommandWorker at execution time — evaluation is cleaner, but CommandWorker must handle "device not found" gracefully. Approach B is recommended (see Anti-Pattern 4 above).

4. **SnapshotJob and leader gating:** PROJECT.md says all instances poll; leader controls export only. Recommendation: SnapshotJob runs on all replicas (no leader gate). Multiple replicas may issue the same SET command to a device, but for idempotent SET operations (e.g., set a register to a fixed value) this is harmless. The `SuppressionCache` is per-pod (not distributed), so each replica maintains its own suppression window. This is acceptable for the current use cases.

5. **CommandWorker TryWrite vs WriteAsync:** If the channel is full at enqueue time, `TryWrite` returns false immediately (non-blocking). `WriteAsync` blocks until space is available or cancellation fires. For SnapshotJob's evaluation loop, `TryWrite` is preferred — a full channel indicates CommandWorker is overwhelmed, and logging + incrementing `snmp.command.failed` is the correct response. Blocking SnapshotJob's Quartz thread on channel backpressure would cascade into liveness probe failures.

---

## Sources

All findings derived from direct reading of source files (no training-data speculation):
- `src/SnmpCollector/Jobs/MetricPollJob.cs` — ISender.Send pattern, SnmpSource.Poll usage, liveness stamp
- `src/SnmpCollector/Jobs/HeartbeatJob.cs` — Messenger.SendTrapV2, liveness stamp, correlation pattern
- `src/SnmpCollector/Jobs/CorrelationJob.cs` — correlation ID pattern, liveness stamp
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — 6-phase DI, Quartz registration, thread pool, singleton-then-hosted pattern, operator config ordering guidance (CS-07)
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — volatile Groups field, priority ordering, slot construction
- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — ReadSlot(), WriteValue(), Volatile.Read/Write, Role gap
- `src/SnmpCollector/Pipeline/ISnmpClient.cs` + `SharpSnmpClient.cs` — GetAsync-only today
- `src/SnmpCollector/Pipeline/SnmpSource.cs` — Poll/Trap/Synthetic; Command is absent today
- `src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs` — OID regex, DeviceName check
- `src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs` — Synthetic bypass at Source check
- `src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` — routing by (ip, port, metricName)
- `src/SnmpCollector/Pipeline/Behaviors/ValueExtractionBehavior.cs` — TypeCode switch, ExtractedValue set
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` — source label, heartbeat stamp
- `src/SnmpCollector/Pipeline/CommandMapService.cs` — ResolveCommandOid bidirectional map
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — 12 counter pattern, IMeterFactory, device_name tag
- `src/SnmpCollector/Configuration/TenantOptions.cs` — Commands list exists but unused at runtime
- `src/SnmpCollector/Configuration/CommandSlotOptions.cs` — Ip/Port/CommandName/Value/ValueType
- `src/SnmpCollector/Configuration/MetricSlotOptions.cs` — Role field, GraceMultiplier, Threshold
- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — Role absent; Threshold present
- `.planning/PROJECT.md` — v2.0 requirements, 4-tier evaluation semantics, priority group rules
