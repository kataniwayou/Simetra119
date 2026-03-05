# Phase 6: Poll Scheduling - Research

**Researched:** 2026-03-05
**Domain:** Quartz.NET 3.15.1 job scheduling, SharpSnmpLib 12.5.7 SNMP GET, MediatR ISender.Send, per-device failure tracking
**Confidence:** HIGH

---

## Summary

Phase 6 adds Quartz-driven SNMP GET polling to SnmpCollector. The goal is a `MetricPollJob` (Quartz `IJob` with `[DisallowConcurrentExecution]`) that fires per configured poll group, issues a single SNMP GET with all OIDs (plus sysUpTime prepended), dispatches each returned varbind individually via `ISender.Send()`, and marks devices unreachable after 3 consecutive failures — while continuing to poll on schedule for immediate recovery detection.

The Simetra reference project (`src/Simetra/Jobs/MetricPollJob.cs` and its scheduling extension) is the authoritative reference for this phase. It demonstrates the exact pattern needed: `UsingJobData` to pass `deviceName` and `intervalSeconds` through the `JobDataMap`, `CancellationTokenSource.CreateLinkedTokenSource` with `CancelAfter(intervalSeconds * 0.8)` for timeout, and `Messenger.GetAsync()` with a `CancellationToken`. The Simetra reference uses `maxConcurrency: jobCount` (1:1 ratio) for thread pool sizing, which is the confirmed formula.

The key difference from the Simetra reference: SnmpCollector's poll job dispatches **directly to MediatR** (no channels, no intermediary service) — one `ISender.Send()` per varbind. The sysUpTime OID (`1.3.6.1.2.1.1.3.0`) must be prepended to every GET request so the counter delta engine gets uptime context atomically. Unreachability tracking lives in a `ConcurrentDictionary<string, int>` per-device failure counter, managed inside the job.

**Primary recommendation:** Build `MetricPollJob` directly from the Simetra reference pattern, with these SnmpCollector-specific adaptations: (1) prepend sysUpTime OID to all GET requests, (2) dispatch via `ISender.Send()` directly instead of through channels, (3) add per-device consecutive failure tracking with `DeviceUnreachabilityTracker` singleton, (4) add two new counters to `PipelineMetricService` (`snmp.poll.unreachable`, `snmp.poll.recovered`).

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Quartz.Extensions.Hosting | 3.15.1 | `IJob`, `[DisallowConcurrentExecution]`, `AddQuartz`, `UseDefaultThreadPool`, `JobDataMap` | Already in `SnmpCollector.csproj`; Quartz job infrastructure locked by project |
| Lextm.SharpSnmpLib | 12.5.7 | `Messenger.GetAsync()`, `VersionCode.V2`, `OctetString`, `Variable`, `ObjectIdentifier` | Already in project; SNMP GET is the polling mechanism |
| MediatR | 12.5.0 | `ISender.Send()` — dispatches each varbind through behavior pipeline | Already in project; ISender is the only dispatch path |

### No New Packages Required

Phase 6 adds no new NuGet references. All required packages are already in `SnmpCollector.csproj`.

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.Collections.Concurrent` | BCL (.NET 9) | `ConcurrentDictionary<string, int>` for per-device failure counters | In `DeviceUnreachabilityTracker` singleton |
| `Microsoft.Extensions.Logging` | 9.0.0 | `ILogger<MetricPollJob>` for Warning/Information logging | Already in project; used for timeout warnings, unreachability transitions |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `Messenger.GetAsync()` | `Messenger.GetBulkAsync()` | GetBulk retrieves up to N OIDs per packet (SNMPv2c feature), but with a fixed OID list per poll group, regular GET is simpler and more predictable |
| `ConcurrentDictionary<string, int>` for failure counts | Per-job field | Singleton tracker allows inspection, reuse, testing; per-job field is lost if job is recreated |
| Inline failure count in `MetricPollJob` | Separate `DeviceUnreachabilityTracker` singleton | Separate service keeps job lean and makes testing easier |

---

## Architecture Patterns

### Recommended Project Structure (new files Phase 6 adds)

```
src/SnmpCollector/
├── Jobs/
│   └── MetricPollJob.cs               # IJob + [DisallowConcurrentExecution] — SNMP GET + ISender.Send
├── Pipeline/
│   └── DeviceUnreachabilityTracker.cs # Singleton — consecutive failure count + transition detection
└── Telemetry/
    └── PipelineMetricService.cs       # MODIFIED — add snmp.poll.unreachable + snmp.poll.recovered counters
Extensions/
    └── ServiceCollectionExtensions.cs # MODIFIED — AddSnmpScheduling: register MetricPollJob, thread pool, counters
```

### Pattern 1: MetricPollJob with DisallowConcurrentExecution

**What:** Quartz `IJob` decorated with `[DisallowConcurrentExecution]`. When a trigger fires while the previous execution is still running, Quartz skips the fire (misfire) rather than queuing a second execution. This prevents pile-up when a slow device holds the job for the full interval.

**When to use:** Any job that should not have overlapping executions for the same job key. Since each device/pollIndex combination is its own job key (`metric-poll-{deviceName}-{pollIndex}`), `DisallowConcurrentExecution` means: "this specific device's this specific poll group cannot execute twice simultaneously."

**From the Simetra reference (read directly):**

```csharp
// Source: src/Simetra/Jobs/MetricPollJob.cs
[DisallowConcurrentExecution]
public sealed class MetricPollJob : IJob
{
    // ... DI constructor injection

    public async Task Execute(IJobExecutionContext context)
    {
        var deviceName = context.MergedJobDataMap.GetString("deviceName")!;
        var intervalSeconds = context.MergedJobDataMap.GetInt("intervalSeconds");
        var pollIndex = context.MergedJobDataMap.GetInt("pollIndex");  // SnmpCollector: use int index

        // ...

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(intervalSeconds * 0.8));

        IList<Variable> response = await Messenger.GetAsync(
            VersionCode.V2,
            endpoint,
            community,
            variables,
            timeoutCts.Token);

