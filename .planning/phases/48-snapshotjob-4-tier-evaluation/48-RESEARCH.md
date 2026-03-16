# Phase 48: SnapshotJob 4-Tier Evaluation - Research

**Researched:** 2026-03-16
**Domain:** Quartz.NET job, tenant vector evaluation, priority group traversal, command dispatch
**Confidence:** HIGH — all findings from direct codebase inspection

## Summary

Phase 48 wires together prerequisite components (ITenantVectorRegistry, ISuppressionCache, ICommandChannel, SnapshotJobOptions) into a SnapshotJob Quartz job that runs a 4-tier tenant evaluation loop. All prerequisite infrastructure exists in the codebase and is fully operational; this phase adds only the job class and its Quartz registration.

The SnapshotJob follows an identical structural pattern to CorrelationJob and HeartbeatJob: `[DisallowConcurrentExecution]` attribute, capture `_correlation.OperationCorrelationId = _correlation.CurrentCorrelationId` at entry, clear it in `finally`, stamp `_liveness.Stamp(jobKey)` in `finally`. The job key name `"snapshot"` must be registered with `intervalRegistry.Register("snapshot", ...)` in `AddSnmpScheduling` so `LivenessHealthCheck` can compute the staleness threshold.

SnapshotJob does NOT exist yet — no skeleton file is present in `src/SnmpCollector/Jobs/`. The Quartz registration block for snapshot is also absent from `AddSnmpScheduling`. Both must be added in plan 48-01.

**Primary recommendation:** Model SnapshotJob structurally on HeartbeatJob (simplest existing job) with dependency injection mirroring CommandWorkerService (which already uses ITenantVectorRegistry, ICommandChannel, ISuppressionCache, PipelineMetricService, ICorrelationService, IOptions<SnapshotJobOptions>).

## Standard Stack

All dependencies are already registered and available in the DI container.

### Core
| Component | Location | Purpose | Notes |
|-----------|----------|---------|-------|
| `IJob` (Quartz) | Quartz package | Job interface | `Execute(IJobExecutionContext)` |
| `[DisallowConcurrentExecution]` | Quartz | Prevent pile-up | Applied to all existing jobs |
| `ITenantVectorRegistry` | `Pipeline/ITenantVectorRegistry.cs` | Priority-sorted tenant groups | Returns `IReadOnlyList<PriorityGroup>` via `.Groups` |
| `ISuppressionCache` | `Pipeline/ISuppressionCache.cs` | Dedup SET commands | `TrySuppress(key, windowSeconds)` — true = skip |
| `ICommandChannel` | `Pipeline/ICommandChannel.cs` | Enqueue SET commands | `Writer.TryWrite(CommandRequest)` — false = channel full |
| `ICorrelationService` | `Pipeline/ICorrelationService.cs` | Correlation ID scoping | `.OperationCorrelationId`, `.CurrentCorrelationId` |
| `ILivenessVectorService` | `Pipeline/ILivenessVectorService.cs` | Job health stamp | `.Stamp(jobKey)` in finally |
| `IJobIntervalRegistry` | `Pipeline/IJobIntervalRegistry.cs` | Liveness threshold registration | `.Register(jobKey, intervalSeconds)` at startup |
| `IOptions<SnapshotJobOptions>` | `Configuration/SnapshotJobOptions.cs` | IntervalSeconds, TimeoutMultiplier | Bound from "SnapshotJob" config section |
| `PipelineMetricService` | `Telemetry/PipelineMetricService.cs` | Increment command metrics | `IncrementCommandFailed`, `IncrementCommandSuppressed` |

