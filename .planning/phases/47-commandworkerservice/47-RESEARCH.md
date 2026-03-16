# Phase 47: CommandWorkerService - Research

**Researched:** 2026-03-16
**Domain:** SNMP SET command channel worker, MediatR pipeline dispatch
**Confidence:** HIGH

---

## Summary

Phase 47 introduces the channel-backed worker that bridges queued SNMP SET commands to
the MediatR pipeline. All patterns needed to implement this phase already exist in the
codebase: the `ITrapChannel`/`TrapChannel` pair defines the channel interface and
implementation to mirror; `ChannelConsumerService` defines the drain loop and error
handling; `MetricPollJob` defines the `SnmpOidReceived` construction and timeout pattern;
and `K8sLeaseElection` defines the Singleton-then-HostedService DI registration.

The work is straightforward extraction and adaptation: define `CommandRequest` record,
define `ICommandChannel` (no `Complete`/`WaitForDrainAsync` needed — no drain on
shutdown), implement `CommandChannel` with `BoundedChannelFullMode.Wait` avoided in favor
of explicit `TryWrite` failure path, and implement `CommandWorkerService` that mirrors
`ChannelConsumerService` almost line-for-line.

**Primary recommendation:** Implement `CommandChannel` as a `BoundedChannel` with
capacity 16 and `BoundedChannelFullMode.DropWrite`. `CommandWorkerService.ExecuteAsync`
uses `await foreach (var req in _commandChannel.Reader.ReadAllAsync(stoppingToken))` with
per-item try/catch and no drain on cancellation. The `await foreach` loop exits naturally
when `stoppingToken` is cancelled.

---

## Standard Stack

### Core (all already in project, no new dependencies)

| Type | Location | Purpose |
|------|----------|---------|
| `System.Threading.Channels` | BCL | Bounded channel backing `CommandChannel` |
| `MediatR.ISender` | NuGet (MediatR) | Dispatch `SnmpOidReceived` through pipeline |
| `ISnmpClient.SetAsync` | `SnmpCollector.Pipeline` | Execute SNMP SET — already defined |
| `IDeviceRegistry.TryGetByIpPort` | `SnmpCollector.Pipeline` | Community string lookup at execution time |
| `ICommandMapService.ResolveCommandName` | `SnmpCollector.Pipeline` | Reverse OID-to-name for `MetricName` pre-set |
| `PipelineMetricService` | `SnmpCollector.Telemetry` | `IncrementCommandSent` / `IncrementCommandFailed` |
| `SharpSnmpClient.ParseSnmpData` | `SnmpCollector.Pipeline` | Convert `Value`+`ValueType` to `ISnmpData` |

### No New Packages Required

All dependencies already registered in the DI container.

---

## Architecture Patterns

### Recommended Project Structure

New files:

```
src/SnmpCollector/
├── Pipeline/
│   ├── ICommandChannel.cs       # interface with Writer/Reader properties only (no Complete/WaitForDrainAsync)
│   ├── CommandChannel.cs        # BoundedChannel implementation
│   └── CommandRequest.cs        # record carrying CommandSlotOptions fields + DeviceName
└── Services/
    └── CommandWorkerService.cs  # BackgroundService drain loop
```

Registration additions in `ServiceCollectionExtensions.AddSnmpPipeline`:

```csharp
// Mirrors ITrapChannel/ChannelConsumerService registration block
services.AddSingleton<ICommandChannel, CommandChannel>();

// Singleton-then-HostedService pattern (same as K8sLeaseElection)
services.AddSingleton<CommandWorkerService>();
services.AddSingleton<ICommandWorkerService>(sp => sp.GetRequiredService<CommandWorkerService>());
services.AddHostedService(sp => sp.GetRequiredService<CommandWorkerService>());
```

Note: If no external consumers need `ICommandWorkerService`, a simpler registration
is acceptable — `AddSingleton<CommandWorkerService>()` + `AddHostedService(sp =>
sp.GetRequiredService<CommandWorkerService>())` is the minimum Singleton-then-HostedService
pattern, consistent with `K8sLeaseElection` registration at lines 239-241 of
`ServiceCollectionExtensions.cs`.

### Pattern 1: ICommandChannel Interface

**What:** Minimal interface — only `Writer` and `Reader` properties. No `Complete` or
`WaitForDrainAsync` because the CONTEXT.md decision is to stop immediately on shutdown
(no drain). `GracefulShutdownService` does not need to await drain.