        // ... dispatch each variable via ISender.Send()
    }
}
```

**SnmpCollector adaptations from the Simetra reference:**
- Replace `pollKey` (string) with `pollIndex` (int) — SnmpCollector uses index-based poll group identity
- Replace `_extractor.Extract()` + `_coordinator.Process()` with direct `ISender.Send()` per varbind
- Remove `_pollRegistry` dependency — OIDs come from `DeviceInfo.PollGroups[pollIndex].Oids` (DeviceRegistry already has them)
- Remove `ILivenessVectorService` — Phase 8 concern, not yet needed
- Add sysUpTime OID prepend
- Add `DeviceUnreachabilityTracker` for failure tracking

### Pattern 2: JobDataMap for Device/Poll Identity

**What:** Pass `deviceName`, `pollIndex`, and `intervalSeconds` through Quartz's `JobDataMap` so the job can look up the device and poll group at execution time.

**Why `deviceName` and `pollIndex` instead of embedding the OID list:** The OID list lives in `DeviceRegistry` (already loaded at startup). Passing the lookup key keeps the `JobDataMap` small and avoids serializing potentially large OID arrays. The job resolves the full `DeviceInfo` at execution time.

```csharp
// Source: src/Simetra/Extensions/ServiceCollectionExtensions.cs (adapted for SnmpCollector)
var jobKey = new JobKey($"metric-poll-{device.Name}-{pollGroup.PollIndex}");
q.AddJob<MetricPollJob>(j => j
    .WithIdentity(jobKey)
    .UsingJobData("deviceName", device.Name)
    .UsingJobData("pollIndex", pollGroup.PollIndex)
    .UsingJobData("intervalSeconds", pollGroup.IntervalSeconds));

q.AddTrigger(t => t
    .ForJob(jobKey)
    .WithIdentity($"metric-poll-{device.Name}-{pollGroup.PollIndex}-trigger")
    .StartNow()
    .WithSimpleSchedule(s => s
        .WithIntervalInSeconds(pollGroup.IntervalSeconds)
        .RepeatForever()
        .WithMisfireHandlingInstructionNextWithRemainingCount()));
```

**JobDataMap access in Execute:**
```csharp
var deviceName = context.MergedJobDataMap.GetString("deviceName")!;
var pollIndex = context.MergedJobDataMap.GetInt("pollIndex");
var intervalSeconds = context.MergedJobDataMap.GetInt("intervalSeconds");
```

### Pattern 3: Thread Pool Sizing Formula

**What:** `maxConcurrency` should equal the total job count so every job can run simultaneously when all polls fire at the same second.

**Verified from Simetra reference (read directly — HIGH confidence):**
```csharp
// Source: src/Simetra/Extensions/ServiceCollectionExtensions.cs:416-427
// Formula: start with static job count, add all dynamic jobs
var jobCount = 1; // correlation job (Phase 6 doesn't add heartbeat yet)
foreach (var device in devicesOptions.Devices)
{
    jobCount += device.MetricPolls.Count; // one job per poll group
}

services.AddQuartz(q =>
{
    q.UseInMemoryStore();
    q.UseDefaultThreadPool(maxConcurrency: jobCount);
    // ...
});
```

**For SnmpCollector Phase 6:**
- Static jobs: 1 (CorrelationJob already registered)
- Dynamic jobs: sum of `device.MetricPolls.Count` across all devices
- Formula: `jobCount = 1 + devices.Sum(d => d.MetricPolls.Count)`

**Why 1:1 ratio works:** With `[DisallowConcurrentExecution]`, each job occupies a thread only during its active SNMP GET + dispatch window. Even if all polls fire simultaneously at the start of a new interval, the thread pool of `jobCount` ensures every job gets a thread immediately. The Quartz `DefaultThreadPool` dispatches tasks to the .NET thread pool — tasks exceeding `maxConcurrency` wait for an available slot. Setting `maxConcurrency = jobCount` prevents any job from waiting.

**Startup log (locked decision):**
```csharp
_logger.LogInformation(
    "Registered {N} poll jobs across {M} devices, thread pool size: {T}",
    jobCount - 1,  // subtract CorrelationJob
    devicesOptions.Devices.Count,
    jobCount);
```

### Pattern 4: SNMP GET with sysUpTime Prepend

**What:** sysUpTime OID (`1.3.6.1.2.1.1.3.0`) is prepended to the OID list for every GET request. The response is then parsed: if the first variable has OID `1.3.6.1.2.1.1.3.0` and type `TimeTicks`, extract its value as `SysUpTimeCentiseconds` on the `SnmpOidReceived` messages. All other variables dispatch normally.

**Why sysUpTime first in the list:** SNMP GET responses return variables in the same order as the request. Prepending ensures the sysUpTime value is atomically bundled with the metric values in the same GET response — no separate GET needed.

**SharpSnmpLib API — verified:**
```csharp
// Source: src/Simetra/Jobs/MetricPollJob.cs (adapted) — HIGH confidence
private const string SysUpTimeOid = "1.3.6.1.2.1.1.3.0";

// Build request variable list: sysUpTime first, then poll group OIDs
var variables = new List<Variable>
{
    new Variable(new ObjectIdentifier(SysUpTimeOid))
};
variables.AddRange(pollGroup.Oids.Select(oid =>
    new Variable(new ObjectIdentifier(oid))));

var endpoint = new IPEndPoint(IPAddress.Parse(device.IpAddress), 161);
var community = new OctetString(device.CommunityString);

using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
    context.CancellationToken);
timeoutCts.CancelAfter(TimeSpan.FromSeconds(intervalSeconds * 0.8));

IList<Variable> response = await Messenger.GetAsync(
    VersionCode.V2,
    endpoint,
    community,
    variables,
    timeoutCts.Token);
```

**Extracting sysUpTime from response:**
```csharp
uint? sysUpTime = null;
foreach (var variable in response)
{
    if (variable.Id.ToString() == SysUpTimeOid
        && variable.Data.TypeCode == SnmpType.TimeTicks)
    {
        sysUpTime = ((TimeTicks)variable.Data).ToUInt32();
        continue;  // don't dispatch sysUpTime as a metric varbind (or do — it's in OidMap as "sysUpTime")
    }

    // Dispatch each non-sysUpTime variable as SnmpOidReceived
    var msg = new SnmpOidReceived
    {
        Oid        = variable.Id.ToString(),
        AgentIp    = IPAddress.Parse(device.IpAddress),
        DeviceName = device.Name,
        Value      = variable.Data,
        Source     = SnmpSource.Poll,
        TypeCode   = variable.Data.TypeCode,
        SysUpTimeCentiseconds = sysUpTime,
    };
    await _sender.Send(msg, context.CancellationToken);
}
```

**Note on sysUpTime dispatch:** The CONTEXT.md says sysUpTime is "prepended to every poll's OID list" — it is polled alongside the other OIDs. Whether to dispatch it as its own `SnmpOidReceived` is a judgment call. Since `sysUpTime` is in the OID map as `"sysUpTime"` (visible in `appsettings.Development.json`), dispatching it is correct and enables sysUpTime to be visible in Prometheus as a gauge. The sysUpTime value is extracted from the first position and set on all subsequent `SnmpOidReceived` messages in the same poll response.

### Pattern 5: DeviceUnreachabilityTracker Singleton

**What:** A singleton service that tracks consecutive failure counts per device and detects unreachable/recovered transitions.

**Why a singleton:** The failure count must persist across job executions (the job is stateless — Quartz re-instantiates it for each execution if using DI, or reuses it if the class is a singleton service). A singleton tracker ensures the count accumulates correctly.

**Interface:**
```csharp
namespace SnmpCollector.Pipeline;