### Supporting Types
| Type | Location | Key Members |
|------|----------|-------------|
| `PriorityGroup` | `Pipeline/PriorityGroup.cs` | `record(int Priority, IReadOnlyList<Tenant> Tenants)` |
| `Tenant` | `Pipeline/Tenant.cs` | `.Id`, `.Priority`, `.Holders`, `.Commands`, `.SuppressionWindowSeconds` |
| `MetricSlotHolder` | `Pipeline/MetricSlotHolder.cs` | `.ReadSlot()`, `.Role`, `.Threshold`, `.IntervalSeconds`, `.GraceMultiplier`, `.Ip`, `.Port` |
| `MetricSlot` | `Pipeline/MetricSlot.cs` | `record(double Value, string? StringValue, DateTimeOffset Timestamp)` |
| `ThresholdOptions` | `Configuration/ThresholdOptions.cs` | `double? Min`, `double? Max` |
| `CommandSlotOptions` | `Configuration/CommandSlotOptions.cs` | `.Ip`, `.Port`, `.CommandName`, `.Value`, `.ValueType` |
| `CommandRequest` | `Pipeline/CommandRequest.cs` | `record(string Ip, int Port, string CommandName, string Value, string ValueType, string DeviceName)` |

**No new packages required.** Everything is already registered.

## Architecture Patterns

### Recommended Project Structure

SnapshotJob lives in the existing Jobs folder. No new folders needed.

```
src/SnmpCollector/
├── Jobs/
│   ├── CorrelationJob.cs        (existing pattern to follow)
│   ├── HeartbeatJob.cs          (existing pattern to follow)
│   ├── MetricPollJob.cs         (existing pattern for complex jobs)
│   └── SnapshotJob.cs           [NEW - Phase 48-01]
```

### Pattern 1: Quartz Job Shell (from HeartbeatJob/CorrelationJob)

**What:** Every job captures correlation ID at entry, wraps work in try/catch, clears correlation ID and stamps liveness in finally.

**When to use:** All Quartz IJob implementations in this codebase.

```csharp
// Source: src/SnmpCollector/Jobs/CorrelationJob.cs + HeartbeatJob.cs
[DisallowConcurrentExecution]
public sealed class SnapshotJob : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        _correlation.OperationCorrelationId = _correlation.CurrentCorrelationId;
        var jobKey = context.JobDetail.Key.Name;

        try
        {
            // ... evaluation work ...
        }
        catch (OperationCanceledException)
        {
            throw; // let Quartz handle shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot job {JobKey} failed", jobKey);
        }
        finally
        {
            _liveness.Stamp(jobKey);
            _correlation.OperationCorrelationId = null;
        }
    }
}
```

### Pattern 2: Quartz Registration (from AddSnmpScheduling in ServiceCollectionExtensions.cs)

**What:** Job + trigger registration pattern. SnapshotJob follows the CorrelationJob/HeartbeatJob pattern exactly.

```csharp
// Source: src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs lines 498-524
// Inside the q => { ... } AddQuartz lambda:

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
```

The `SnapshotJobOptions` bind is already done outside the lambda in `AddSnmpConfiguration`; the local bind inside `AddSnmpScheduling` is needed because DI is not built yet (same pattern as `heartbeatOptions`).

### Pattern 3: Priority Group Traversal with Task.WhenAll

**What:** Sequential across priority groups (lower int = higher priority = processed first), parallel within each group.

```csharp
// Source: derived from ITenantVectorRegistry.Groups ordering
// "Priority-sorted groups, ascending (lowest priority value = highest priority = first group)"
foreach (var group in _registry.Groups)
{
    // Evaluate all tenants in this group in parallel
    await Task.WhenAll(group.Tenants.Select(tenant =>
        EvaluateTenantAsync(tenant, ct)));

    // Advance gate: check group results before proceeding to next group
    if (!ShouldAdvance(group, results))
        break;
}
```

### Pattern 4: Staleness Check (Tier 1)

**What:** A holder is stale if `ReadSlot()` returns null (never written), OR if the slot's timestamp is older than `IntervalSeconds * GraceMultiplier`.