**Source:** Mirrors `ITrapChannel` but simplified.

```csharp
// Source: src/SnmpCollector/Pipeline/ITrapChannel.cs (simplified)
public interface ICommandChannel
{
    ChannelWriter<CommandRequest> Writer { get; }
    ChannelReader<CommandRequest> Reader { get; }
}
```

### Pattern 2: CommandChannel Implementation

**What:** Bounded channel with `DropWrite` mode so `TryWrite` returns false on overflow
(SnapshotJob handles the failure path, not the channel). `SingleWriter = false` because
multiple tenants in SnapshotJob may write concurrently. `SingleReader = true` because only
`CommandWorkerService` reads.

```csharp
// Source: mirrors TrapChannel.cs construction at lines 26-39
var options = new BoundedChannelOptions(capacity)  // capacity: 16-32
{
    FullMode = BoundedChannelFullMode.DropWrite,   // TryWrite returns false, no callback needed
    SingleWriter = false,
    SingleReader = true,
    AllowSynchronousContinuations = false,
};
_channel = Channel.CreateBounded<CommandRequest>(options);
```

**Why `DropWrite` not `Wait`:** CONTEXT.md locks "Non-blocking. Command is lost." The
`DropWrite` mode causes `TryWrite` to return false immediately without blocking, which is
exactly the semantics needed.

**Why no itemDropped callback:** Unlike `TrapChannel`, `CommandChannel` does not have an
inline drop callback because the increment is done by the caller (`SnapshotJob`) after
inspecting the `TryWrite` return value.

### Pattern 3: CommandRequest Record

**What:** Minimal record — all fields from `CommandSlotOptions` plus `DeviceName`.
Community string is NOT included — resolved at execution time from `IDeviceRegistry`.

```csharp
// Source: CONTEXT.md decision — mirrors MetricPollJob.JobDataMap pattern
public sealed record CommandRequest(
    string Ip,
    int Port,
    string CommandName,
    string Value,
    string ValueType,
    string DeviceName);
```

### Pattern 4: CommandWorkerService Drain Loop

**What:** `BackgroundService` that reads `CommandRequest` items, resolves device, builds
`Variable`, calls `SetAsync`, dispatches response varbinds via `ISender.Send`.

**Mirrors:** `ChannelConsumerService` lines 49-84 exactly — same `await foreach`,
same per-item try/catch, same correlation ID wiring, same startup log.

```csharp
// Source: src/SnmpCollector/Services/ChannelConsumerService.cs lines 49-84
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    _logger.LogInformation("Command channel worker started");

    await foreach (var req in _commandChannel.Reader.ReadAllAsync(stoppingToken))
    {
        _correlation.OperationCorrelationId = _correlation.CurrentCorrelationId;
        try
        {
            await ExecuteCommandAsync(req, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            break;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Command {CommandName} for {DeviceName} failed",
                req.CommandName, req.DeviceName);
            _pipelineMetrics.IncrementCommandFailed(req.DeviceName);
        }
    }

    _logger.LogInformation("Command channel worker completed");
}
```

### Pattern 5: SET Execution and Response Dispatch

**What:** Per-command execution — resolves OID, builds `Variable`, calls `SetAsync` with
timeout, dispatches response varbinds through full pipeline.