/// <summary>
/// Tracks consecutive poll failures per device for unreachability detection.
/// Thread-safe: multiple jobs may call simultaneously (different devices).
/// </summary>
public interface IDeviceUnreachabilityTracker
{
    /// <summary>
    /// Record a poll failure for the device. Returns true on the TRANSITION to unreachable
    /// (i.e., the Nth consecutive failure where N == threshold). Returns false otherwise.
    /// </summary>
    bool RecordFailure(string deviceName);

    /// <summary>
    /// Record a poll success for the device. Returns true on TRANSITION from unreachable to
    /// healthy (i.e., device was marked unreachable, now recovered). Returns false if device
    /// was already healthy.
    /// </summary>
    bool RecordSuccess(string deviceName);

    /// <summary>
    /// Returns the current consecutive failure count for the device.
    /// </summary>
    int GetFailureCount(string deviceName);

    /// <summary>
    /// Returns true if the device is currently in the unreachable state.
    /// </summary>
    bool IsUnreachable(string deviceName);
}
```

**Implementation using ConcurrentDictionary:**
```csharp
// Internal state per device: (consecutiveFailures, isUnreachable)
private readonly ConcurrentDictionary<string, (int Count, bool IsUnreachable)> _state = new();
private readonly int _threshold;  // = 3 (locked decision)

public bool RecordFailure(string deviceName)
{
    bool transitioned = false;

    _state.AddOrUpdate(deviceName,
        addValueFactory: _ =>
        {
            // First failure — count = 1, not yet unreachable (threshold is 3)
            transitioned = (1 >= _threshold);
            return (1, transitioned);
        },
        updateValueFactory: (_, current) =>
        {
            var newCount = current.Count + 1;
            var wasAlreadyUnreachable = current.IsUnreachable;
            var nowUnreachable = newCount >= _threshold;
            transitioned = nowUnreachable && !wasAlreadyUnreachable;
            return (newCount, nowUnreachable);
        });

    return transitioned;
}

public bool RecordSuccess(string deviceName)
{
    bool recovered = false;

    _state.AddOrUpdate(deviceName,
        addValueFactory: _ => (0, false),
        updateValueFactory: (_, current) =>
        {
            recovered = current.IsUnreachable;
            return (0, false);
        });

    return recovered;
}
```

**Note on ConcurrentDictionary with struct tuples:** The `AddOrUpdate` overload with `updateValueFactory` is atomic per key. The struct `(int Count, bool IsUnreachable)` is small enough for value-type semantics. The lambda approach is correct because `AddOrUpdate` guarantees the factory runs at most once and the result is stored atomically.

### Pattern 6: Failure/Recovery Logging and Counter Increments

**What:** When a device transitions to unreachable, log at Warning and increment `snmp.poll.unreachable`. When it recovers, log at Information and increment `snmp.poll.recovered`.

**Locked decisions from CONTEXT.md:**
- Unreachable log message: `"Device {Name} ({Ip}) unreachable after {N} consecutive failures"`
- Recovery: `_logger.LogInformation("Device {Name} ({Ip}) recovered")`
- Counter per event: `snmp.poll.unreachable` on transition to unreachable, `snmp.poll.recovered` on transition back to healthy

**PipelineMetricService additions:**
```csharp
// New counters to add to PipelineMetricService:
private readonly Counter<long> _pollUnreachable;
private readonly Counter<long> _pollRecovered;

// In constructor:
_pollUnreachable = _meter.CreateCounter<long>("snmp.poll.unreachable");
_pollRecovered   = _meter.CreateCounter<long>("snmp.poll.recovered");

// New public methods:
public void IncrementPollUnreachable()
    => _pollUnreachable.Add(1, new TagList { { "site_name", _siteName } });

public void IncrementPollRecovered()
    => _pollRecovered.Add(1, new TagList { { "site_name", _siteName } });
```

### Pattern 7: Timeout and Failure Handling in Execute

**What:** The job must distinguish three failure modes: (1) timeout (`OperationCanceledException` from the 80%-of-interval CTS, not from the host shutdown CTS), (2) network/SNMP error, (3) successful poll. All failure modes increment `snmp.poll.executed` (locked decision: "after every completed poll regardless of success/failure") and update the unreachability tracker.

**Complete Execute structure:**

```csharp
public async Task Execute(IJobExecutionContext context)
{
    var jobKey = context.JobDetail.Key.Name;
    var deviceName = context.MergedJobDataMap.GetString("deviceName")!;
    var pollIndex = context.MergedJobDataMap.GetInt("pollIndex");
    var intervalSeconds = context.MergedJobDataMap.GetInt("intervalSeconds");

    try
    {
        // ... device lookup, build variables, create timeout CTS ...

        IList<Variable> response = await Messenger.GetAsync(
            VersionCode.V2,
            endpoint,
            community,
            variables,
            timeoutCts.Token);

        // Extract sysUpTime, dispatch each varbind via ISender.Send
        // ...

        // On success: reset failure counter, detect recovery
        if (_unreachabilityTracker.RecordSuccess(deviceName))
        {
            _logger.LogInformation(
                "Device {Name} ({Ip}) recovered",
                device.Name, device.IpAddress);
            _pipelineMetrics.IncrementPollRecovered();
        }
    }
    catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
    {
        // Timeout (80% of interval elapsed) — NOT host shutdown
        _logger.LogWarning(
            "Poll job {JobKey} timed out waiting for SNMP response",
            jobKey);

        RecordPollFailure(deviceName, device);
    }
    catch (OperationCanceledException)
    {
        // Host shutdown — rethrow so Quartz knows the job was interrupted
        throw;
    }
    catch (Exception ex)
    {
        // Network error, SNMP protocol error, etc.
        _logger.LogWarning(ex,
            "Poll job {JobKey} failed: {Message}",
            jobKey, ex.Message);

        RecordPollFailure(deviceName, device);
    }
    finally
    {
        // snmp.poll.executed always increments (locked decision SC#4)
        _pipelineMetrics.IncrementPollExecuted();
    }
}