```csharp
// Source: MetricSlotHolder.cs — ReadSlot() returns null before any write has occurred
var slot = holder.ReadSlot();
if (slot is null) return true; // null = skip holder (cannot judge)

var age = DateTimeOffset.UtcNow - slot.Timestamp;
var graceWindow = TimeSpan.FromSeconds(holder.IntervalSeconds * holder.GraceMultiplier);
return age > graceWindow; // stale if older than grace window
```

### Pattern 5: Threshold Violation Check (Tiers 2/3)

**What:** Strict inequality. No threshold (Min=null, Max=null) = treated as violated.

```csharp
// Source: derived from CONTEXT.md decisions + ThresholdOptions.cs
private static bool IsViolated(MetricSlotHolder holder)
{
    var slot = holder.ReadSlot();
    if (slot is null) return false; // null = skip (does not participate)

    var threshold = holder.Threshold;
    if (threshold is null || (threshold.Min is null && threshold.Max is null))
        return true; // no threshold = violated

    var value = slot.Value;
    if (threshold.Min is not null && value < threshold.Min.Value) return true;
    if (threshold.Max is not null && value > threshold.Max.Value) return true;
    return false;
}
```

### Pattern 6: Command Enqueue with Suppression Check

**What:** Before each command, check suppression cache. If not suppressed, TryWrite to channel. If TryWrite fails (channel full), increment `snmp.command.failed` and log Warning.

```csharp
// Source: ISuppressionCache.cs, ICommandChannel.cs, PipelineMetricService.cs
foreach (var cmd in tenant.Commands)
{
    var suppressionKey = $"{cmd.Ip}:{cmd.Port}:{cmd.CommandName}";
    if (_suppressionCache.TrySuppress(suppressionKey, tenant.SuppressionWindowSeconds))
    {
        _pipelineMetrics.IncrementCommandSuppressed(cmd.Ip); // device_name tag
        continue;
    }

    var request = new CommandRequest(cmd.Ip, cmd.Port, cmd.CommandName, cmd.Value, cmd.ValueType, tenant.Id);
    if (!_commandChannel.Writer.TryWrite(request))
    {
        _logger.LogWarning("Command channel full, dropping command {CommandName} for {TenantId}",
            cmd.CommandName, tenant.Id);
        _pipelineMetrics.IncrementCommandFailed(cmd.Ip);
    }
    else
    {
        _logger.LogInformation("Command dispatched: tenant={TenantId} command={CommandName} ip={Ip}:{Port}",
            tenant.Id, cmd.CommandName, cmd.Ip, cmd.Port);
    }
}
```

### Advance Gate Logic

From CONTEXT.md decisions:

```csharp
// A group advances when ALL tenants are either:
//   (a) all Resolved metrics violated → confirmed bad → move on
//   (b) NOT all Evaluate metrics violated → confirmed healthy → move on
//
// A group BLOCKS when ANY tenant is:
//   (a) actively commanding (all Evaluate violated, Resolved not all violated)
//   (b) stale (missing data)
private static bool ShouldAdvance(IReadOnlyList<TenantResult> results)
{
    foreach (var result in results)
    {
        if (result.IsStale) return false;        // stale = uncertain = block
        if (result.IsCommanding) return false;   // commanding = block
    }
    return true; // all confirmed-bad or confirmed-healthy
}
```

### Anti-Patterns to Avoid