```csharp
// Source: MetricPollJob lines 90-101 (timeout pattern) + lines 172-185 (dispatch pattern)
private async Task ExecuteCommandAsync(CommandRequest req, CancellationToken stoppingToken)
{
    // 1. Resolve OID from command name
    var oid = _commandMapService.ResolveCommandOid(req.CommandName);
    if (oid is null)
    {
        _logger.LogWarning(
            "Command {CommandName} not found in command map for {DeviceName} -- skipping",
            req.CommandName, req.DeviceName);
        _pipelineMetrics.IncrementCommandFailed(req.DeviceName);
        return;
    }

    // 2. Resolve device for community string
    if (!_deviceRegistry.TryGetByIpPort(req.Ip, req.Port, out var device))
    {
        _logger.LogWarning(
            "Device {Ip}:{Port} not found in registry for {DeviceName} -- skipping",
            req.Ip, req.Port, req.DeviceName);
        _pipelineMetrics.IncrementCommandFailed(req.DeviceName);
        return;
    }

    // 3. Build Variable using SharpSnmpClient.ParseSnmpData
    var snmpData = SharpSnmpClient.ParseSnmpData(req.Value, req.ValueType);
    var variable = new Variable(new ObjectIdentifier(oid), snmpData);

    // 4. SET with timeout (mirrors MetricPollJob lines 92-101)
    var intervalSeconds = _snapshotJobOptions.Value.IntervalSeconds;
    var timeoutMultiplier = _snapshotJobOptions.Value.TimeoutMultiplier;
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(intervalSeconds * timeoutMultiplier));

    var endpoint = new IPEndPoint(IPAddress.Parse(device.ResolvedIp), device.Port);
    var community = new OctetString(device.CommunityString);

    var response = await _snmpClient.SetAsync(
        VersionCode.V2, endpoint, community, variable, timeoutCts.Token);

    // 5. Dispatch response varbinds (mirrors MetricPollJob.DispatchResponseAsync)
    foreach (var varbind in response)
    {
        var metricName = _commandMapService.ResolveCommandName(varbind.Id.ToString());

        var msg = new SnmpOidReceived
        {
            Oid        = varbind.Id.ToString(),
            AgentIp    = IPAddress.Parse(device.ResolvedIp),
            DeviceName = req.DeviceName,           // from CommandRequest, not device.Name
            Value      = varbind.Data,
            Source     = SnmpSource.Command,
            TypeCode   = varbind.Data.TypeCode,
            MetricName = metricName,               // pre-set if found; null triggers OidResolution fallback
        };

        await _sender.Send(msg, stoppingToken);
    }

    _pipelineMetrics.IncrementCommandSent(req.DeviceName);
}
```

### Anti-Patterns to Avoid

- **Do not call `_commandChannel.Writer.Complete()` during shutdown.** No drain is
  needed; `ReadAllAsync(stoppingToken)` exits when the token is cancelled without
  requiring the writer to complete. Calling `Complete()` on a channel that callers
  (`SnapshotJob`) are still writing to would throw `ChannelClosedException`.
- **Do not use `BoundedChannelFullMode.Wait`.** The CONTEXT.md decision is non-blocking
  `TryWrite`. `Wait` would cause the writer to block until capacity is available.
- **Do not use `BoundedChannelFullMode.DropOldest`.** Unlike traps, the failure path for
  commands is explicit: `SnapshotJob` increments `snmp.command.failed` when `TryWrite`
  returns false. `DropOldest` would silently evict items without the caller knowing.
- **Do not drain on shutdown.** CONTEXT.md: "stop immediately on cancellation — do NOT
  drain remaining commands."

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SNMP value type conversion | Custom parser | `SharpSnmpClient.ParseSnmpData(value, valueType)` | Already handles Integer32/OctetString/IpAddress with proper SharpSnmpLib types |
| Community string lookup | Store in CommandRequest | `IDeviceRegistry.TryGetByIpPort(ip, port, out device)` then `device.CommunityString` | CONTEXT.md locked decision; registry is source of truth |
| OID-to-command-name reverse lookup | Custom dictionary | `ICommandMapService.ResolveCommandName(oid)` | Existing bidirectional map with hot-reload support |
| Command-name-to-OID forward lookup | Custom dictionary | `ICommandMapService.ResolveCommandOid(commandName)` | Same service, same instance |
| SET timeout | Manual timer | `CancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(interval * multiplier))` | Exact pattern from MetricPollJob line 93 |
| Channel capacity config | New config class | Use hardcoded constant 16 or 32 in `CommandChannel` constructor | `ChannelsOptions` governs trap channel; command channel capacity is fixed-small by design |

**Key insight:** Every building block already exists in the codebase. This phase is
primarily wiring: connect `CommandRequest` → `ICommandChannel` → `CommandWorkerService`
→ `ISnmpClient.SetAsync` → `ISender.Send`.

---

## Common Pitfalls

### Pitfall 1: DeviceName from CommandRequest vs. device.Name

**What goes wrong:** Using `device.Name` (from `IDeviceRegistry`) instead of
`req.DeviceName` when constructing `SnmpOidReceived.DeviceName`.

**Why it happens:** `MetricPollJob` uses `device.Name` because it has only the device
object. `CommandWorkerService` has both, and the CONTEXT.md decision is "DeviceName set
on SnmpOidReceived from CommandRequest.DeviceName" — consistent with how the tenant
knows which device it commanded.

**How to avoid:** Always set `DeviceName = req.DeviceName` on `SnmpOidReceived`. The
registry lookup is only for `device.CommunityString` and `device.ResolvedIp`.