private void RecordPollFailure(string deviceName, DeviceInfo device)
{
    if (_unreachabilityTracker.RecordFailure(deviceName))
    {
        var failureCount = _unreachabilityTracker.GetFailureCount(deviceName);
        _logger.LogWarning(
            "Device {Name} ({Ip}) unreachable after {N} consecutive failures",
            device.Name, device.IpAddress, failureCount);
        _pipelineMetrics.IncrementPollUnreachable();
    }
}
```

**Key: `when (!context.CancellationToken.IsCancellationRequested)`** is the correct guard to distinguish timeout from host shutdown. The linked CTS fires the timeout; the job's own `CancellationToken` fires on host shutdown. This pattern is from the Simetra reference (read directly — HIGH confidence).

### Pattern 8: noSuchObject / noSuchInstance Handling

**What:** When a device returns `noSuchObject` (SnmpType 128) or `noSuchInstance` (SnmpType 129) for an OID, the variable is still present in the response but with an error type code.

**CONTEXT.md decision:** "Claude's Discretion" — no locked decision.

**Recommendation:** Skip the dispatch of `noSuchObject` and `noSuchInstance` varbinds silently. Rationale:
- Dispatching them would reach `ValidationBehavior` which does not reject by TypeCode. They would reach `OtelMetricHandler` which dispatches based on TypeCode. `OtelMetricHandler` has no case for TypeCode 128 or 129 — the result would be a rejected/unknown metric.
- Silently skipping at the job level is cleaner: it treats a "missing OID" response as a partial response, consistent with the "partial responses: publish what we got" decision.
- If needed for debugging, a Debug-level log per skipped OID is appropriate.

```csharp
// In response processing loop:
if (variable.Data.TypeCode is SnmpType.NoSuchObject or SnmpType.NoSuchInstance or SnmpType.EndOfMibView)
{
    _logger.LogDebug(
        "OID {Oid} returned {TypeCode} from {DeviceName} — skipping",
        variable.Id.ToString(), variable.Data.TypeCode, deviceName);
    continue;
}
```

### Pattern 9: AddSnmpScheduling Modification

**What:** Extend the existing `AddSnmpScheduling()` in `ServiceCollectionExtensions.cs` to:
1. Register `DeviceUnreachabilityTracker` as a singleton
2. Pre-calculate total job count from device config
3. Set `UseDefaultThreadPool(maxConcurrency: jobCount)`
4. Register `MetricPollJob` for each device/poll group

**Critical:** `devicesOptions` must be read from `IConfiguration` directly (not via `IOptions<>`) because `AddSnmpScheduling` runs before the DI container is built. This matches the existing pattern in the Simetra reference: `configuration.GetSection(DevicesOptions.SectionName).Bind(devicesOptions.Devices)`.

```csharp
public static IServiceCollection AddSnmpScheduling(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // Register ICorrelationService (already done in Phase 1)
    services.AddSingleton<ICorrelationService, RotatingCorrelationService>();

    // Phase 6: Register unreachability tracker
    services.AddSingleton<IDeviceUnreachabilityTracker, DeviceUnreachabilityTracker>();

    // Read device config directly (DI not built yet)
    var devicesOptions = new DevicesOptions();
    configuration.GetSection(DevicesOptions.SectionName).Bind(devicesOptions.Devices);

    var correlationOptions = new CorrelationJobOptions();
    configuration.GetSection(CorrelationJobOptions.SectionName).Bind(correlationOptions);

    // Calculate total job count: 1 correlation + all poll groups
    var jobCount = 1; // CorrelationJob
    foreach (var device in devicesOptions.Devices)
        jobCount += device.MetricPolls.Count;

    services.AddQuartz(q =>
    {
        q.UseInMemoryStore();
        q.UseDefaultThreadPool(maxConcurrency: jobCount);

        // CorrelationJob (existing)
        var correlationKey = new JobKey("correlation");
        q.AddJob<CorrelationJob>(j => j.WithIdentity(correlationKey));
        q.AddTrigger(t => t
            .ForJob(correlationKey)
            .WithIdentity("correlation-trigger")
            .StartNow()
            .WithSimpleSchedule(s => s
                .WithIntervalInSeconds(correlationOptions.IntervalSeconds)
                .RepeatForever()
                .WithMisfireHandlingInstructionNextWithRemainingCount()));

        // MetricPollJob per device per poll group
        foreach (var device in devicesOptions.Devices)
        {
            foreach (var poll in device.MetricPolls)
            {
                var pollIndex = device.MetricPolls.IndexOf(poll);
                var jobKey = new JobKey(
                    $"metric-poll-{device.Name}-{pollIndex}");
                q.AddJob<MetricPollJob>(j => j
                    .WithIdentity(jobKey)
                    .UsingJobData("deviceName", device.Name)
                    .UsingJobData("pollIndex", pollIndex)
                    .UsingJobData("intervalSeconds", poll.IntervalSeconds));

                q.AddTrigger(t => t
                    .ForJob(jobKey)
                    .WithIdentity($"metric-poll-{device.Name}-{pollIndex}-trigger")
                    .StartNow()
                    .WithSimpleSchedule(s => s
                        .WithIntervalInSeconds(poll.IntervalSeconds)
                        .RepeatForever()
                        .WithMisfireHandlingInstructionNextWithRemainingCount()));
            }
        }
    });

    services.AddQuartzHostedService(options =>
    {
        options.WaitForJobsToComplete = true;
    });

    return services;
}
```

**Note on `device.MetricPolls.IndexOf(poll)`:** This works because `MetricPolls` is `List<MetricPollOptions>`. However, it's cleaner to use a `for` loop with explicit index:

```csharp
for (var i = 0; i < device.MetricPolls.Count; i++)
{
    var poll = device.MetricPolls[i];
    var jobKey = new JobKey($"metric-poll-{device.Name}-{i}");
    q.AddJob<MetricPollJob>(j => j
        .WithIdentity(jobKey)
        .UsingJobData("deviceName", device.Name)
        .UsingJobData("pollIndex", i)
        .UsingJobData("intervalSeconds", poll.IntervalSeconds));
    // ... trigger
}
```

### Anti-Patterns to Avoid

- **Using `IPublisher.Publish()` instead of `ISender.Send()`:** `SnmpOidReceived : IRequest<Unit>`, not `INotification`. `IPublisher.Publish()` bypasses all `IPipelineBehavior` behaviors — no logging, no validation, no OID resolution. Use `ISender.Send()`.
- **Not linking the timeout CTS to `context.CancellationToken`:** If only a standalone `CancellationTokenSource` is used for the timeout, host shutdown will not cancel the SNMP GET. Always use `CreateLinkedTokenSource(context.CancellationToken)`.
- **Catching `OperationCanceledException` without checking `context.CancellationToken`:** Catching all `OperationCanceledException` and treating them as timeouts will suppress host shutdown signals. Quartz cannot cleanly stop the job. The `when (!context.CancellationToken.IsCancellationRequested)` guard is mandatory.
- **Setting `maxConcurrency` to a hardcoded constant:** The number of jobs changes when devices are added to config. Always calculate from the actual job count at registration time.
- **Dispatching `noSuchObject`/`noSuchInstance` varbinds:** These are SNMP protocol-level error types (SnmpType 128, 129). The `OtelMetricHandler` has no case for them and they will produce no metric. Silently skip them at the job level rather than letting them fall through the pipeline.
- **Storing per-device state in the job instance:** Quartz DI integration creates a new job instance per execution (scoped lifetime default). Any state stored in job instance fields is lost between executions. Use a singleton tracker.
- **Sending `DeviceName = null` on `SnmpOidReceived`:** The `ValidationBehavior` rejects messages where `DeviceName` is null and the device IP is not in the registry. Since poll jobs know the device name at registration time, always set `DeviceName = device.Name` on every constructed `SnmpOidReceived`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SNMP GET with timeout | Manual UDP socket with select() | `Messenger.GetAsync(token)` + `CancelAfter()` | `Messenger.GetAsync` handles request ID, response matching, retry; CancellationToken propagates cleanly |
| Concurrent execution prevention | Lock per job key | `[DisallowConcurrentExecution]` on `IJob` | Quartz-native attribute; lock approach would deadlock if the job takes longer than the interval |
| Thread pool for jobs | `ThreadPool.SetMinThreads()` | `q.UseDefaultThreadPool(maxConcurrency: N)` | Quartz's pool governs job scheduling concurrency independently from .NET ThreadPool |
| Per-device failure count | Per-job instance field | `ConcurrentDictionary` in singleton tracker | Job instances may be recreated; singleton persists across invocations |
| Partial response handling | Filtering at pipeline level | Skip at job level before `ISender.Send()` | Avoids unnecessary `ISender.Send()` calls for error-type varbinds that will produce no metric |

**Key insight:** The Simetra reference project already solved this exact problem. Read `src/Simetra/Jobs/MetricPollJob.cs` and `src/Simetra/Extensions/ServiceCollectionExtensions.cs` (the `AddScheduling` method) before writing any new code. The SnmpCollector implementation is a simplification of the reference — fewer dependencies, direct `ISender.Send()` instead of extractor+coordinator, no liveness vector.

---

## Common Pitfalls

### Pitfall 1: OperationCanceledException Catch Ordering

**What goes wrong:** A single `catch (OperationCanceledException)` that treats all cancellations as timeouts. Host shutdown sends cancellation via `context.CancellationToken` — catching it as a "timeout" prevents Quartz from cleanly terminating the job.

**Why it happens:** Both timeout and shutdown throw `OperationCanceledException`. Without the `when` guard, they're indistinguishable.

**How to avoid:**
```csharp
// CORRECT: two separate catch blocks with guard
catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
{
    // Timeout — log Warning, record failure, continue
}
catch (OperationCanceledException)
{
    // Host shutdown — rethrow
    throw;
}
```

**Warning signs:** Application takes longer than expected to shut down; Quartz logs "job did not complete during shutdown" warnings.

### Pitfall 2: DevicesOptions Binding in AddSnmpScheduling

**What goes wrong:** Trying to use `IOptions<DevicesOptions>` inside `AddSnmpScheduling()` to iterate devices for job registration. The DI container is not yet built when `AddQuartz` lambda executes — `IOptions<T>` cannot be resolved via `GetRequiredService`.

**Why it happens:** `services.AddQuartz(q => {...})` is a configuration callback that runs during host building, before the DI container is built.

**How to avoid:** Bind directly from `IConfiguration`:
```csharp
var devicesOptions = new DevicesOptions();
configuration.GetSection(DevicesOptions.SectionName).Bind(devicesOptions.Devices);
```
This is the existing pattern already used in `AddSnmpScheduling` for `CorrelationJobOptions`. Verified from `src/Simetra/Extensions/ServiceCollectionExtensions.cs` (read directly — HIGH confidence).

### Pitfall 3: SimpleTrigger Misfire — WithMisfireHandlingInstructionNextWithRemainingCount

**What goes wrong:** Using `WithMisfireHandlingInstructionDoNothing()` on a `SimpleTrigger`. `DoNothing` is only available on `CronTrigger`, not `SimpleTrigger`.

**Why it happens:** The misfire instruction names differ between trigger types. "DoNothing" sounds correct for "skip missed fires," but calling it on a `SimpleTrigger` throws a runtime exception.

**How to avoid:** On all `SimpleTrigger` (`WithSimpleSchedule()`), use:
```csharp
.WithMisfireHandlingInstructionNextWithRemainingCount()
```
For indefinite `RepeatForever` triggers, this provides semantics equivalent to "skip missed fires, wait for next scheduled time." This is verified in the Simetra reference code comment (`NOTE on misfire handling (SCHED-10)`).

### Pitfall 4: Job Instance Lifecycle (Scoped vs Singleton)

**What goes wrong:** Storing mutable state in a `MetricPollJob` instance field, expecting it to persist between executions. Quartz's DI integration creates a new instance per execution when using the Microsoft DI integration (scoped lifetime).

**Why it happens:** In Quartz's Java origins, jobs are re-created per execution. The .NET `Quartz.Extensions.Hosting` DI integration follows this pattern by default.

**How to avoid:** Keep `MetricPollJob` fields to injected services only (all singletons or scoped services from DI). Any state that must persist between executions (failure counts, last execution timestamps) belongs in a singleton service injected into the job.

### Pitfall 5: sysUpTime OID in the OID Map

**What goes wrong:** Prepending sysUpTime OID to every GET request, then dispatching it as a regular `SnmpOidReceived`. If `1.3.6.1.2.1.1.3.0` is not in the OID map, `OidResolutionBehavior` sets `MetricName = "Unknown"`. The sysUpTime value would appear in Prometheus as `metric_name="Unknown"`, polluting the Unknown bucket.

**How to avoid:** sysUpTime (`1.3.6.1.2.1.1.3.0`) is already in `appsettings.Development.json` as `"sysUpTime"`. Ensure it's in production `OidMap` config. Alternatively, extract the sysUpTime value from the response but do NOT dispatch it as an `SnmpOidReceived` — just use it as context for the counter delta engine. The CONTEXT.md decision is ambiguous on this: "polled in the same GET request as OIDs." Either approach is valid; dispatching is consistent with "publish what we got" but requires it to be in the OID map.

**Recommended approach:** Dispatch sysUpTime as a regular varbind (it will be recorded as `snmp_gauge` with `metric_name="sysUpTime"`) and also set `SysUpTimeCentiseconds` on all other `SnmpOidReceived` messages from the same GET. This is maximally consistent with the "one SnmpOidReceived per OID" decision.

### Pitfall 6: `Null` Variable.Data on noSuchObject/noSuchInstance

**What goes wrong:** Calling `variable.Data.TypeCode` on a varbind where the device returned `noSuchObject`. In SharpSnmpLib 12.5.7, `noSuchObject` and `noSuchInstance` are represented as `ISnmpData` instances with `TypeCode` 128 and 129 respectively — not as `null`. Accessing `.TypeCode` is safe.

**How to avoid:** Check TypeCode before dispatch:
```csharp
if (variable.Data.TypeCode is SnmpType.NoSuchObject or SnmpType.NoSuchInstance or SnmpType.EndOfMibView)
    continue;