- **Awaiting inside inner foreach:** When doing `Task.WhenAll` for parallel tenant evaluation, use `Select` + `Task.WhenAll` on the group tenants — do NOT use `foreach` with sequential `await`.
- **Checking suppression after command enqueue:** Suppression check MUST happen BEFORE `TryWrite`. The act of stamping in `TrySuppress` is conditional on returning `false` (proceed path only).
- **Using `_correlation.SetCorrelationId`:** SnapshotJob must NOT call `SetCorrelationId`. Only `CorrelationJob` writes the global ID. SnapshotJob only reads (`CurrentCorrelationId`) and scopes (`OperationCorrelationId`).
- **Stamping liveness before finally:** Always stamp in `finally` so it executes even on exception.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Dedup SET commands | Custom timestamp map | `ISuppressionCache.TrySuppress` | Already implemented and tested, handles window expiry correctly |
| Channel overflow handling | Custom bounded queue | `ICommandChannel.Writer.TryWrite` (BoundedChannelFullMode.DropWrite) | Already implemented with capacity 16, SingleWriter=false |
| Job staleness tracking | Custom timer | `ILivenessVectorService.Stamp` + `IJobIntervalRegistry.Register` | Already consumed by `LivenessHealthCheck` |
| Threshold comparison | Custom eval logic | Follow CONTEXT.md spec exactly | Edge cases are locked decisions: null threshold = violated, boundary values are in-range |
| Priority ordering | Sort tenants manually | `ITenantVectorRegistry.Groups` | Already sorted ascending (lowest int = highest priority = first) by `TenantVectorRegistry.Reload` using `SortedDictionary<int, ...>` |

**Key insight:** All suppression, channel, liveness, and priority-order logic is implemented in prerequisite phases. SnapshotJob is an orchestrator, not an implementor.

## Common Pitfalls

### Pitfall 1: Missing Quartz Registration for Liveness
**What goes wrong:** SnapshotJob stamps liveness but `LivenessHealthCheck` never detects staleness because no interval was registered.
**Why it happens:** `IJobIntervalRegistry.Register("snapshot", ...)` must be called in `AddSnmpScheduling` alongside the Quartz trigger registration.
**How to avoid:** Always pair `q.AddTrigger(...)` with `intervalRegistry.Register(jobKey, intervalSeconds)` — every existing job does this.
**Warning signs:** Liveness check never reports `snapshot` key stale even after long absence.

### Pitfall 2: Suppression Key Collisions
**What goes wrong:** Two different tenants that send the same command to the same device suppress each other.
**Why it happens:** If suppression key is only `"{Ip}:{Port}:{CommandName}"`, tenants sharing the same device share suppression state.
**How to avoid:** The CONTEXT.md implies per-tenant suppression. Use `"{TenantId}:{Ip}:{Port}:{CommandName}"` as the suppression key to isolate tenants. (Per `Tenant.SuppressionWindowSeconds` — window is tenant-scoped.)
**Warning signs:** Tenant A's command suppresses Tenant B's command to the same device.

### Pitfall 3: Task.WhenAll with Mutable Shared State
**What goes wrong:** Parallel tenant evaluations write to shared cycle counters (commanded, stale, evaluated) with data races.
**Why it happens:** Multiple `Task` lambdas capturing the same `int` variable or `List`.
**How to avoid:** Use `Interlocked.Increment` for shared counters, or collect `TenantResult` objects and aggregate after `await Task.WhenAll(...)`.

### Pitfall 4: Advance Gate Applied to First Group Prematurely
**What goes wrong:** Advance gate fires before all tenant evaluations complete within the group.
**Why it happens:** Checking advance gate during `Task.WhenAll` instead of after.
**How to avoid:** Collect all results for the group first, then apply advance gate.