**Warning signs:** Test that asserts `SnmpOidReceived.DeviceName` matches the
`CommandRequest.DeviceName` fails.

### Pitfall 2: OidResolutionBehavior bypass via MetricName pre-set

**What goes wrong:** Not pre-setting `MetricName` on the `SnmpOidReceived` when the OID
is found in the command map — causing unnecessary OID map lookup.

**Why it happens:** The guard in `OidResolutionBehavior` (line 37 in
`OidResolutionBehavior.cs`): `if (msg.MetricName is not null && msg.MetricName !=
OidMapService.Unknown)` — if `MetricName` is pre-set to a non-Unknown value, the behavior
skips the OID map lookup and passes through. CONTEXT.md: "If found, pre-sets MetricName
(OidResolutionBehavior guard fires, skips metric map)."

**How to avoid:** Call `_commandMapService.ResolveCommandName(varbind.Id.ToString())`
and assign result to `msg.MetricName`. If null (not in command map), leave null — the
behavior will fall through to the metric map, getting "Unknown" if not there either.

### Pitfall 3: OperationCanceledException handling

**What goes wrong:** Catching `OperationCanceledException` with a broad catch that also
catches the SET timeout, logging it as a failure when it was actually a shutdown.

**Why it happens:** The `CancelAfter` linked CTS fires `OperationCanceledException`
with `timeoutCts.Token`. The outer loop's `stoppingToken` may also be cancelled.

**How to avoid:** In the inner `ExecuteCommandAsync`, catch `OperationCanceledException`
and check which token caused it:

```csharp
catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
{
    // Timeout (linked CTS fired) — not shutdown
    _logger.LogWarning("Command {CommandName} timed out for {DeviceName}", req.CommandName, req.DeviceName);
    _pipelineMetrics.IncrementCommandFailed(req.DeviceName);
}
// OperationCanceledException when stoppingToken IS cancelled: re-throw, let outer loop break
```

The outer `await foreach` loop has `catch (OperationCanceledException) when
(stoppingToken.IsCancellationRequested)` which handles host shutdown cleanly.

### Pitfall 4: Singleton-then-HostedService creates TWO instances

**What goes wrong:** Calling `services.AddSingleton<CommandWorkerService>()` AND
`services.AddHostedService<CommandWorkerService>()` creates two instances. The hosted
service instance runs `ExecuteAsync`, but injected consumers get the OTHER instance.

**Why it happens:** `AddHostedService<T>()` creates its own registration if `T` is not
already registered as the exact same singleton.

**How to avoid:** Use the exact pattern from `ServiceCollectionExtensions.cs` lines 239-241:

```csharp
// Source: ServiceCollectionExtensions.cs lines 239-241 (K8sLeaseElection pattern)
services.AddSingleton<CommandWorkerService>();
services.AddHostedService(sp => sp.GetRequiredService<CommandWorkerService>());
```

The `AddHostedService` delegate resolves the SAME singleton instance that DI will inject.

### Pitfall 5: Channel.CreateBounded itemDropped callback with DropWrite

**What goes wrong:** Providing an `itemDropped` callback to `Channel.CreateBounded` when
using `DropWrite` mode. With `DropWrite`, the callback is NOT invoked — the channel
returns false from `TryWrite` and no callback fires.

**Why it happens:** The callback pattern is used by `TrapChannel` with `DropOldest` mode
where the channel itself evicts items asynchronously. With `DropWrite`, the writer (caller)
is responsible for handling the failure.

**How to avoid:** Do not pass an `itemDropped` callback to `CommandChannel`. The
`SnapshotJob` caller checks `TryWrite` return value and increments the counter directly.

---

## Code Examples

### CommandRequest Record

```csharp
// All fields from CommandSlotOptions + DeviceName for counter tags
// Community string excluded — resolved at execution time
public sealed record CommandRequest(
    string Ip,
    int Port,
    string CommandName,
    string Value,
    string ValueType,
    string DeviceName);
```

### ICommandChannel Interface

```csharp
// Source pattern: ITrapChannel.cs (simplified — no Complete/WaitForDrainAsync)
public interface ICommandChannel
{
    ChannelWriter<CommandRequest> Writer { get; }
    ChannelReader<CommandRequest> Reader { get; }
}
```

### CommandChannel Implementation