```
Do NOT null-check `variable.Data` — it is never null in SharpSnmpLib 12.5.7 GET responses.

**Confidence:** MEDIUM — verified from SharpSnmpLib SnmpType enum values (128, 129, 130) via official docs. The non-null guarantee is inferred from the SharpSnmpLib type system where `ISnmpData` implementations are value objects; not explicitly documented.

### Pitfall 7: DeviceRegistry Lookup Failure in Execute

**What goes wrong:** The `DeviceRegistry` is built at startup from config. If `IDeviceRegistry.TryGetDeviceByName()` returns false inside `Execute`, it means the device name in `JobDataMap` doesn't match any registered device. This should never happen in production (job names are derived from config) but can occur in tests if the registry and job data are out of sync.

**How to avoid:** After looking up the device, if not found — log a Warning with the job key and return (don't throw). The job will still increment `snmp.poll.executed` in `finally` (or should it? A device-not-found condition is a configuration error, not a poll failure). Recommendation: return early before the `try` block so the `finally` does NOT increment `snmp.poll.executed` — the job effectively did nothing. Log at Warning.

---

## Code Examples

### Complete MetricPollJob Structure

```csharp
// Source: adapted from src/Simetra/Jobs/MetricPollJob.cs (read directly — HIGH confidence)
// Adapted for SnmpCollector's direct-dispatch pattern
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;
using System.Net;