### Pitfall 5: ReadSlot Null Handling Inconsistency
**What goes wrong:** Tier 1 treats null as stale (exclude from staleness check), but Tier 2/3 logic accidentally treats null as "not violated" or vice versa.
**Why it happens:** Different null semantics per tier.
**How to avoid:** Encode per-tier null behavior in a helper method that is tier-specific:
- Tier 1 null check: skip holder (not stale, excluded)
- Tier 2 (Resolved) null check: exclude from "all violated" count (holder doesn't participate)
- Tier 3 (Evaluate) null check: exclude from "all violated" count (holder doesn't participate)

## Code Examples

### Job Constructor Pattern

```csharp
// Source: src/SnmpCollector/Jobs/HeartbeatJob.cs + CorrelationJob.cs
public SnapshotJob(
    ITenantVectorRegistry registry,
    ISuppressionCache suppressionCache,
    ICommandChannel commandChannel,
    ICorrelationService correlation,
    ILivenessVectorService liveness,
    PipelineMetricService pipelineMetrics,
    IOptions<SnapshotJobOptions> options,
    ILogger<SnapshotJob> logger)
```

### MetricSlotHolder Key Fields

```csharp
// Source: src/SnmpCollector/Pipeline/MetricSlotHolder.cs
holder.ReadSlot()             // MetricSlot? — null if never written
holder.Role                   // "Evaluate" or "Resolved"
holder.Threshold              // ThresholdOptions? — null means no threshold
holder.Threshold?.Min         // double? — strict less-than violation
holder.Threshold?.Max         // double? — strict greater-than violation
holder.IntervalSeconds        // int — poll interval, used for staleness
holder.GraceMultiplier        // double — default 2.0, staleness = interval * grace
holder.Ip                     // string — device IP
holder.Port                   // int — device SNMP port
```

### Tenant Key Fields

```csharp
// Source: src/SnmpCollector/Pipeline/Tenant.cs
tenant.Id                     // string — tenant name/identifier
tenant.Priority               // int — lower = higher priority
tenant.Holders                // IReadOnlyList<MetricSlotHolder>
tenant.Commands               // IReadOnlyList<CommandSlotOptions>
tenant.SuppressionWindowSeconds  // int — suppression window duration
```

### CommandRequest Construction

```csharp
// Source: src/SnmpCollector/Pipeline/CommandRequest.cs
// DeviceName = tenant.Id (SnapshotJob uses tenant ID as device name for SET commands)
var request = new CommandRequest(
    Ip: cmd.Ip,
    Port: cmd.Port,
    CommandName: cmd.CommandName,
    Value: cmd.Value,
    ValueType: cmd.ValueType,
    DeviceName: tenant.Id);
```

### TryWrite Channel Pattern

```csharp
// Source: src/SnmpCollector/Pipeline/CommandChannel.cs — capacity 16, DropWrite mode
// False return = channel full (dropped), increment snmp.command.failed
if (!_commandChannel.Writer.TryWrite(request))
{
    _logger.LogWarning("...");
    _pipelineMetrics.IncrementCommandFailed(cmd.Ip);
}
```

### Test Stub Pattern for Job Tests

```csharp
// Source: tests/SnmpCollector.Tests/Jobs/HeartbeatJobTests.cs
// StubCorrelationService, StubLivenessVectorService, StubHeartbeatJobContext
// SnapshotJobTests should follow same pattern: stub all interfaces, assert side effects
private static IJobExecutionContext MakeContext(string jobKeyName)
{
    var jobDetail = JobBuilder.Create<SnapshotJob>()
        .WithIdentity(jobKeyName)
        .Build();
    // Return minimal stub implementing IJobExecutionContext
}
```

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| N/A — SnapshotJob is new | Register in AddSnmpScheduling, bind SnapshotJobOptions | SnapshotJobOptions already bound in AddSnmpConfiguration; AddSnmpScheduling needs local bind for thread-pool sizing (same pattern as heartbeatOptions) |
| N/A | ISuppressionCache.TrySuppress stamps on false only | Suppression window does not extend on repeated suppressed calls |

**No deprecated approaches.** All prerequisite interfaces were designed for SnapshotJob consumption.

## Open Questions

1. **DeviceName tag for IncrementCommandFailed / IncrementCommandSuppressed**
   - What we know: PipelineMetricService methods take `string deviceName` and use it as `device_name` tag. For command operations, `CommandWorkerService` uses `req.DeviceName` (which is the tenant ID in our case).
   - What's unclear: Whether SnapshotJob should pass `tenant.Id` or `cmd.Ip` as the `deviceName` parameter. CommandWorkerService uses `req.DeviceName` = tenant ID. SnapshotJob should be consistent.
   - Recommendation: Use `tenant.Id` as the `deviceName` parameter for command-related metric calls in SnapshotJob, matching the convention established by CommandWorkerService.

2. **Advance gate result collection thread safety**
   - What we know: `Task.WhenAll` runs tenant evaluations concurrently. Results must be aggregated safely.
   - What's unclear: Whether to use `ConcurrentBag<TenantResult>` or a pre-allocated array indexed by tenant index.
   - Recommendation: Pre-allocate `TenantResult[]` with one slot per tenant (index matches `group.Tenants[i]`) and assign by index — no locking needed if each task writes to a unique index.

## Sources

### Primary (HIGH confidence)
All findings are from direct codebase inspection — source of truth, not training data.

- `src/SnmpCollector/Jobs/HeartbeatJob.cs` — job shell pattern, correlation + liveness finally pattern
- `src/SnmpCollector/Jobs/CorrelationJob.cs` — minimal job pattern, correlation ID handling
- `src/SnmpCollector/Jobs/MetricPollJob.cs` — complex job with timeout, cancellation, metrics
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — Quartz registration pattern, SnapshotJobOptions already bound
- `src/SnmpCollector/Pipeline/ITenantVectorRegistry.cs` — Groups API, ascending priority ordering
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — confirms SortedDictionary ascending ordering
- `src/SnmpCollector/Pipeline/PriorityGroup.cs` — `record(int Priority, IReadOnlyList<Tenant> Tenants)`
- `src/SnmpCollector/Pipeline/Tenant.cs` — Id, Priority, Holders, Commands, SuppressionWindowSeconds
- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — ReadSlot(), all fields, null semantics
- `src/SnmpCollector/Pipeline/MetricSlot.cs` — `record(double Value, string? StringValue, DateTimeOffset Timestamp)`
- `src/SnmpCollector/Pipeline/ICommandChannel.cs` — Writer.TryWrite pattern, DropWrite semantics
- `src/SnmpCollector/Pipeline/CommandChannel.cs` — capacity 16, SingleWriter=false confirmed
- `src/SnmpCollector/Pipeline/ISuppressionCache.cs` — TrySuppress(key, windowSeconds) semantics
- `src/SnmpCollector/Pipeline/SuppressionCache.cs` — stamps only on false path
- `src/SnmpCollector/Pipeline/CommandRequest.cs` — record fields
- `src/SnmpCollector/Configuration/SnapshotJobOptions.cs` — IntervalSeconds (default 15), TimeoutMultiplier (default 0.8)
- `src/SnmpCollector/Configuration/ThresholdOptions.cs` — `double? Min`, `double? Max`
- `src/SnmpCollector/Configuration/CommandSlotOptions.cs` — Ip, Port, CommandName, Value, ValueType
- `src/SnmpCollector/Configuration/MetricSlotOptions.cs` — Role "Evaluate"/"Resolved" confirmed
- `src/SnmpCollector/Pipeline/ICorrelationService.cs` — OperationCorrelationId, CurrentCorrelationId
- `src/SnmpCollector/Pipeline/ILivenessVectorService.cs` — Stamp(jobKey)
- `src/SnmpCollector/Pipeline/IJobIntervalRegistry.cs` — Register(jobKey, intervalSeconds)
- `src/SnmpCollector/Pipeline/JobIntervalRegistry.cs` — implementation
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — IncrementCommandFailed, IncrementCommandSuppressed signatures
- `src/SnmpCollector/Services/CommandWorkerService.cs` — confirm DeviceName convention, SnapshotJobOptions usage
- `tests/SnmpCollector.Tests/Jobs/HeartbeatJobTests.cs` — test double patterns for job tests
- `tests/SnmpCollector.Tests/Pipeline/SuppressionCacheTests.cs` — confirmed suppression semantics

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all types directly inspected in source
- Architecture: HIGH — patterns copied from existing jobs
- Pitfalls: HIGH — based on real API semantics verified in source
- Advance gate logic: HIGH — directly from CONTEXT.md locked decisions

**Research date:** 2026-03-16
**Valid until:** 2026-04-16 (stable codebase, no external dependencies added)