```csharp
// Source pattern: TrapChannel.cs constructor (lines 20-41)
public sealed class CommandChannel : ICommandChannel
{
    private readonly Channel<CommandRequest> _channel;

    public CommandChannel()
    {
        var options = new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false,
        };
        _channel = Channel.CreateBounded<CommandRequest>(options);
    }

    public ChannelWriter<CommandRequest> Writer => _channel.Writer;
    public ChannelReader<CommandRequest> Reader => _channel.Reader;
}
```

### DI Registration in AddSnmpPipeline

```csharp
// Source pattern: ServiceCollectionExtensions.cs lines 239-241 (K8sLeaseElection)
// and lines 414 (ITrapChannel registration)
services.AddSingleton<ICommandChannel, CommandChannel>();
services.AddSingleton<CommandWorkerService>();
services.AddHostedService(sp => sp.GetRequiredService<CommandWorkerService>());
```

### SnapshotJob Enqueue Pattern (for reference — Phase 48)

```csharp
// CONTEXT.md: TryWrite failure → increment snmp.command.failed, log Warning
if (!_commandChannel.Writer.TryWrite(request))
{
    _logger.LogWarning(
        "Command channel full — command {CommandName} for {DeviceName} dropped",
        cmd.CommandName, deviceName);
    _pipelineMetrics.IncrementCommandFailed(deviceName);
}
```

---

## Verified Facts

### ICommandMapService.ResolveCommandName exists and is correct

`ICommandMapService.cs` line 16: `string? ResolveCommandName(string oid)` — forward-resolves
OID to command name. Returns null if not found. This is what the worker calls on response
varbinds to pre-set `MetricName`.

`ICommandMapService.cs` line 24: `string? ResolveCommandOid(string commandName)` — reverse-
resolves command name to OID. This is what the worker calls at execution start to get the
OID for the SET variable.

### SnapshotJobOptions.IntervalSeconds and TimeoutMultiplier are both accessible

`SnapshotJobOptions.cs` line 17: `public int IntervalSeconds { get; set; } = 15`
`SnapshotJobOptions.cs` line 23: `public double TimeoutMultiplier { get; set; } = 0.8`
Default timeout = 15 × 0.8 = 12 seconds. Already bound and registered with
`ValidateOnStart` in `AddSnmpConfiguration` (line 211-214).

### CommandSlotOptions.Ip+Port device existence is already validated at load time

`TenantVectorWatcherService.cs` lines 296-303: Commands with `Ip:Port` not found in
`IDeviceRegistry` are skipped with `LogError` at tenant load time. "Device not found" at
execution time should never occur per CONTEXT.md. The worker still needs a defensive
`TryGetByIpPort` call for robustness, but can log Warning (not Error) and increment
`snmp.command.failed`.

### SnmpSource.Command already defined

`SnmpSource.cs` line 8: `Command` is already a member of the enum. No enum addition needed.

### PipelineMetricService counters already registered

`PipelineMetricService.cs` lines 54-55: `_commandSent` and `_commandFailed` counters
(`snmp.command.sent` and `snmp.command.failed`) are already created in the constructor.
`IncrementCommandSent(deviceName)` and `IncrementCommandFailed(deviceName)` methods are
at lines 149-156.

### SharpSnmpClient.ParseSnmpData is a public static method

`SharpSnmpClient.cs` lines 36-42: `public static ISnmpData ParseSnmpData(string value,
string valueType)` — handles `Integer32`, `OctetString`, `IpAddress`. Throws
`ArgumentException` for unsupported types. Worker can call this directly (it is static)
or inject `ISnmpClient` and use the concrete cast (worker should inject `ISnmpClient`,
not the concrete type; use a helper or cast).

**Note:** `ParseSnmpData` is on the concrete `SharpSnmpClient`, not on `ISnmpClient`.
The worker needs to either call it as a static method directly (`SharpSnmpClient.ParseSnmpData`)
or have a separate helper. Calling a static method of the concrete type from a service
that uses the interface is acceptable since parsing logic is not SNMP-protocol behavior.

### ISnmpClient.SetAsync signature

`ISnmpClient.cs` line 26-31: Takes `(VersionCode version, IPEndPoint endpoint,
OctetString community, Variable variable, CancellationToken ct)` — single `Variable`
(not a list). `SharpSnmpClient` wraps it in `new List<Variable> { variable }` internally.

---

## Test Patterns

### Unit test structure mirrors ChannelConsumerServiceTests

`ChannelConsumerServiceTests.cs` is the direct analogue. Tests needed for
`CommandWorkerService`:

1. **Dispatches SET and calls ISender.Send** — happy path
2. **Sets Source=Command on dispatched SnmpOidReceived**
3. **Sets DeviceName from CommandRequest (not device.Name)**
4. **Pre-sets MetricName when OID found in command map**
5. **Leaves MetricName null when OID not in command map**
6. **Exception in SetAsync continues to next command** (error isolation)
7. **OID not in command map → increments snmp.command.failed, skips**
8. **Device not found → increments snmp.command.failed, skips**
9. **IncrementCommandSent on success**

Test infrastructure pattern (from `ChannelConsumerServiceTests`):
- `[Collection(NonParallelCollection.Name)]` — required for `MeterListener`
- `PrimedCommandChannel` stub (unbounded, pre-loaded, pre-completed)
- `CapturingSender` stub (reuse from `ChannelConsumerServiceTests` or copy)
- `StubSnmpClient` for `SetAsync` (mirrors `MetricPollJobTests.StubSnmpClient`)

---

## State of the Art

| Old Approach | Current Approach | Notes |
|--------------|------------------|-------|
| N/A | `await foreach (ReadAllAsync(stoppingToken))` | Standard .NET Channels consumer pattern; already used by `ChannelConsumerService` |

---

## Open Questions

1. **CommandChannel capacity: hardcoded 16 or IOptions-configurable?**
   - What we know: CONTEXT.md says "small bounded (16-32)". `ChannelsOptions.BoundedCapacity`
     controls the trap channel separately.
   - What's unclear: Whether a new `CommandChannelOptions` section is desired, or
     hardcoded is acceptable.
   - Recommendation: Hardcode 16 in `CommandChannel` constructor for Phase 47. A config
     option can be added later if needed. Keeps the implementation minimal.

2. **Whether to expose ICommandWorkerService or just CommandWorkerService directly**
   - What we know: CONTEXT.md says "Singleton-then-HostedService DI pattern" but does
     not mention an interface.
   - What's unclear: Whether SnapshotJob (Phase 48) needs to inject the worker or only
     the channel.
   - Recommendation: Do not create `ICommandWorkerService`. SnapshotJob injects
     `ICommandChannel` to enqueue commands — it does not interact with the worker directly.
     The worker is an internal implementation detail.

---

## Sources

### Primary (HIGH confidence — direct source code inspection)

- `src/SnmpCollector/Pipeline/ITrapChannel.cs` — interface pattern to mirror
- `src/SnmpCollector/Pipeline/TrapChannel.cs` — bounded channel implementation pattern
- `src/SnmpCollector/Services/ChannelConsumerService.cs` — drain loop, error handling, correlation ID
- `src/SnmpCollector/Jobs/MetricPollJob.cs` — SET timeout pattern (line 93), SnmpOidReceived construction (lines 172-185)
- `src/SnmpCollector/Pipeline/ICommandMapService.cs` — `ResolveCommandName` and `ResolveCommandOid` signatures verified
- `src/SnmpCollector/Pipeline/IDeviceRegistry.cs` — `TryGetByIpPort` signature verified
- `src/SnmpCollector/Pipeline/ISnmpClient.cs` — `SetAsync` signature verified
- `src/SnmpCollector/Pipeline/SharpSnmpClient.cs` — `ParseSnmpData` static method verified
- `src/SnmpCollector/Pipeline/SnmpSource.cs` — `Command` enum member verified
- `src/SnmpCollector/Configuration/SnapshotJobOptions.cs` — `IntervalSeconds` and `TimeoutMultiplier` verified
- `src/SnmpCollector/Configuration/CommandSlotOptions.cs` — all fields verified
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — `IncrementCommandSent`/`IncrementCommandFailed` verified
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — K8sLeaseElection DI pattern (lines 239-241) verified
- `src/SnmpCollector/Services/TenantVectorWatcherService.cs` — CommandSlot Ip+Port validation already exists (lines 296-303)
- `src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs` — pre-set MetricName bypass guard verified (line 37)
- `tests/SnmpCollector.Tests/Services/ChannelConsumerServiceTests.cs` — test infrastructure pattern verified

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all types verified by direct source inspection
- Architecture: HIGH — patterns copied directly from existing implementations
- Pitfalls: HIGH — derived from actual code paths, not speculation
- DI registration: HIGH — exact lines of existing patterns identified

**Research date:** 2026-03-16
**Valid until:** 2026-04-16 (stable codebase; patterns won't change unless prior phases modify these files)