namespace SnmpCollector.Jobs;

[DisallowConcurrentExecution]
public sealed class MetricPollJob : IJob
{
    private const string SysUpTimeOid = "1.3.6.1.2.1.1.3.0";

    private readonly IDeviceRegistry _deviceRegistry;
    private readonly IDeviceUnreachabilityTracker _unreachabilityTracker;
    private readonly ISender _sender;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly ILogger<MetricPollJob> _logger;

    public MetricPollJob(
        IDeviceRegistry deviceRegistry,
        IDeviceUnreachabilityTracker unreachabilityTracker,
        ISender sender,
        PipelineMetricService pipelineMetrics,
        ILogger<MetricPollJob> logger)
    {
        _deviceRegistry = deviceRegistry;
        _unreachabilityTracker = unreachabilityTracker;
        _sender = sender;
        _pipelineMetrics = pipelineMetrics;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var jobKey = context.JobDetail.Key.Name;
        var deviceName = context.MergedJobDataMap.GetString("deviceName")!;
        var pollIndex = context.MergedJobDataMap.GetInt("pollIndex");
        var intervalSeconds = context.MergedJobDataMap.GetInt("intervalSeconds");

        // Device lookup — configuration error if not found
        if (!_deviceRegistry.TryGetDeviceByName(deviceName, out var device))
        {
            _logger.LogWarning(
                "Poll job {JobKey}: device {DeviceName} not found in registry",
                jobKey, deviceName);
            return;  // Do NOT increment snmp.poll.executed — job did nothing
        }

        var pollGroup = device.PollGroups[pollIndex];

        // Build request: sysUpTime first, then configured OIDs
        var variables = new List<Variable>
        {
            new Variable(new ObjectIdentifier(SysUpTimeOid))
        };
        variables.AddRange(pollGroup.Oids
            .Select(oid => new Variable(new ObjectIdentifier(oid))));

        var endpoint = new IPEndPoint(IPAddress.Parse(device.IpAddress), 161);
        var community = new OctetString(device.CommunityString);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
                context.CancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(intervalSeconds * 0.8));

            var response = await Messenger.GetAsync(
                VersionCode.V2,
                endpoint,
                community,
                variables,
                timeoutCts.Token);

            await DispatchResponseAsync(response, device, context.CancellationToken);

            // Success: reset failure counter, detect recovery
            if (_unreachabilityTracker.RecordSuccess(deviceName))
            {
                _logger.LogInformation(
                    "Device {Name} ({Ip}) recovered",
                    device.Name, device.IpAddress);
                _pipelineMetrics.IncrementPollRecovered();
            }
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            // Timeout (80% of interval) — not host shutdown
            _logger.LogWarning(
                "Poll job {JobKey} timed out waiting for SNMP response",
                jobKey);
            RecordFailure(deviceName, device);
        }
        catch (OperationCanceledException)
        {
            throw;  // Host shutdown — do not swallow
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Poll job {JobKey} failed: {Message}", jobKey, ex.Message);
            RecordFailure(deviceName, device);
        }
        finally
        {
            _pipelineMetrics.IncrementPollExecuted();  // always, per SC#4
        }
    }

    private async Task DispatchResponseAsync(
        IList<Variable> response,
        DeviceInfo device,
        CancellationToken ct)
    {
        uint? sysUpTime = null;

        foreach (var variable in response)
        {
            // Extract sysUpTime if present as first variable
            if (variable.Id.ToString() == SysUpTimeOid
                && variable.Data.TypeCode == SnmpType.TimeTicks)
            {
                sysUpTime = ((TimeTicks)variable.Data).ToUInt32();
                // Also dispatch as regular varbind so it appears in Prometheus
            }

            // Skip SNMP error types — don't dispatch noSuchObject/noSuchInstance
            if (variable.Data.TypeCode is SnmpType.NoSuchObject
                or SnmpType.NoSuchInstance
                or SnmpType.EndOfMibView)
            {
                _logger.LogDebug(
                    "OID {Oid} returned {TypeCode} from {DeviceName} — skipping",
                    variable.Id.ToString(), variable.Data.TypeCode, device.Name);
                continue;
            }

            var msg = new SnmpOidReceived
            {
                Oid        = variable.Id.ToString(),
                AgentIp    = IPAddress.Parse(device.IpAddress),
                DeviceName = device.Name,
                Value      = variable.Data,
                Source     = SnmpSource.Poll,
                TypeCode   = variable.Data.TypeCode,
                SysUpTimeCentiseconds = sysUpTime,
            };

            await _sender.Send(msg, ct);
        }
    }

    private void RecordFailure(string deviceName, DeviceInfo device)
    {
        if (_unreachabilityTracker.RecordFailure(deviceName))
        {
            var failureCount = _unreachabilityTracker.GetFailureCount(deviceName);
            _logger.LogWarning(
                "Device {Name} ({Ip}) unreachable after {N} consecutive failures",
                device.Name, device.IpAddress, failureCount);
            _pipelineMetrics.IncrementPollUnreachable();
        }
    }
}
```

### DeviceUnreachabilityTracker

```csharp
// New file: src/SnmpCollector/Pipeline/DeviceUnreachabilityTracker.cs
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Pipeline;

public sealed class DeviceUnreachabilityTracker : IDeviceUnreachabilityTracker
{
    private readonly int _threshold;  // = 3 (locked decision)

    // Per-device state: (ConsecutiveFailures, IsUnreachable)
    private readonly ConcurrentDictionary<string, DeviceState> _state = new(
        StringComparer.OrdinalIgnoreCase);

    public DeviceUnreachabilityTracker()
    {
        _threshold = 3;  // hardcoded per locked decision; could be IOptions-injected if needed
    }

    public bool RecordFailure(string deviceName)
    {
        var state = _state.GetOrAdd(deviceName, _ => new DeviceState());
        return state.RecordFailure(_threshold);
    }

    public bool RecordSuccess(string deviceName)
    {
        var state = _state.GetOrAdd(deviceName, _ => new DeviceState());
        return state.RecordSuccess();
    }

    public int GetFailureCount(string deviceName)
    {
        return _state.TryGetValue(deviceName, out var state) ? state.Count : 0;
    }

    public bool IsUnreachable(string deviceName)
    {
        return _state.TryGetValue(deviceName, out var state) && state.IsUnreachable;
    }

    // Inner class to avoid struct-in-ConcurrentDictionary atomicity issues
    private sealed class DeviceState
    {
        private volatile int _count;
        private volatile bool _isUnreachable;

        public int Count => _count;
        public bool IsUnreachable => _isUnreachable;

        public bool RecordFailure(int threshold)
        {
            var newCount = Interlocked.Increment(ref _count);
            if (newCount >= threshold && !_isUnreachable)
            {
                _isUnreachable = true;
                return true;  // transition to unreachable
            }
            return false;
        }

        public bool RecordSuccess()
        {
            Interlocked.Exchange(ref _count, 0);
            if (_isUnreachable)
            {
                _isUnreachable = false;
                return true;  // transition to recovered
            }
            return false;
        }
    }
}
```

**Concurrency note:** `DeviceState` uses `volatile int` + `Interlocked.Increment` for the count, and `volatile bool` for `_isUnreachable`. There is a tiny window between incrementing `_count` and setting `_isUnreachable = true` where another thread could observe `_count >= threshold` but `_isUnreachable == false`. For this use case (per-device, `[DisallowConcurrentExecution]` on the job), two threads cannot call `RecordFailure` for the same device simultaneously. The volatile approach is sufficient.

### AddSnmpScheduling with Thread Pool and Startup Log

```csharp
// Extend existing AddSnmpScheduling in ServiceCollectionExtensions.cs
// Key additions:
// 1. Register DeviceUnreachabilityTracker before AddQuartz
// 2. Calculate jobCount from device config
// 3. UseDefaultThreadPool(maxConcurrency: jobCount)
// 4. Register MetricPollJob for each device/poll group
// 5. Log startup summary after the host starts (in Program.cs or IHostedLifecycleService)

services.AddSingleton<IDeviceUnreachabilityTracker, DeviceUnreachabilityTracker>();

var devicesOptions = new DevicesOptions();
configuration.GetSection(DevicesOptions.SectionName).Bind(devicesOptions.Devices);

var jobCount = 1; // CorrelationJob
foreach (var device in devicesOptions.Devices)
    jobCount += device.MetricPolls.Count;

services.AddQuartz(q =>
{
    q.UseInMemoryStore();
    q.UseDefaultThreadPool(maxConcurrency: jobCount);

    // Correlation job (existing, unchanged)
    // ...

    // Dynamic: MetricPollJob per device per poll group
    for (var di = 0; di < devicesOptions.Devices.Count; di++)
    {
        var device = devicesOptions.Devices[di];
        for (var pi = 0; pi < device.MetricPolls.Count; pi++)
        {
            var poll = device.MetricPolls[pi];
            var jobKey = new JobKey($"metric-poll-{device.Name}-{pi}");
            q.AddJob<MetricPollJob>(j => j
                .WithIdentity(jobKey)
                .UsingJobData("deviceName", device.Name)
                .UsingJobData("pollIndex", pi)
                .UsingJobData("intervalSeconds", poll.IntervalSeconds));

            q.AddTrigger(t => t
                .ForJob(jobKey)
                .WithIdentity($"metric-poll-{device.Name}-{pi}-trigger")
                .StartNow()
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(poll.IntervalSeconds)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionNextWithRemainingCount()));
        }
    }
});
```

**Startup log:** The "Registered {N} poll jobs across {M} devices, thread pool size: {T}" log belongs in a `IHostedLifecycleService.StartingAsync()` (fired before Quartz starts) or in a startup service. It can be emitted from within `AddSnmpScheduling` via a transient `IHostedService` that logs at start and stops immediately, but the cleaner approach is a new `PollSchedulerStartupService` (IHostedService) that reads the same values and logs at `StartAsync` time.

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Quartz `JobChainingJobListener` for dependencies | Independent jobs per poll group, all `RepeatForever` | Quartz 3.x | No job chaining needed; each poll group is fully independent |
| `Quartz.Simpl.DefaultThreadPool` with separate thread-per-job | `DefaultThreadPool` backed by .NET `ThreadPool` (task-based) | Quartz 3.x | Thread count = task semaphore count, not literal threads; lighter weight |
| Manual SNMP request/response matching | `Messenger.GetAsync(token)` | SharpSnmpLib 8.x+ | CancellationToken support for timeout; request ID matching is internal |
| `WithMisfireHandlingInstructionDoNothing()` on SimpleTrigger | `WithMisfireHandlingInstructionNextWithRemainingCount()` | Always: DoNothing is CronTrigger only | Runtime exception if used on SimpleTrigger |

**Current and verified (HIGH confidence):**
- Quartz 3.15.1 `UseDefaultThreadPool(maxConcurrency: N)` — confirmed from reference code and official docs
- `Messenger.GetAsync(VersionCode.V2, endpoint, community, variables, token)` — confirmed from SharpSnmpLib official API docs
- `SnmpType.NoSuchObject = 128`, `SnmpType.NoSuchInstance = 129` — confirmed from SharpSnmpLib official SnmpType enum docs

---

## Open Questions

1. **sysUpTime dispatch: separate varbind or context-only?**
   - What we know: CONTEXT.md says "polled in the same GET request as OIDs." The Development appsettings has `"1.3.6.1.2.1.1.3.0": "sysUpTime"` in the OID map.
   - What's unclear: Should the sysUpTime varbind be dispatched as its own `SnmpOidReceived` (appearing in Prometheus as `metric_name="sysUpTime"`, `snmp_gauge`) or only extracted as context for the delta engine and not dispatched?
   - Recommendation: Dispatch it. It's in the OID map, it's a valid `TimeTicks` gauge value, and dispatching it is maximally consistent with "publish what we got." Set `SysUpTimeCentiseconds` on all subsequent `SnmpOidReceived` in the same GET response.

2. **Startup log placement: where to emit "Registered N poll jobs"?**
   - What we know: The log must fire at startup (Information level, locked decision). `AddSnmpScheduling` runs before DI is built.
   - What's unclear: The count is known at registration time (inside the `AddSnmpScheduling` lambda), but `ILogger` is not available there. Options: (a) a new `PollSchedulerStartupService : IHostedService` that reads device count from `IDeviceRegistry` and logs at `StartAsync`, or (b) a `PostConfigureOptions` or `IHostedLifecycleService`.
   - Recommendation: A small `PollSchedulerStartupService : IHostedService` that injects `IDeviceRegistry` and logs at `StartAsync`. It reads `AllDevices` (already populated by the registry), sums `PollGroups.Count` across devices, and logs with the thread pool size. Register it in `AddSnmpScheduling` after `AddQuartzHostedService`.

3. **`Messenger.GetAsync` exception type on timeout**
   - What we know: The Simetra reference catches `OperationCanceledException when (!context.CancellationToken.IsCancellationRequested)` for SNMP timeout. This is the established pattern.
   - What's unclear: SharpSnmpLib official docs don't specify the exception type for CTS cancellation. Based on .NET `CancellationToken` semantics and the Simetra reference code, `OperationCanceledException` is the correct expectation.
   - Recommendation: Use the `when` guard pattern from the Simetra reference. If testing reveals a different exception (e.g., `SocketException`), add a separate catch clause.

4. **`TimeTicks.ToUInt32()` API availability**
   - What we know: `TimeTicks` is an `ISnmpData` implementation in SharpSnmpLib. `SysUpTimeCentiseconds` on `SnmpOidReceived` is `uint?`.
   - What's unclear: The exact method name for extracting the uint value from `TimeTicks`. In SharpSnmpLib 12.x, `TimeTicks` likely exposes `ToUInt32()` or a `.Value` property.
   - Recommendation: Check the SharpSnmpLib API at implementation time. The cast pattern `((TimeTicks)variable.Data).ToUInt32()` is a reasonable assumption; if the API differs, use `.ToString()` to get the string representation and parse it. LOW confidence on exact method name — verify at implementation.

---

## Sources

### Primary (HIGH confidence)

- `src/Simetra/Jobs/MetricPollJob.cs` — Complete job structure, timeout CTS pattern, OperationCanceledException guard, JobDataMap access, Messenger.GetAsync call signature (read directly)
- `src/Simetra/Extensions/ServiceCollectionExtensions.cs` — `AddScheduling()` method: jobCount calculation formula, `UseDefaultThreadPool(maxConcurrency: jobCount)`, UsingJobData pattern, trigger registration with `WithMisfireHandlingInstructionNextWithRemainingCount()`, misfire handling note (SCHED-10) (read directly)
- `src/SnmpCollector/Pipeline/SnmpOidReceived.cs` — `SysUpTimeCentiseconds` field, `DeviceName` set at poll time, `Source = SnmpSource.Poll`, `TypeCode` from `ISnmpData.TypeCode` (read directly)
- `src/SnmpCollector/Pipeline/DeviceRegistry.cs` — `TryGetDeviceByName()`, `AllDevices` (read directly)
- `src/SnmpCollector/Pipeline/DeviceInfo.cs` — `PollGroups: IReadOnlyList<MetricPollInfo>`, `CommunityString`, `IpAddress` (read directly)
- `src/SnmpCollector/Pipeline/MetricPollInfo.cs` — `PollIndex`, `Oids`, `IntervalSeconds`, `JobKey()` method (read directly)
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — Existing counter pattern, `IncrementPollExecuted()` (read directly)
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — Existing `AddSnmpScheduling()`, `AddQuartzHostedService(WaitForJobsToComplete = true)` (read directly)
- `src/SnmpCollector/Jobs/CorrelationJob.cs` — `[DisallowConcurrentExecution]` usage, `context.JobDetail.Key.Name`, Quartz IJob pattern (read directly)
- [Quartz.NET Configuration Reference](https://www.quartz-scheduler.net/documentation/quartz-3.x/configuration/reference.html) — `quartz.threadPool.maxConcurrency`, DefaultThreadPool default = 10, task-based dispatch (fetched)
- [SharpSnmpLib SnmpType Enum](https://help.sharpsnmp.com/html/T_Lextm_SharpSnmpLib_SnmpType.htm) — `NoSuchObject = 128`, `NoSuchInstance = 129`, `EndOfMibView = 130` (fetched)
- [SharpSnmpLib Messenger.GetAsync](https://help.sharpsnmp.com/html/M_Lextm_SharpSnmpLib_Messaging_Messenger_GetAsync.htm) — Method signature: `(VersionCode, IPEndPoint, OctetString, IList<Variable>, CancellationToken) -> Task<IList<Variable>>` (fetched)

### Secondary (MEDIUM confidence)

- `src/SnmpCollector/Configuration/DeviceOptions.cs` — `MetricPolls: List<MetricPollOptions>`, iteration pattern (read directly)
- `src/SnmpCollector/Configuration/MetricPollOptions.cs` — `Oids: List<string>`, `IntervalSeconds: int` (read directly)
- `src/SnmpCollector/appsettings.Development.json` — sysUpTime OID in OidMap confirms dispatch is expected (read directly)
- [Quartz.NET Microsoft DI Integration](https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/microsoft-di-integration.html) — `UsingJobData` API pattern (fetched)

### Tertiary (LOW confidence)

- `TimeTicks.ToUInt32()` method name — assumed from .NET convention and SharpSnmpLib naming patterns; verify at implementation time
- `OperationCanceledException` as the exception type from `Messenger.GetAsync` on cancellation — inferred from .NET CancellationToken semantics and Simetra reference pattern; SharpSnmpLib docs do not explicitly state the exception type

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all packages already in project; no new dependencies needed
- Architecture: HIGH — Simetra reference implementation read directly; all patterns verified from production code
- Pitfalls: HIGH — most derived from direct reference code reading and verified API docs; noSuchObject handling from official SnmpType enum
- Thread pool formula: HIGH — read directly from Simetra reference (`jobCount` variable, `UseDefaultThreadPool(maxConcurrency: jobCount)`)
- `TimeTicks.ToUInt32()` API: LOW — assumed from convention; verify at implementation time

**Research date:** 2026-03-05
**Valid until:** 2026-06-05 (stable library versions; Quartz 3.15.1, SharpSnmpLib 12.5.7, .NET 9 — 90 days reasonable)
